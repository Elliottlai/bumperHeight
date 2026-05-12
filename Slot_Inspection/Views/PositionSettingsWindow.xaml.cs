using System.Windows;
using Slot_Inspection.ViewModels;

namespace Slot_Inspection.Views;

public partial class PositionSettingsWindow : Window
{
    public PositionSettingsWindow()
    {
        InitializeComponent();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
