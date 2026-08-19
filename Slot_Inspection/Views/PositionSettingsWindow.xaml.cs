using System.Linq;
using System.Windows;
using Slot_Inspection.Helper;
using Slot_Inspection.Models;
using Slot_Inspection.Services;
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
        var vm = (PositionSettingsViewModel)DataContext;

        if (vm.IsDirty)
        {
            var result = MessageBox.Show(
                "您修改了座標設定但尚未備份，建議先備份再離開，避免之後需要還原時找不到目前的設定。\n\n是否現在備份？",
                "尚未備份", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes && !TryBackupCurrentSettings(vm))
                return; // 備份失敗或使用者取消覆蓋，留在視窗讓使用者修正，不要直接關閉
        }

        DialogResult = true;
    }

    private void BtnLoadRecipe_Click(object sender, RoutedEventArgs e)
    {
        var vm = (PositionSettingsViewModel)DataContext;
        string name = vm.SelectedRecipeName.Trim();
        if (string.IsNullOrWhiteSpace(name)) return;

        if (!AxisRecipeManager.Exists(name))
        {
            MessageBox.Show($"找不到 Recipe「{name}」", "讀取失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (MessageBox.Show(
                $"讀取「{name}」將覆蓋目前生效中的軸座標與演算法參數設定，建議先確認目前設定已備份。是否繼續？",
                "確認讀取", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            vm.ApplyRecipeData(AxisRecipeManager.Load(name));
            bool hasAlgFiles = AxisRecipeManager.LoadAlgFiles(name);
            vm.CurrentRecipeName = name;
            vm.IsDirty = false; // 剛套用完的數值跟Recipe內容一致，不需要立刻又被提醒備份
            if (vm.CurrentUser != null)
                AuditLogger.Write(vm.CurrentUser.Username, vm.CurrentUser.Role, $"讀取 Recipe「{name}」");

            MessageBox.Show(hasAlgFiles
                    ? $"已套用 Recipe「{name}」：座標設定與演算法參數都已覆蓋回目前生效中的設定。"
                    : $"已套用 Recipe「{name}」：僅套用了座標設定（這份Recipe沒有演算法參數備份）。",
                "讀取完成", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"讀取 Recipe「{name}」失敗：{ex.Message}", "讀取失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnNewRecipe_Click(object sender, RoutedEventArgs e)
    {
        var vm = (PositionSettingsViewModel)DataContext;
        TryBackupCurrentSettings(vm);
    }

    /// <summary>
    /// 把「目前生效中」的座標與演算法參數存成一份Recipe備份。供「備份／建立」按鈕，
    /// 以及離開視窗時偵測到尚未備份的提醒共用，確保兩個入口走同一套驗證與確認邏輯。
    /// </summary>
    private bool TryBackupCurrentSettings(PositionSettingsViewModel vm)
    {
        string name = vm.SelectedRecipeName.Trim();
        if (!AxisRecipeManager.IsValidName(name))
        {
            MessageBox.Show("請先在「參數組合 (Recipe)」欄位輸入有效的名稱，才能備份。", "備份失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (AxisRecipeManager.Exists(name) &&
            MessageBox.Show($"Recipe「{name}」已存在，要覆蓋嗎？", "確認覆蓋",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return false;

        AxisRecipeManager.Save(name, vm.BuildRecipeData());
        int algFileCount = AxisRecipeManager.SaveAlgFiles(name);
        vm.RefreshRecipeList();
        vm.SelectedRecipeName = name;
        vm.CurrentRecipeName = name;
        vm.IsDirty = false;
        if (vm.CurrentUser != null)
            AuditLogger.Write(vm.CurrentUser.Username, vm.CurrentUser.Role, $"建立 Recipe「{name}」");

        MessageBox.Show(
            $"已備份完成：Recipe「{name}」\n\n已存入目前生效中的座標設定，以及 {algFileCount} 個演算法參數JSON檔案。",
            "備份完成", MessageBoxButton.OK, MessageBoxImage.Information);
        return true;
    }

    private void BtnDeleteRecipe_Click(object sender, RoutedEventArgs e)
    {
        var vm = (PositionSettingsViewModel)DataContext;
        string name = vm.SelectedRecipeName.Trim();
        if (!AxisRecipeManager.Exists(name)) return;

        if (MessageBox.Show($"確定刪除 Recipe「{name}」？", "確認刪除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        AxisRecipeManager.Delete(name);
        AxisRecipeManager.DeleteAlgFiles(name);
        vm.RefreshRecipeList();
        vm.SelectedRecipeName = "";
        if (vm.CurrentRecipeName == name)
            vm.CurrentRecipeName = "";
        if (vm.CurrentUser != null)
            AuditLogger.Write(vm.CurrentUser.Username, vm.CurrentUser.Role, $"刪除 Recipe「{name}」");
    }

    /// <summary>把開啟本視窗當下的量測結果快照存成「參數組合」欄位所指名稱的 QC 基準。</summary>
    private void BtnSaveQcBaseline_Click(object sender, RoutedEventArgs e)
    {
        var vm = (PositionSettingsViewModel)DataContext;
        string name = vm.SelectedRecipeName.Trim();
        if (!AxisRecipeManager.IsValidName(name))
        {
            MessageBox.Show("請先在「參數組合 (Recipe)」欄位輸入有效的名稱，才能儲存 QC 基準。",
                "無法儲存 QC 基準", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (vm.CurrentMeasurementsAreaA.All(s => s.Value == 0) && vm.CurrentMeasurementsAreaB.All(s => s.Value == 0))
        {
            MessageBox.Show("目前尚無量測結果，請先對已知良品板執行一次 RUN 再儲存基準。",
                "無法儲存 QC 基準", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (AxisRecipeManager.HasBaseline(name) &&
            MessageBox.Show($"Recipe「{name}」已有 QC 基準，是否覆蓋？",
                "確認覆蓋", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var data = QcBaselineData.FromSlotItems(name, vm.CurrentUser?.Username ?? "", "",
            vm.CurrentMeasurementsAreaA, vm.CurrentMeasurementsAreaB);
        AxisRecipeManager.SaveBaseline(name, data);

        if (vm.CurrentUser != null)
            AuditLogger.Write(vm.CurrentUser.Username, vm.CurrentUser.Role, $"儲存 QC 基準（Recipe「{name}」）");

        MessageBox.Show($"已將目前量測結果存為 Recipe「{name}」的 QC 基準。",
            "儲存完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>開啟 QC 比對視窗：把「參數組合」欄位所指 Recipe 的基準，跟開啟本視窗當下的量測結果快照比較。</summary>
    private void BtnOpenQcComparison_Click(object sender, RoutedEventArgs e)
    {
        var vm = (PositionSettingsViewModel)DataContext;
        string name = vm.SelectedRecipeName.Trim();
        if (!AxisRecipeManager.HasBaseline(name))
        {
            MessageBox.Show(string.IsNullOrWhiteSpace(name)
                    ? "請先在「參數組合 (Recipe)」欄位輸入要比對的 Recipe 名稱。"
                    : $"Recipe「{name}」尚未儲存過 QC 基準，請先執行「存為QC基準」。",
                "無法比對", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (vm.Config == null) return;

        try
        {
            var baseline = AxisRecipeManager.LoadBaseline(name);
            var compareVm = new QcComparisonViewModel(baseline, vm.CurrentMeasurementsAreaA, vm.CurrentMeasurementsAreaB, vm.Config);
            TryWriteQcReport(vm.CurrentBarcode, name, compareVm.Rows);
            new QcComparisonWindow { DataContext = compareVm, Owner = this }.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"讀取 QC 基準失敗：{ex.Message}", "比對失敗", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void TryWriteQcReport(string barcode, string recipeName, IReadOnlyList<QcSlotComparison> rows)
    {
        try { QcComparisonResultWriter.Write(barcode, recipeName, rows); }
        catch { /* 報表寫入失敗不影響比對視窗顯示 */ }
    }
}
