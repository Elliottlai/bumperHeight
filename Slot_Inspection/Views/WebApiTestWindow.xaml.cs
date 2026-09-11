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

    /// <summary>
    /// 載入目前 CimApiClient 設定值至表單。isSimulationMode 決定「測試查詢料號」區塊是否顯示
    /// ——只在模擬模式下開放，避免正式產線有人手滑對真實 MES 發出測試查詢。
    /// </summary>
    public void LoadFrom(CimApiClient client, bool isSimulationMode)
    {
        _mesClient          = client;
        ChkEnabled.IsChecked = client.IsEnabled;
        TxtBaseUrl.Text     = client.BaseUrl;
        TxtEquipmentId.Text = client.EquipmentId;

        BorderTestSection.Visibility = isSimulationMode ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(TxtTestProductId.Text))
            TxtTestProductId.Text = "001400038499_00665"; // 機台實測條碼範例，方便直接測
    }

    private async void BtnQuery_Click(object sender, RoutedEventArgs e)
    {
        if (_mesClient == null) return;

        string productId = TxtTestProductId.Text.Trim();
        if (string.IsNullOrWhiteSpace(productId))
        {
            TxtQueryResult.Text = "請輸入要查詢的條碼。";
            return;
        }

        BtnQuery.IsEnabled = false;
        TxtQueryResult.Text = "查詢中...";
        try
        {
            var result = await _mesClient.QueryWorkOrderAsync(_mesClient.EquipmentId, productId);

            if (result == null)
            {
                TxtQueryResult.Text =
                    "沒有打成——可能原因：\n" +
                    "・「WebAPI 啟用」沒有勾選\n" +
                    "・尚未連線成功（BaseUrl 錯誤 / MES 連不到 / 還沒按過儲存重連）\n" +
                    "・呼叫途中發生例外（詳見 Logs\\WebApiLog）";
            }
            else if (result.Success)
            {
                TxtQueryResult.Text =
                    $"✔ 成功\n" +
                    $"料號 MaterialNo：{result.MaterialNo}\n" +
                    $"工單 WorkerOrderNo：{result.WorkerOrderNo}\n" +
                    $"序號 LotNo：{result.LotNo}\n" +
                    $"RFID 序號 RfidLotNo：{result.RfidLotNo}";
            }
            else
            {
                TxtQueryResult.Text =
                    $"✘ MES 回覆錯誤\n" +
                    $"錯誤碼：{result.ErrorCode}\n" +
                    $"錯誤訊息：{result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            TxtQueryResult.Text = $"查詢失敗：{ex.Message}";
        }
        finally
        {
            BtnQuery.IsEnabled = true;
        }
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
