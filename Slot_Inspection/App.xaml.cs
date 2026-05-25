using System.Windows;
using System.Windows.Threading;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Slot_Inspection
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // ── 1. 初始化 NLog（純程式碼設定，不依賴 NLog.config 檔案）──
            InitNLog();

            // ── 2. 全域例外攔截（讓閃退時能看到錯誤訊息）──

            // UI 執行緒上的未處理例外
            DispatcherUnhandledException += (s, ex) =>
            {
                LogManager.GetCurrentClassLogger().Fatal(ex.Exception, "[UI] Unhandled exception");
                MessageBox.Show(
                    $"[UI 錯誤]\n{ex.Exception.GetType().Name}\n\n{ex.Exception.Message}\n\n{ex.Exception.StackTrace}",
                    "未處理例外", MessageBoxButton.OK, MessageBoxImage.Error);
                ex.Handled = true;
            };

            // 背景執行緒（Task.Run / Thread）的未處理例外
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                var err = ex.ExceptionObject as Exception;
                LogManager.GetCurrentClassLogger().Fatal(err, "[Fatal] AppDomain unhandled exception");
                MessageBox.Show(
                    $"[程式致命錯誤]\n{err?.GetType().Name}\n\n{err?.Message}\n\n{err?.StackTrace}",
                    "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            };

            // 未被觀察的 Task 例外
            TaskScheduler.UnobservedTaskException += (s, ex) =>
            {
                LogManager.GetCurrentClassLogger().Error(ex.Exception, "[Task] Unobserved task exception");
                ex.SetObserved();
            };

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            LogManager.Shutdown();
            base.OnExit(e);
        }

        /// <summary>
        /// 用程式碼初始化 NLog，輸出到執行檔旁的 logs\ 資料夾。
        /// 不依賴 NLog.config，避免工作目錄不同導致找不到設定檔。
        /// </summary>
        private static void InitNLog()
        {
            // 固定使用執行檔所在目錄，避免工作目錄差異問題
            string logDir = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "logs");

            var config = new LoggingConfiguration();

            var fileTarget = new FileTarget("logfile")
            {
                FileName = System.IO.Path.Combine(logDir, "${shortdate}.log"),
                Layout = "${date:format=HH\\:mm\\:ss.fff} [${level:uppercase=true}] ${logger:shortName=true} | ${message} ${exception:format=tostring}",
                ArchiveEvery = FileArchivePeriod.Day,
                MaxArchiveFiles = 30,
            };

            config.AddRule(LogLevel.Debug, LogLevel.Fatal, fileTarget);
            LogManager.Configuration = config;
        }
    }
}

