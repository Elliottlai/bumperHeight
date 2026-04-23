using System.Windows;
using BarcodeReader.Interfaces;
using BarcodeReader.Services;
using BarcodeReader.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BarcodeReader;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // Services
        services.AddSingleton<IDeviceEnumerator, MvDeviceEnumerator>();
        services.AddSingleton<ICodeReaderDevice, MvCodeReaderDevice>();
        services.AddSingleton<IBarcodeResultParser, MvBarcodeResultParser>();
        services.AddSingleton<ICameraParameters, CameraParameters>();

        // ViewModel
        services.AddSingleton<MainViewModel>();

        Services = services.BuildServiceProvider();

        var mainWindow = new MainWindow();
        mainWindow.Show();
    }
}
