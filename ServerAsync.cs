using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace http_forward_proxy
{
    public class ServerAsync
    {
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        TcpListener server = null;
        public ServerAsync(string ip, int port)
        {
            IPAddress localAddr = IPAddress.Parse(ip);
            server = new TcpListener(localAddr, port);
        }

        public async void Start()
        {
            log.Debug("Starting Server...");
            server.Start();
            try
            {
                while (true)
                {
                    log.Debug("Waiting for a connection...");
                    TcpClient client = await server.AcceptTcpClientAsync();
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

        public async Task HandleConnection(TcpClient client)
        {
            var stream = client.GetStream();
            string data = null;
            Byte[] bytes = new Byte[0x1000];
            int byteCount;
            TcpClient targetClient = null;
            NetworkStream targetStream = null;
            try
            {
                byteCount = await stream.ReadAsync(bytes, 0, bytes.Length);
                data = Encoding.UTF8.GetString(bytes, 0, byteCount);
                log.Debug($"UTF8{data}");
                if (data.StartsWith("CONNECT"))
                {
                    string[] splitted = data.Split(" ");
                    string[] splitted2 = splitted[1].Split(":");
                    string url = splitted2[0];
                    int port = int.Parse(splitted2[1]);
                    targetClient = new TcpClient();
                    targetClient.Connect(url, port);
                    targetStream = targetClient.GetStream();
                    log.Debug($"Connected {url}");
                    string replyStr = $"HTTP/1.1 200 Connection Established\nProxy-agent: Dotnet Core Proxy/0.1.0 Draft 1\n\n";
                    Byte[] reply = Encoding.UTF8.GetBytes(replyStr);
                    await stream.WriteAsync(reply, 0, reply.Length);
                    stream.Flush();
                    ByPass(stream, targetStream);
                }
                else
                {
                    do
                    {
                        data = Encoding.UTF8.GetString(bytes, 0, byteCount);
                        log.Debug($"UTF8{data}");

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
                        while ((byteCount = await targetStream.ReadAsync(bytes, 0, bytes.Length)) != 0)
                        {
                            log.Debug($"Response came back {byteCount} {Encoding.UTF8.GetString(bytes, 0, byteCount)}");

                            await stream.WriteAsync(bytes, 0, byteCount);
                            stream.Flush();
                        }
                    } while ((byteCount = await stream.ReadAsync(bytes, 0, bytes.Length)) != 0);
                }
            }
            catch (Exception e)
            {
                log.Debug("Exception: {0}", e);
            }
            finally
            {
                //stream.Close();
                //client.Close();
                //if (targetStream != null)
                //    targetStream.Close();
                //if (targetClient != null)
                //    targetClient.Close();
            }
        }

        public static void ByPass(Stream s1, Stream s2)
        {
            Task.Run(async () => await Process(s1, s2));
            Task.Run(async () => await Process(s2, s1));
        }

        public static async Task Process(Stream sIn, Stream sOut)
        {
            byte[] buf = new byte[0x1000];
            while (true)
            {
                int len = await sIn.ReadAsync(buf, 0, buf.Length);
                if (len == 0)
                    return;
                await sOut.WriteAsync(buf, 0, len);
            }
        }

        public static void Run()
        {
            Task.Run(() => {
                var myServer = new ServerAsync("127.0.0.1", 8080);
                myServer.Start();
            });
        }
    }
}
