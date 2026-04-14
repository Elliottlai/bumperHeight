using NLog;
using NLog.Config;
using NLog.Targets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Machine.Core
{


    public static class Nlogger
    {

        public static void CreateLogger()
        {
            var config = new LoggingConfiguration();
            var fileTarget = new FileTarget
            {
                Name = "Original",
                FileName = "${basedir}/logs/${shortdate}_${level}.log",
                Layout = "${date:format=HH\\:mm\\:ss\\.ffff} [${uppercase:${level}}] ${message}",
            };
            config.AddRule(LogLevel.Trace, LogLevel.Fatal, fileTarget, "Original");
            config.AddTarget("Original", fileTarget);


            LogManager.Configuration = config;

            LogManager.GetLogger("Original").Debug("Start=======================================================");

        }

        public static void Debug(string message)
        {
            LogManager.GetLogger("Original").Debug(message);

        }
        public static void Error(string message)
        {
            LogManager.GetLogger("Original").Error(message);

        }
        public static void Info(string message)
        {
            LogManager.GetLogger("Original").Info(message);

        }
    }
}
