using System.Windows;
using Slot_Inspection.ViewModels;

namespace Slot_Inspection;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    // ★ 程式關閉時上報 MES Disconnected
    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is MainViewModel vm)
            await vm.DisconnectMesAsync();
    }
}
