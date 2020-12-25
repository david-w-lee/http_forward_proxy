using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Org.BouncyCastle.X509.Extension;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace http_forward_proxy
{
    public class CertUtil
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public static ConcurrentDictionary<string, System.Security.Cryptography.X509Certificates.X509Certificate> certs = new ConcurrentDictionary<string, System.Security.Cryptography.X509Certificates.X509Certificate>();

        public static string rootSubjectName = "MyCA";

        private static AsymmetricKeyParameter rootPrivateKey = null;
        public static AsymmetricKeyParameter RootPrivateKey
        {
            get
            {
                if (rootPrivateKey == null)
                    rootPrivateKey = GetPrivateAsymmetricKeyParameter("MyRootCertPrivate.key");
                return rootPrivateKey;
            }
        }

        private static AsymmetricKeyParameter rootPublicKey = null;
        public static AsymmetricKeyParameter RootPublicKey
        {
            get
            {
                if (rootPublicKey == null)
                    rootPublicKey = GetPublicAsymmetricKeyParameter("MyRootCertPublic.key");
                return rootPublicKey;
            }
        }

        private static AsymmetricKeyParameter dependentPrivateKey = null;
        public static AsymmetricKeyParameter DependentPrivateKey
        {
            get
            {
                if (dependentPrivateKey == null)
                    dependentPrivateKey = GetPrivateAsymmetricKeyParameter("MyDependentCertPrivate.key");
                return dependentPrivateKey;
            }
        }

        private static AsymmetricKeyParameter dependentPublicKey = null;
        public static AsymmetricKeyParameter DependentPublicKey
        {
            get
            {
                if (dependentPublicKey == null)
                    dependentPublicKey = GetPublicAsymmetricKeyParameter("MyDependentCertPublic.key");
                return dependentPublicKey;
            }
        }

        private static Org.BouncyCastle.X509.X509Certificate rootCert = null;
        public static Org.BouncyCastle.X509.X509Certificate RootCert
        {
            get
            {
                if (rootCert == null)
                    rootCert = GetCertFromFile("MyRootCert.crt");
                return rootCert;
            }
        }

        public static System.Security.Cryptography.X509Certificates.X509Certificate GetCert(string subjectDn)
        {
            if (!certs.ContainsKey(subjectDn))
            {
                var dependentCert = CertUtil.CreateDependentCert(subjectDn);

                byte[] dependentPkcs12 = CertUtil.CreatePkcs12(DependentPrivateKey, dependentCert, "DependentCert", string.Empty, null);// new List<Org.BouncyCastle.X509.X509Certificate>() { RootCert });
                var dependentMsCert = new X509Certificate2(dependentPkcs12, string.Empty, X509KeyStorageFlags.Exportable);

                certs[subjectDn] = dependentMsCert;
            }
            return certs[subjectDn];
        }

        public static byte[] CreatePkcs12(AsymmetricKeyParameter privateKey, Org.BouncyCastle.X509.X509Certificate cert, string name, string password, List<Org.BouncyCastle.X509.X509Certificate> chainCerts = null)
        {
            // Create a cert chain
            var certEnt = CreateChainEntry(cert, name);
            X509CertificateEntry[] chain = null;
            if (chainCerts != null)
            {
                chain = new X509CertificateEntry[1 + chainCerts.Count];
                chain[0] = certEnt;

                int i = 1;
                foreach (Org.BouncyCastle.X509.X509Certificate chainCert in chainCerts)
                {
                    log.Debug($"Cert: {chainCert.SubjectDN}");
                    certEnt = CreateChainEntry(chainCert, chainCert.SubjectDN.ToString());
                    chain[i++] = certEnt;
                }
            }
            else
            {
                chain = new X509CertificateEntry[1];
                chain[0] = certEnt;
            }

            // Save the cert chain in memory
            var builder = new Pkcs12StoreBuilder();
            var store = builder.Build();

            var keyEnt = new AsymmetricKeyEntry(privateKey);
            store.SetKeyEntry(name, keyEnt, chain);

            using (MemoryStream stream = new MemoryStream())
            {
                store.Save(stream, password.ToCharArray(), new SecureRandom());
                return stream.ToArray();
            }
        }

        private static X509CertificateEntry CreateChainEntry(Org.BouncyCastle.X509.X509Certificate cert, string name)
        {
            var attr = new Dictionary<string, DerBmpString>
            {
                {PkcsObjectIdentifiers.Pkcs9AtFriendlyName.Id, new DerBmpString(name) }
            };
            var certEnt = new X509CertificateEntry(cert, attr);
            return certEnt;
        }

        public static Org.BouncyCastle.X509.X509Certificate CreateDependentCert(string subjectDn)
        {
            // Set cert values
            BigInteger serialNumber = new BigInteger(Guid.NewGuid().ToByteArray()).Abs();

            DateTime startDate = DateTime.UtcNow.AddDays(-2);
            DateTime expiryDate = DateTime.Parse("1/1/2038");

            IDictionary attrs = new Hashtable();
            attrs.Add(X509Name.CN, $"{subjectDn}");

            IList order = new ArrayList();
            order.Add(X509Name.CN);
            X509Name subjectName = new X509Name(order, attrs);

            X509V3CertificateGenerator certGen = new X509V3CertificateGenerator();
            certGen.SetSerialNumber(serialNumber);
            certGen.SetIssuerDN(new X509Name($"cn={rootSubjectName}"));
            certGen.SetNotBefore(startDate);
            certGen.SetNotAfter(expiryDate);
            certGen.SetSubjectDN(subjectName);
            certGen.SetPublicKey(DependentPublicKey);

            certGen.AddExtension(X509Extensions.AuthorityKeyIdentifier, false, new AuthorityKeyIdentifierStructure(RootPublicKey));
            certGen.AddExtension(X509Extensions.SubjectKeyIdentifier, false, new SubjectKeyIdentifierStructure(DependentPublicKey));
            certGen.AddExtension(X509Extensions.KeyUsage, true, new X509KeyUsage((int)(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DataEncipherment)));

            ISignatureFactory signatureFactory = new Asn1SignatureFactory("SHA256WITHRSA", RootPrivateKey, new SecureRandom());
            var cert = certGen.Generate(signatureFactory);
            return cert;
        }

        private static AsymmetricKeyParameter GetPrivateAsymmetricKeyParameter(string filename)
        {
            AsymmetricCipherKeyPair keyPair;

            using (var reader = File.OpenText(filename))
                keyPair = (AsymmetricCipherKeyPair)new PemReader(reader).ReadObject();

            return keyPair.Private;
        }

        private static AsymmetricKeyParameter GetPublicAsymmetricKeyParameter(string filename)
        {
            var stream = System.IO.File.OpenText(filename);
            var pemReader = new Org.BouncyCastle.OpenSsl.PemReader(stream);
            var KeyParameter = (Org.BouncyCastle.Crypto.AsymmetricKeyParameter)pemReader.ReadObject();
            return KeyParameter;
        }

        private static Org.BouncyCastle.X509.X509Certificate GetCertFromFile(string filename)
        {
            var mscert = X509Certificate2.CreateFromCertFile(filename);
            return new Org.BouncyCastle.X509.X509CertificateParser().ReadCertificate(mscert.GetRawCertData());
        }
    }
}
