using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace http_forward_proxy
{
    public class SecureServer
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        TcpListener server = null;
        public SecureServer(string ip, int port)
        {
            IPAddress localAddr = IPAddress.Parse(ip);
            server = new TcpListener(localAddr, port);
        }

        public void Start()
        {
            log.Debug("Starting Server...");
            server.Start();
            try
            {
                while (true)
                {
                    log.Debug("Waiting for a connection...");
                    TcpClient client = server.AcceptTcpClient();
                    log.Debug("Connected!");

                    Task.Run(() => HandleConnection(client));
                }
            }
            catch (SocketException e)
            {
                log.Debug("SocketException: {0}", e);
                server.Stop();
            }
        }

        public void HandleConnection(TcpClient client)
        {
            log.Debug($"Thread({Thread.CurrentThread.ManagedThreadId}) New Connection");
            var stream = client.GetStream();
            string data = null;
            Byte[] bytes = new Byte[22400];
            int byteCount;
            TcpClient targetClient = null;
            NetworkStream targetStream = null;
            try
            {
                log.Debug($"Thread({Thread.CurrentThread.ManagedThreadId}) BEGIN: Reading first data");
                byteCount = stream.Read(bytes, 0, bytes.Length);
                log.Debug($"Thread({Thread.CurrentThread.ManagedThreadId}) END: Reading first data");
                data = Encoding.UTF8.GetString(bytes, 0, byteCount);

                log.Debug($"Thread({Thread.CurrentThread.ManagedThreadId}) UTF8{data}");
                if (data.StartsWith("CONNECT"))
                {
                    string[] splitted = data.Split(" ");
                    string[] splitted2 = splitted[1].Split(":");
                    string url = splitted2[0];
                    int port = int.Parse(splitted2[1]);

                    string replyStr = $"HTTP/1.1 200 Connection Established\nProxy-agent: Dotnet Core Proxy/0.1.0 Draft 1\n\n";
                    Byte[] reply = Encoding.UTF8.GetBytes(replyStr);
                    stream.Write(reply, 0, reply.Length);
                    stream.Flush();

                    SslStream sslStream = new SslStream(stream, true, (a, b, c, d) => true);
                    //X509Certificate2
                    sslStream.AuthenticateAsServer(CertUtil.GetCert(url), false, false);

                    targetClient = new TcpClient();
                    targetClient.Connect(url, port);
                    targetStream = targetClient.GetStream();
                    SslStream sslTargetStream = new SslStream(targetStream);
                    sslTargetStream.AuthenticateAsClient(url);
                    log.Debug($"Thread({Thread.CurrentThread.ManagedThreadId}) Connected {url}");


                    //StringBuilder sb = new StringBuilder();
                    //int bufferCount = 0;
                    //byte[] buffer = new byte[1024];
                    //do
                    //{
                    //    bufferCount = sslStream.Read(buffer, 0, buffer.Length);
                    //    sb.Append(Encoding.UTF8.GetString(buffer, 0, bufferCount));
                    //} while (bufferCount != 0);
                    //string input = sb.ToString();
                    //string firstLine = input.Substring(0, input.IndexOf("\n"));

                    //splitted = data.Split(" ");
                    //splitted2 = splitted[1].Split(":");
                    //url = splitted2[0];
                    //port = int.Parse(splitted2[1]);

                    //IPAddress address = Dns.GetHostAddresses("url")[0];


                    ByPass($"Thread({Thread.CurrentThread.ManagedThreadId}) Url({url})", sslStream, sslTargetStream);
                }
                else
                {
                    do
                    {
                        data = Encoding.UTF8.GetString(bytes, 0, byteCount);
                        log.Debug($"Thread({Thread.CurrentThread.ManagedThreadId}) UTF8{data}");

                        string[] splitted = data.Split(" ");
                        string[] splitted2 = splitted[1].Split(":");
                        string url = splitted2[1].Substring(2);

                        int port = int.Parse(splitted2[2].Contains("/") ? splitted2[2].Substring(0, splitted2[2].IndexOf('/')) : splitted2[2]);
                        targetClient = new TcpClient();
                        targetClient.Connect(url, port);
                        log.Debug($"url {url} port {port}");
                        targetStream = targetClient.GetStream();
                        targetStream.Write(bytes, 0, byteCount);
                        targetStream.Flush();
                        while ((byteCount = targetStream.Read(bytes, 0, bytes.Length)) != 0)
                        {
                            log.Debug($"Response came back {byteCount} {Encoding.UTF8.GetString(bytes, 0, byteCount)}");

                            stream.Write(bytes, 0, byteCount);
                            stream.Flush();
                        }
                    } while ((byteCount = stream.Read(bytes, 0, bytes.Length)) != 0);
                }
            }
            catch (Exception e)
            {
                log.Debug("Exception: {0}", e);
                client.Close();
            }
        }

        public static void ByPass(string id, Stream s1, Stream s2)
        {
            Task.Run(() => Process(id + "Direction(s1->s2)", s1, s2));
            Task.Run(() => Process(id + "Direction(s2->s1)", s2, s1));
        }

        public static async Task Process(string id, Stream sIn, Stream sOut)
        {
            byte[] buf = new byte[0x10000];
            while (true)
            {
                log.Debug($"{id} Trying to read");
                int len = await sIn.ReadAsync(buf, 0, buf.Length);
                // Normally NetworkStream.Read() or NetworkStream.ReadAsync() is blocked until new data is available.
                // When len == 0, it means the connection has been closed.
                if (len == 0)
                {
                    return;
                }
                
                log.Debug($"{id} Read {len}");
                await sOut.WriteAsync(buf, 0, len);
                log.Debug($"{id} Wrote {len}");
            }
        }

        public static void Run()
        {
            Task.Run(() => {
                var myServer = new SecureServer("127.0.0.1", 8080);
                myServer.Start();
            });
        }
    }
}
