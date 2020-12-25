using log4net;
using log4net.Config;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;

namespace http_forward_proxy
{
    class Program
    {
        static void Main(string[] args)
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");

            var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
            XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));

            SecureServer.Run();
            Console.ReadLine();
        }
    }
}
