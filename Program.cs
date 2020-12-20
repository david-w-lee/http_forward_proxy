using log4net;
using log4net.Config;
using System;
using System.IO;
using System.Reflection;

namespace http_forward_proxy
{
    class Program
    {
        static void Main(string[] args)
        {
            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));

            ServerAsync.Run();
            Console.ReadLine();
        }
    }
}
