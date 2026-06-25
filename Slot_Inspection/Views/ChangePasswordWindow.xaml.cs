using System.Windows;
using Slot_Inspection.Models;

namespace Slot_Inspection.Views;

public partial class ChangePasswordWindow : Window
{
    private readonly string _username;

    public ChangePasswordWindow(string username)
    {
        InitializeComponent();
        _username = username;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        string oldPassword = PwdOld.Password;
        string newPassword = PwdNew.Password;
        string confirm = PwdConfirm.Password;

        if (string.IsNullOrEmpty(newPassword))
        {
            TxtError.Text = "新密碼不能空白";
            return;
        }
        if (newPassword != confirm)
        {
            TxtError.Text = "兩次輸入的新密碼不一致";
            return;
        }

        if (!UserManager.ChangePassword(_username, oldPassword, newPassword))
        {
            TxtError.Text = "目前密碼錯誤";
            PwdOld.Clear();
            PwdOld.Focus();
            return;
        }

        DialogResult = true;
    }
}
