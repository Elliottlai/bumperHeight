using System.Windows;
using System.Windows.Controls;
using Slot_Inspection.Helper;
using Slot_Inspection.Models;

namespace Slot_Inspection.Views;

/// <summary>
/// 料號設定表編輯視窗（僅 Admin 開啟）。純 code-behind、操作即時存檔、無 OK/取消，
/// 比照「帳號與安全設定」視窗（<see cref="UserManagementWindow"/>）。
/// </summary>
public partial class MaterialRecipeMapWindow : Window
{
    private readonly UserAccount _currentAdmin;

    public MaterialRecipeMapWindow(UserAccount currentAdmin)
    {
        InitializeComponent();
        _currentAdmin = currentAdmin;

        CmbAddRecipe.ItemsSource  = AxisRecipeManager.List();
        CmbEditRecipe.ItemsSource = AxisRecipeManager.List();
        CmbAddSide.SelectedIndex  = 0;   // 預設 "R"

        RefreshList();
    }

    /// <summary>ListView 顯示用投影列：Recipe 欄帶「⚠ Recipe 不存在」標記。</summary>
    private sealed record Row(string MaterialNo, string Side, string RecipeName, string RecipeDisplay);

    private void RefreshList()
    {
        var recipes = new HashSet<string>(AxisRecipeManager.List(), StringComparer.OrdinalIgnoreCase);
        LstMap.ItemsSource = MaterialRecipeManager.LoadAll()
            .Select(e => new Row(
                e.MaterialNo,
                e.Side,
                e.RecipeName,
                recipes.Contains(e.RecipeName) ? e.RecipeName : $"{e.RecipeName}  ⚠ Recipe 不存在"))
            .ToList();
    }

    private static string SideOf(ComboBox cmb)
        => (cmb.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

    private void LstMap_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstMap.SelectedItem is not Row row) return;

        CmbEditSide.SelectedItem = null;
        foreach (ComboBoxItem it in CmbEditSide.Items)
        {
            if ((it.Content as string) == row.Side) { CmbEditSide.SelectedItem = it; break; }
        }

        CmbEditRecipe.SelectedItem = AxisRecipeManager.Exists(row.RecipeName) ? row.RecipeName : null;
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        string materialNo = TxtMaterialNo.Text.Trim();
        string side       = SideOf(CmbAddSide);
        string recipe     = CmbAddRecipe.SelectedItem as string ?? "";

        if (materialNo.Length == 0) { TxtError.Text = "請輸入料號"; return; }
        if (side != "R" && side != "L") { TxtError.Text = "請選擇側別（R 或 L）"; return; }
        if (string.IsNullOrWhiteSpace(recipe) || !AxisRecipeManager.Exists(recipe))
        {
            TxtError.Text = "請選擇有效的 Recipe";
            return;
        }
        if (MaterialRecipeManager.Exists(materialNo))
        {
            TxtError.Text = $"料號「{materialNo}」已存在，請改用「套用修改」";
            return;
        }

        MaterialRecipeManager.Add(materialNo, side, recipe);
        AuditLogger.Write(_currentAdmin.Username, _currentAdmin.Role,
            $"新增料號對應「{materialNo}」（{side} / {recipe}）");
        TxtError.Text = "";
        TxtMaterialNo.Clear();
        RefreshList();
    }

    private void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (LstMap.SelectedItem is not Row selected) { TxtError.Text = "請先選一列"; return; }

        string side   = SideOf(CmbEditSide);
        string recipe = CmbEditRecipe.SelectedItem as string ?? "";

        if (side != "R" && side != "L") { TxtError.Text = "請選擇側別（R 或 L）"; return; }
        if (string.IsNullOrWhiteSpace(recipe) || !AxisRecipeManager.Exists(recipe))
        {
            TxtError.Text = "請選擇有效的 Recipe";
            return;
        }

        MaterialRecipeManager.Update(selected.MaterialNo, side, recipe);
        AuditLogger.Write(_currentAdmin.Username, _currentAdmin.Role,
            $"修改料號對應「{selected.MaterialNo}」（{side} / {recipe}）");
        TxtError.Text = "";
        RefreshList();
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (LstMap.SelectedItem is not Row selected) { TxtError.Text = "請先選一列"; return; }

        if (MessageBox.Show($"確定刪除料號「{selected.MaterialNo}」的對應設定？", "確認刪除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        MaterialRecipeManager.Delete(selected.MaterialNo);
        AuditLogger.Write(_currentAdmin.Username, _currentAdmin.Role,
            $"刪除料號對應「{selected.MaterialNo}」");
        TxtError.Text = "";
        RefreshList();
    }
}
