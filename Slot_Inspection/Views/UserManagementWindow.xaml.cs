using System.Windows;
using Slot_Inspection.Helper;
using Slot_Inspection.Models;
using Slot_Inspection.ViewModels;

namespace Slot_Inspection.Views;

public partial class UserManagementWindow : Window
{
    private readonly UserAccount _currentAdmin;
    private readonly MainViewModel _mainViewModel;

    public UserManagementWindow(UserAccount currentAdmin, MainViewModel mainViewModel)
    {
        InitializeComponent();
        _currentAdmin = currentAdmin;
        _mainViewModel = mainViewModel;

        // 注意：專案裡有 Slot_Inspection.Enum 這個既有namespace（Enum/NamedKey.cs），
        // 在 Slot_Inspection 底下的namespace寫「Enum」會被優先解析成那個namespace而非System.Enum，要寫完整路徑
        CmbNewRole.ItemsSource = System.Enum.GetValues<UserRole>();
        CmbNewRole.SelectedItem = UserRole.Operator;
        CmbChangeRole.ItemsSource = System.Enum.GetValues<UserRole>();

        TxtIdleTimeout.Text = _mainViewModel.IdleLockTimeoutMinutes.ToString();

        RefreshList();
    }

    private void BtnApplyIdleTimeout_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(TxtIdleTimeout.Text.Trim(), out int minutes) || minutes < 1 || minutes > 120)
        {
            TxtError.Text = "請輸入 1~120 之間的整數分鐘數";
            return;
        }

        _mainViewModel.IdleLockTimeoutMinutes = minutes;
        TxtIdleTimeout.Text = _mainViewModel.IdleLockTimeoutMinutes.ToString(); // 顯示實際套用值（含clamp結果）
        TxtError.Text = "";
        MessageBox.Show($"閒置自動鎖定時間已更新為 {_mainViewModel.IdleLockTimeoutMinutes} 分鐘。",
            "設定完成", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshList()
    {
        LstUsers.ItemsSource = UserManager.LoadAll();
    }

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        string username = TxtNewUsername.Text.Trim();
        string password = PwdNewPassword.Password;
        var role = (UserRole)(CmbNewRole.SelectedItem ?? UserRole.Operator);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            TxtError.Text = "請輸入帳號與密碼";
            return;
        }

        if (!UserManager.Add(username, password, role))
        {
            TxtError.Text = $"帳號「{username}」已存在";
            return;
        }

        AuditLogger.Write(_currentAdmin.Username, _currentAdmin.Role, $"新增帳號「{username}」（{role}）");
        TxtError.Text = "";
        TxtNewUsername.Clear();
        PwdNewPassword.Clear();
        RefreshList();
    }

    private void BtnApplyRole_Click(object sender, RoutedEventArgs e)
    {
        if (LstUsers.SelectedItem is not UserAccount selected)
        {
            TxtError.Text = "請先選一個帳號";
            return;
        }
        if (CmbChangeRole.SelectedItem is not UserRole newRole)
        {
            TxtError.Text = "請選擇要改成的角色";
            return;
        }

        UserManager.ChangeRole(selected.Username, newRole);
        AuditLogger.Write(_currentAdmin.Username, _currentAdmin.Role,
            $"將帳號「{selected.Username}」角色改為 {newRole}");
        TxtError.Text = "";
        RefreshList();
    }

    private void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (LstUsers.SelectedItem is not UserAccount selected)
        {
            TxtError.Text = "請先選一個帳號";
            return;
        }

        if (selected.Username.Equals(_currentAdmin.Username, StringComparison.OrdinalIgnoreCase))
        {
            TxtError.Text = "不能刪除自己目前登入的帳號";
            return;
        }

        if (MessageBox.Show($"確定刪除帳號「{selected.Username}」？", "確認刪除",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        UserManager.Delete(selected.Username);
        AuditLogger.Write(_currentAdmin.Username, _currentAdmin.Role, $"刪除帳號「{selected.Username}」");
        TxtError.Text = "";
        RefreshList();
    }
}
