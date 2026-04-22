using System.Windows;
using Slot_Inspection.ViewModels;

namespace Slot_Inspection;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        // 畫面出來之後才開始初始化（不會凍住 UI）
        Loaded += async (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                await vm.InitializeAsync();
            }
        };
    }
}