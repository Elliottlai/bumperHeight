using System.Windows;
using System.Windows.Input;
using Slot_Inspection.Helper;
using Slot_Inspection.Models;

namespace Slot_Inspection.Views;

public partial class LoginWindow : Window
{
    /// <summary>登入成功後的帳號資料，DialogResult=true 時保證非 null。</summary>
    public UserAccount? AuthenticatedUser { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => TxtUsername.Focus();
    }

    private void PwdPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            BtnLogin_Click(sender, e);
    }

    private void BtnLogin_Click(object sender, RoutedEventArgs e)
    {
        string username = TxtUsername.Text.Trim();
        string password = PwdPassword.Password;

        if (string.IsNullOrWhiteSpace(username))
        {
            TxtError.Text = "請輸入帳號";
            return;
        }

        var user = UserManager.Authenticate(username, password);
        if (user == null)
        {
            TxtError.Text = "帳號或密碼錯誤";
            AuditLogger.Write(username, null, "登入失敗");
            PwdPassword.Clear();
            PwdPassword.Focus();
            return;
        }

        AuthenticatedUser = user;
        AuditLogger.Write(user.Username, user.Role, "登入成功");
        DialogResult = true;
    }
}
