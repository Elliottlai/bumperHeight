using System.Windows.Controls;
using System.Windows.Input;

namespace DeltaAxis_RS485.Views;

public partial class AxisControlView : UserControl
{
    public AxisControlView()
    {
        InitializeComponent();
    }

    private void Button_Click(object sender, System.Windows.RoutedEventArgs e)
    {

    }

    // 數值輸入框：第一次點擊/Tab進來時整段文字反白選取，打字直接覆蓋掉原本的值，
    // 不需要先手動刪除（例如預設顯示的 0）。
    private void NumericTextBox_GotFocus(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            tb.SelectAll();
    }

    private void NumericTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox tb && !tb.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            tb.Focus();
        }
    }
}