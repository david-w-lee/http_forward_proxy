using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace http_forward_proxy
{
    public class Server
    {
        TcpListener server = null;
        public Server(string ip, int port)
        {
            IPAddress localAddr = IPAddress.Parse(ip);
            server = new TcpListener(localAddr, port);
        }

        public void Start()
        {
            Console.WriteLine("Starting Server...");
            server.Start();
            try
            {
                while (true)
                {
                    Console.WriteLine("Waiting for a connection...");
                    TcpClient client = server.AcceptTcpClient();
                    Console.WriteLine("Connected!");

                    Task.Run(() => HandleConnection(client));
                }
            }
            catch (SocketException e)
            {
                Console.WriteLine("SocketException: {0}", e);
                server.Stop();
            }
        }

        public void HandleConnection(TcpClient client)
        {
            var stream = client.GetStream();
            string data = null;
            Byte[] bytes = new Byte[22400];
            int byteCount;
            TcpClient targetClient = null;
            NetworkStream targetStream = null;
            try
            {
                bool flag = false;
                byteCount = stream.Read(bytes, 0, bytes.Length);
                Console.WriteLine("55------------------------------------------------");
                data = Encoding.UTF8.GetString(bytes, 0, byteCount);
                Console.WriteLine($"UTF8{data}");
                if (data.StartsWith("CONNECT"))
                {
                    string[] splitted = data.Split(" ");
                    string[] splitted2 = splitted[1].Split(":");
                    string url = splitted2[0];
                    int port = int.Parse(splitted2[1]);
                    targetClient = new TcpClient();
                    targetClient.Connect(url, port);
                    targetStream = targetClient.GetStream();
                    Console.WriteLine($"Connected {url}");
                    string replyStr = $"HTTP/1.1 200 Connection Established\nProxy-agent: Dotnet Core Proxy/0.1.0 Draft 1\n\n";
                    Byte[] reply = Encoding.UTF8.GetBytes(replyStr);
                    stream.Write(reply, 0, reply.Length);
                    stream.Flush();
                    ByPass(stream, targetStream);
                }
                else
                {
                    do
                    {
                        data = Encoding.UTF8.GetString(bytes, 0, byteCount);
                        Console.WriteLine($"UTF8{data}");

                        string[] splitted = data.Split(" ");
                        string[] splitted2 = splitted[1].Split(":");
                        string url = splitted2[1].Substring(2);

                        int port = int.Parse(splitted2[2].Contains("/") ? splitted2[2].Substring(0, splitted2[2].IndexOf('/')) : splitted2[2]);
                        targetClient = new TcpClient();
                        targetClient.Connect(url, port);
                        Console.WriteLine($"url {url} port {port}");
                        targetStream = targetClient.GetStream();
                        targetStream.Write(bytes, 0, byteCount);
                        targetStream.Flush();
                        while ((byteCount = targetStream.Read(bytes, 0, bytes.Length)) != 0)
                        {
                            Console.WriteLine($"Response came back {byteCount} {Encoding.UTF8.GetString(bytes, 0, byteCount)}");

                            stream.Write(bytes, 0, byteCount);
                            stream.Flush();
                        }
                    } while ((byteCount = stream.Read(bytes, 0, bytes.Length)) != 0);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception: {0}", e);
                client.Close();
            }
        }

        public static void ByPass(Stream s1, Stream s2)
        {
            Task.Run(() => Process(s1, s2));
            Task.Run(() => Process(s2, s1));
        }

        public static void Process(Stream sIn, Stream sOut)
        {
            byte[] buf = new byte[0x10000];
            while (true)
            {
                int len = sIn.Read(buf, 0, buf.Length);
                sOut.Write(buf, 0, len);
            }
        }

        public static void Run()
        {
            Task.Run(() => {
                var myServer = new Server("127.0.0.1", 8080);
                myServer.Start();
            });
        }
    }
}
