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
                $"讀取「{name}」將立即覆蓋目前生效中的軸座標與演算法參數設定，建議先確認目前設定已備份。是否繼續？",
                "確認讀取", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        if (vm.Config == null)
        {
            MessageBox.Show("內部錯誤：設定尚未載入，無法套用 Recipe。", "讀取失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        try
        {
            // 座標／高度／演算法檔／名稱全部立即套用並存檔（跟「掃碼自動切換」共用同一套邏輯）
            bool hasAlgFiles = RecipeService.Apply(name, vm.Config);
            // 把剛套用的值重新載回編輯欄位顯示（LoadFrom 內會一併把 IsDirty 歸零、更新 CurrentRecipeName）
            vm.LoadFrom(vm.Config);
            vm.SelectedRecipeName = name;
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
}
