using System.Windows;
using BarcodeReader.Interfaces;
using BarcodeReader.Services;
using BarcodeReader.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace BarcodeReader;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        // IImageRenderer 需要 UI 控件，在 View 層建立後注入 ViewModel
        var renderer = new WpfImageRenderer(ImageDisplay);
        _viewModel = new MainViewModel(
            App.Services.GetRequiredService<IDeviceEnumerator>(),
            App.Services.GetRequiredService<ICodeReaderDevice>(),
            App.Services.GetRequiredService<IBarcodeResultParser>(),
            App.Services.GetRequiredService<ICameraParameters>(),
            renderer);

        DataContext = _viewModel;
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        _viewModel.Dispose();
    }
}