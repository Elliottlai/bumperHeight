using System.Windows;

namespace WebApiSever
{
    public partial class App : Application
    {
        public static WebApiHost? ApiHost { get; private set; }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            try
            {
                ApiHost = new WebApiHost("http://0.0.0.0:8069");
                await ApiHost.StartAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"API 伺服器啟動失敗：{ex.Message}", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (ApiHost != null)
                await ApiHost.StopAsync();

            base.OnExit(e);
        }
    }
}