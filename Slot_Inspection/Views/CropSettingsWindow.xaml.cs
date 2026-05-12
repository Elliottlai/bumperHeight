using System.Windows;

namespace Slot_Inspection.Views;

public partial class CropSettingsWindow : Window
{
    public CropSettingsWindow()
    {
        InitializeComponent();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
