using System.Windows;
using Slot_Inspection.ViewModels;

namespace Slot_Inspection.Views;

public partial class QcInspectionWindow : Window
{
    public QcInspectionWindow()
    {
        InitializeComponent();
    }

    /// <summary>MainViewModel 在每輪檢測結束後呼叫，讓開著的儀表板即時重新比對。</summary>
    public void NotifyInspectionCompleted()
    {
        if (DataContext is QcInspectionViewModel vm)
            Dispatcher.BeginInvoke(new Action(vm.Refresh));
    }
}
