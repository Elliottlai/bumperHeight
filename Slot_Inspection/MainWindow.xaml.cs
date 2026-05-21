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

}