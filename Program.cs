using System;

namespace http_forward_proxy
{
    class Program
    {
        static void Main(string[] args)
        {
            Server.Run();
            Console.ReadLine();
        }
    }
}
