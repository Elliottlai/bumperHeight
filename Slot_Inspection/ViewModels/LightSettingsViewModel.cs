using System.Windows;
using System.Windows.Input;
using Slot_Inspection.Models;

namespace Slot_Inspection.ViewModels;

/// <summary>
/// 亮度設定視窗按下「測試」時呼叫，直接把目前 UI 上的百分比送到硬體，不用等按 OK 存檔、
/// 也不用跑 Run/DryRun 才看得到燈的變化。簽章刻意對齊 MachineController.TryApplyLightIntensityNow，
/// 呼叫端可以直接把該方法當委派傳進來，不用另外包一層。
/// </summary>
public delegate bool LightTestDelegate(int leftPercent, int rightPercent, out string message);

/// <summary>
/// 亮度設定視窗的 ViewModel。
/// 持有左/右相機亮度、Value 上下極限、（Admin限定）曝光時間、以及（Admin限定）X3/X11 板子感測個別停用設定的可編輯副本，按 OK 後呼叫端再寫回 config。
/// </summary>
public sealed class LightSettingsViewModel : ObservableObject
{
    private int _lightIntensityLeft;
    private int _lightIntensityRight;
    private double _valueLimitLow;
    private double _valueLimitHigh;
    private double _exposureTimeLeft;
    private double _exposureTimeRight;
    private bool _pauseX3BoardSensor;
    private bool _pauseX11BoardSensor;
    private double _qcToleranceValue;
    private double _qcToleranceMeanIntensity;
    private double _qcToleranceDistanceToRoiCenter;

    public int LightIntensityLeft
    {
        get => _lightIntensityLeft;
        set => SetProperty(ref _lightIntensityLeft, Math.Clamp(value, 0, 100));
    }

    public int LightIntensityRight
    {
        get => _lightIntensityRight;
        set => SetProperty(ref _lightIntensityRight, Math.Clamp(value, 0, 100));
    }

    public double ValueLimitLow
    {
        get => _valueLimitLow;
        set => SetProperty(ref _valueLimitLow, value);
    }

    public double ValueLimitHigh
    {
        get => _valueLimitHigh;
        set => SetProperty(ref _valueLimitHigh, value);
    }

    /// <summary>左相機曝光時間 (µs)。僅 Admin 登入時，UI 上的曝光時間 Tab 才會顯示/可編輯。</summary>
    public double ExposureTimeLeft
    {
        get => _exposureTimeLeft;
        set => SetProperty(ref _exposureTimeLeft, Math.Max(1.0, value));
    }

    /// <summary>右相機曝光時間 (µs)。僅 Admin 登入時，UI 上的曝光時間 Tab 才會顯示/可編輯。</summary>
    public double ExposureTimeRight
    {
        get => _exposureTimeRight;
        set => SetProperty(ref _exposureTimeRight, Math.Max(1.0, value));
    }

    /// <summary>目前登入者是否為 Admin；決定曝光時間 Tab、板子感測停用 Tab 是否顯示（由呼叫端傳入，不在此判斷角色）。</summary>
    public bool IsAdmin { get; }

    private readonly LightTestDelegate? _lightTest;

    /// <summary>測試按鈕的結果訊息（成功/失敗原因），顯示在亮度設定 Tab 裡。</summary>
    private string _testResultMessage = "";
    public string TestResultMessage
    {
        get => _testResultMessage;
        set => SetProperty(ref _testResultMessage, value);
    }

    // 曾實測連續快速切換測試按鈕（100%↔0% 每秒以上）十幾次後，光源控制器開始對所有後續
    // 指令回應通用錯誤碼「E」，且不會自行恢復。實機 S03 流程從不會這樣高頻切換，這裡加個
    // 最短間隔保護測試按鈕本身，避免手動測試時不小心把控制器逼進這個狀態。
    private const int MinTestIntervalMs = 800;
    private DateTime _lastTestCallUtc = DateTime.MinValue;

    /// <summary>把目前滑桿上的百分比（不用先按 OK）直接送到硬體開燈，方便反覆調整測試。</summary>
    public ICommand TestLightOnCommand { get; }

    /// <summary>兩通道都設為 0%，方便確認「真的有在控制」而不是燈本來就一直亮著。</summary>
    public ICommand TestLightOffCommand { get; }

    private bool CanTest() => DateTime.UtcNow - _lastTestCallUtc >= TimeSpan.FromMilliseconds(MinTestIntervalMs);

    private void OnTestLightOn()
    {
        if (_lightTest == null) { TestResultMessage = "✘ 尚未連接測試功能"; return; }
        _lastTestCallUtc = DateTime.UtcNow;
        bool ok = _lightTest(LightIntensityLeft, LightIntensityRight, out string message);
        TestResultMessage = (ok ? "✔ " : "✘ ") + message;
    }

    private void OnTestLightOff()
    {
        if (_lightTest == null) { TestResultMessage = "✘ 尚未連接測試功能"; return; }
        _lastTestCallUtc = DateTime.UtcNow;
        bool ok = _lightTest(0, 0, out string message);
        TestResultMessage = (ok ? "✔ " : "✘ ") + message;
    }

    public double QcToleranceValue
    {
        get => _qcToleranceValue;
        set => SetProperty(ref _qcToleranceValue, value);
    }

    public double QcToleranceMeanIntensity
    {
        get => _qcToleranceMeanIntensity;
        set => SetProperty(ref _qcToleranceMeanIntensity, value);
    }

    public double QcToleranceDistanceToRoiCenter
    {
        get => _qcToleranceDistanceToRoiCenter;
        set => SetProperty(ref _qcToleranceDistanceToRoiCenter, value);
    }

    /// <summary>是否停用 X3 板子放置感測判斷。勾選（false→true）時會跳出安全確認對話框。</summary>
    public bool PauseX3BoardSensor
    {
        get => _pauseX3BoardSensor;
        set
        {
            if (_pauseX3BoardSensor == value) return;
            if (value && !ConfirmPauseSensor("X3"))
            {
                OnPropertyChanged(); // 使用者取消：UI 需重新查詢 getter，讓 CheckBox 復原為未勾選
                return;
            }
            SetProperty(ref _pauseX3BoardSensor, value);
        }
    }

    /// <summary>是否停用 X11 板子放置感測判斷。勾選（false→true）時會跳出安全確認對話框。</summary>
    public bool PauseX11BoardSensor
    {
        get => _pauseX11BoardSensor;
        set
        {
            if (_pauseX11BoardSensor == value) return;
            if (value && !ConfirmPauseSensor("X11"))
            {
                OnPropertyChanged();
                return;
            }
            SetProperty(ref _pauseX11BoardSensor, value);
        }
    }

    private static bool ConfirmPauseSensor(string signalName)
    {
        var confirm = MessageBox.Show(
            $"停用後將完全忽略 {signalName} 板子放置感測，即使該感測器偵測到異常也不會擋下 RUN。\n" +
            "僅供排除感測器異常時應急使用，請確認板子實際狀態後再繼續。\n\n是否確定停用？",
            $"停用 {signalName} 板子感測（Admin）",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        return confirm == MessageBoxResult.Yes;
    }

    public LightSettingsViewModel(InspectionConfig config, bool isAdmin, LightTestDelegate? lightTest = null)
    {
        _lightIntensityLeft   = config.LightIntensityLeft;
        _lightIntensityRight  = config.LightIntensityRight;
        _valueLimitLow        = config.ValueLimitLow;
        _valueLimitHigh       = config.ValueLimitHigh;
        _exposureTimeLeft     = config.ExposureTimeLeft;
        _exposureTimeRight    = config.ExposureTimeRight;
        _pauseX3BoardSensor   = config.PauseX3BoardSensor;
        _pauseX11BoardSensor  = config.PauseX11BoardSensor;
        _qcToleranceValue               = config.QcToleranceValue;
        _qcToleranceMeanIntensity       = config.QcToleranceMeanIntensity;
        _qcToleranceDistanceToRoiCenter = config.QcToleranceDistanceToRoiCenter;
        IsAdmin = isAdmin;

        _lightTest = lightTest;
        TestLightOnCommand = new RelayCommand(OnTestLightOn, CanTest);
        TestLightOffCommand = new RelayCommand(OnTestLightOff, CanTest);
    }

    /// <summary>將目前值寫回 InspectionConfig 並呼叫 Save()。</summary>
    public void ApplyTo(InspectionConfig config)
    {
        config.LightIntensityLeft  = LightIntensityLeft;
        config.LightIntensityRight = LightIntensityRight;
        config.ValueLimitLow       = ValueLimitLow;
        config.ValueLimitHigh      = ValueLimitHigh;
        config.ExposureTimeLeft    = ExposureTimeLeft;
        config.ExposureTimeRight   = ExposureTimeRight;
        config.PauseX3BoardSensor  = PauseX3BoardSensor;
        config.PauseX11BoardSensor = PauseX11BoardSensor;
        config.QcToleranceValue               = QcToleranceValue;
        config.QcToleranceMeanIntensity       = QcToleranceMeanIntensity;
        config.QcToleranceDistanceToRoiCenter = QcToleranceDistanceToRoiCenter;
        config.Save();
    }
}
