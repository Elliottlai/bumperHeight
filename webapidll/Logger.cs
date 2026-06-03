using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebApidll
{
    public sealed class Logger
    {
        /// <summary>
        /// Provides a general-purpose <see cref="Logger"/> instance.<para/>
        /// The logger stores its data under a "Logs" directory located on the first available drive, preferring D:\Logs and then C:\Logs.
        /// </summary>
        public static Logger General { get; } = new Logger(GetDefaultRootFolder());

        public string RootFolder { get; }

        public int RetentionDays { get; set; } = 30;

        private readonly Timer Timer;
        private readonly ConcurrentQueue<(string[] SubFolders, LogEventArgs Log)> LogQueue = new ConcurrentQueue<(string[] SubFolders, LogEventArgs Log)>();
        public Logger(string RootFolder)
        {
            this.RootFolder = RootFolder;
            Timer = new(OnLogging, null, Timeout.Infinite, Timeout.Infinite);
        }

        private int IsLogging = 0;
        private void OnLogging(object state)
        {
            if (Interlocked.Exchange(ref IsLogging, 1) == 1)
                return;

            try
            {
                while (LogQueue.TryDequeue(out (string[] SubFolders, LogEventArgs Log) Data))
                {
                    try
                    {
                        DateTime Time = Data.Log.Time;

                        string[] logFolderParts = new string[2 + Data.SubFolders.Length];
                        logFolderParts[0] = RootFolder;
                        logFolderParts[1] = Time.ToString("yyyyMMdd");
                        for (int i = 0; i < Data.SubFolders.Length; i++)
                            logFolderParts[2 + i] = Data.SubFolders[i];
                        string LogFolder = Path.Combine(logFolderParts);
                        if (!Directory.Exists(LogFolder))
                            Directory.CreateDirectory(LogFolder);

                        string LogFilename = Path.Combine(LogFolder, $"{Time:HH}.txt");
                        StreamWriter Writer = File.AppendText(LogFilename);
                        Writer.WriteLine(Data.Log);
                        Writer.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref IsLogging, 0);
            }
        }

        //public void Log(LogEventArgs Content)
        //    => Log(Content, []);

        public void Log(LogEventArgs Content)
            => Log(Content, Array.Empty<string>());

        public void Log(LogEventArgs Content, string[] SubFolders)
        {
            if (string.IsNullOrEmpty(RootFolder))
                return;

            DeleteExpiredFiles();
            LogQueue.Enqueue((SubFolders, Content));
            Timer.Change(0U, Timeout.Infinite);
        }

        private void DeleteExpiredFiles()
        {
            if (!Directory.Exists(RootFolder))
                return;

            foreach (string Folder in Directory.EnumerateDirectories(RootFolder))
            {
                string FolderName = Path.GetFileName(Folder);
                if (DateTime.TryParseExact(FolderName, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime FolderDate))
                {
                    if ((DateTime.Today - FolderDate).TotalDays > RetentionDays)
                    {
                        try
                        {
                            Directory.Delete(Folder, true);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        private static string GetDefaultRootFolder()
        {

            string[] PreferredDrives = new string[] { @"D:\"};
            foreach (string Drive in PreferredDrives)
                if (Directory.Exists(Drive))
                    return Path.Combine(Drive, "Logs");

            return DriveInfo.GetDrives().Where(d => d.IsReady).OrderBy(d => d.Name).FirstOrDefault() is DriveInfo AvailableDrive ?
                   Path.Combine(AvailableDrive.RootDirectory.FullName, "Logs") :
                   null;
        }

    }
}
