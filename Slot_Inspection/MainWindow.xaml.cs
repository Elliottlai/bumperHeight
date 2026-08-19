using System.Windows;
using System.Windows.Input;
using Slot_Inspection.Models;
using Slot_Inspection.ViewModels;

namespace Slot_Inspection;

public partial class MainWindow : Window
{
    /// <summary>
    /// user 為 null（預設）表示尚未登入：視窗會直接顯示但畫面是鎖定狀態，
    /// 由 App.xaml.cs 在登入視窗成功登入後呼叫 MainViewModel.CompleteLogin 解鎖。
    /// </summary>
    public MainWindow(UserAccount? user = null)
    {
        InitializeComponent();
        DataContext = new MainViewModel(user);
    }

    // ★ 程式關閉時上報 MES Disconnected
    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (DataContext is MainViewModel vm)
            await vm.DisconnectMesAsync();
    }

    // ShutdownMode=OnExplicitShutdown，主畫面關閉時要手動結束整個程式
    protected override void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    // ── 閒置逾時鎖定：任何滑鼠/鍵盤操作都重置計時器 ──
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        (DataContext as MainViewModel)?.ResetIdleTimer();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        (DataContext as MainViewModel)?.ResetIdleTimer();

        // 模擬模式下按 X 鍵切換「板子未放好」狀態（焦點在輸入框時不觸發，避免誤擋打字）
        if (e.Key == Key.X
            && DataContext is MainViewModel vm
            && vm.IsSimulationMode
            && e.OriginalSource is not System.Windows.Controls.TextBox)
        {
            vm.ToggleSimBoardMisplaced();
            e.Handled = true;
        }

        // 模擬模式下按 E 鍵切換 EMO(X0) 觸發狀態，測試按下 EMO 後的畫面/流程行為
        if (e.Key == Key.E
            && DataContext is MainViewModel vm2
            && vm2.IsSimulationMode
            && e.OriginalSource is not System.Windows.Controls.TextBox)
        {
            vm2.ToggleSimEmo();
            e.Handled = true;
        }
    }

}
