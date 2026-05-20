using System.Diagnostics;
using System.IO;
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

    private void ResultButton_Click(object sender, RoutedEventArgs e)
    {
        string folderPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Results");

        if (!Directory.Exists(folderPath))
        {
            MessageBox.Show("報表資料夾尚不存在。\n" + folderPath,
                "找不到資料夾", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start("explorer.exe", folderPath);
    }
}