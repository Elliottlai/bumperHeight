using System.Windows;

namespace Slot_Inspection.Views;

public partial class LightSettingsWindow : Window
{
    public LightSettingsWindow()
    {
        InitializeComponent();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
        => DialogResult = true;

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
