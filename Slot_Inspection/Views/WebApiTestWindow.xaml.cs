using System.Windows;
using Slot_Inspection.Services;

namespace Slot_Inspection.Views;

public partial class WebApiTestWindow : Window
{
    private CimApiClient? _mesClient;

    public WebApiTestWindow()
    {
        InitializeComponent();
    }

    /// <summary>載入目前 CimApiClient 設定值至表單。</summary>
    public void LoadFrom(CimApiClient client)
    {
        _mesClient          = client;
        ChkEnabled.IsChecked = client.IsEnabled;
        TxtBaseUrl.Text     = client.BaseUrl;
        TxtEquipmentId.Text = client.EquipmentId;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_mesClient != null)
        {
            _mesClient.IsEnabled   = ChkEnabled.IsChecked == true;
            _mesClient.BaseUrl     = TxtBaseUrl.Text.Trim();
            _mesClient.EquipmentId = TxtEquipmentId.Text.Trim();
            _mesClient.Save();
        }
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

