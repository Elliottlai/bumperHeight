using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using JsonType;
using NLog;
using Slot_Inspection.Helper;
using Slot_Inspection.Models;
using Slot_Inspection.Services;
using Slot_Inspection.Views;

namespace Slot_Inspection.ViewModels;

/// <summary>主畫面標題列機台運行狀態指示燈用的整體狀態。</summary>
public enum MachineRunState { Disconnected, Ready, Running, Paused, Alarm }

public sealed class MainViewModel : ObservableObject
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

    private string _barcode = "";
    private string _carrierSide = "";
    private string _areaATitle = "Area A";
    private string _areaBTitle = "Area B";
    private string _confirmedBarcode = "";
    private string _currentDate = DateTime.Now.ToString("yyyy/MM/dd");
    private string _currentTime = DateTime.Now.ToString("HH:mm:ss");
    private bool _isRunning;
    private string _statusMessage = "系統啟動中，請稍候...";
    private bool _isInitialized;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _plcTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    // MES 重試計時器：失敗時每 15 秒自動重試，連線成功後停止
    private readonly DispatcherTimer _mesRetryTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly System.Diagnostics.Stopwatch _x1x2Stopwatch = new();
    private static readonly TimeSpan DryRunTriggerDuration = TimeSpan.FromMilliseconds(500);
    private DateTime _runStartTime;  // 主流程開始時間（用於 MES 上傳 ProcessStartTime）
    private bool _prevX10 = false; // X10 上一次狀態（預設 0，保留供未來使用）
    private bool _isLightGateBlocked; // X7=LOW：光閘遮斷中
    private int _lightGateBlockedCount = 0; // 連續讀到 LOW 的次數（觸發防抖用）
    private int _lightGateClearCount = 0;  // 連續讀到 HIGH 的次數（釋放防抖用）
    private const int LightGateDebounceCount = 3;      // 需連續 N 次（×100ms）LOW 才視為真正觸發
    private const int LightGateClearDebounceCount = 5; // 需連續 N 次（×100ms）HIGH 才視為真正解除
    // X3/X11 板子放置雷射感測
    private bool _isBoardMisplaced;
    private int _boardMisplacedCount;
    private int _boardPlacedCount;
    private bool _boardMisplacedDialogShown;
    private const int BoardMisplacedDebounceCount = 3; // 需連續 N 次（×500ms）才確認未放好
    private const int BoardPlacedDebounceCount = 3;    // 需連續 N 次（×500ms）才確認放好
    // X3/X11 訊號歷史：每次 Tick 都記錄，最多保留 10 筆，確認觸發時一起寫 log（做法比照 X7）
    private readonly Queue<(DateTime Time, bool X3Raw, bool X11Raw, bool RawMisplaced, int MisplacedCount, int PlacedCount)> _boardSignalHistory = new();
    private const int BoardHistorySize = 10;
    // X3/X11 原始訊號上一次記錄的值（用於偵測變化，寫入持續追蹤 log；null=尚未記錄過）
    private bool? _prevX3RawLogged;
    private bool? _prevX11RawLogged;

    private bool _isLightGateBypassActive; // 取料暫停光閘旗標
    private bool _isBoardSensorBypassActive; // Admin 專用：暫時取消 X3/X11 雷射感測判斷，不讓它卡住 Run
    private bool _isLocked; // 閒置逾時鎖定畫面
    // Interval 在建構式裡會被覆寫為 InspectionConfig.IdleLockTimeoutMinutes（可由Admin在帳號管理視窗調整）
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromMinutes(10) };

    // X7 訊號歷史：每次 Tick 都記錄，最多保留 10 筆，觸發遮斷時一起寫 log
    private readonly Queue<(DateTime Time, bool X7Raw, bool Blocked, int Count)> _x7SignalHistory = new();
    private const int X7HistorySize = 10;
    private readonly MachineController _machine = new();
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<string>? _barcodeTcs; // 等待使用者手動輸入條碼
    private readonly CimApiClient _mesClient = new();

    private MesConnectionStatus _mesStatus = MesConnectionStatus.Unknown;
    public MesConnectionStatus MesStatus
    {
        get => _mesStatus;
        private set
        {
            if (SetProperty(ref _mesStatus, value))
            {
                OnPropertyChanged(nameof(MesStatusText));
                OnPropertyChanged(nameof(MesStatusKey));
            }
        }
    }

    /// <summary>供 UI 顯示的 MES 連線狀態文字。</summary>
    public string MesStatusText => _mesStatus switch
    {
        MesConnectionStatus.Connected    => "MES ✅ 已連線",
        MesConnectionStatus.Disconnected => "MES ❌ 未連線",
        MesConnectionStatus.Disabled     => "MES ■ 已關閉",
        _                                => "MES — 未嘗試"
    };

    /// <summary>供 XAML DataTrigger 比對用的字串（避免 enum 型別引用）。</summary>
    public string MesStatusKey => _mesStatus.ToString();

    /// <summary>
    /// 是否處於異常狀態。單一事實來源是 <see cref="Services.AlarmManager"/>（<c>_machine.Alarms</c>），
    /// 這裡只是唯讀代理；實體 PLC Y01 紅燈由 <see cref="MachineController.SyncRedAlarmOutput"/>
    /// 每個 PLC tick 依 Alarms.IsActive 持續斷言，不需要（也不應該）在這裡手動 set。
    /// </summary>
    public bool IsAlarmActive => _machine.Alarms.IsActive;

    /// <summary>目前所有生效中的異常原因，供 UI 顯示「為什麼在亮紅燈」用，避免只有一顆謎樣的紅點。</summary>
    public IReadOnlyList<AlarmEntry> ActiveAlarms => _machine.Alarms.GetActive();

    /// <summary>主畫面標題列狀態指示燈的整體狀態，依優先順序判斷：異常 > 暫停 > 運行中 > 待命 > 未連線。</summary>
    public MachineRunState RunState =>
        IsAlarmActive ? MachineRunState.Alarm :
        (IsLightGateBypassActive && IsLightGateBlocked) ? MachineRunState.Paused :
        (IsRunning || IsDryRunning) ? MachineRunState.Running :
        IsDeviceReady ? MachineRunState.Ready :
        MachineRunState.Disconnected;

    public string RunStateText => RunState switch
    {
        MachineRunState.Alarm   => "⚠ 異常",
        MachineRunState.Paused  => "暫停中（光閘）",
        MachineRunState.Running => "運行中",
        MachineRunState.Ready   => "待命中",
        _                       => "未連線"
    };

    /// <summary>供 XAML DataTrigger 比對用的字串（避免 enum 型別引用）。</summary>
    public string RunStateKey => RunState.ToString();

    /// <summary>RunState 的構成旗標有變動時，通知 RunState/RunStateText/RunStateKey 一起刷新。</summary>
    private void NotifyRunStateChanged()
    {
        OnPropertyChanged(nameof(RunState));
        OnPropertyChanged(nameof(RunStateText));
        OnPropertyChanged(nameof(RunStateKey));
        OnPropertyChanged(nameof(CanToggleSimulationMode));
    }

    // ═══════════════════════════════════════
    //  使用者登入 / 權限
    // ═══════════════════════════════════════

    private UserAccount _currentUser = null!;

    /// <summary>
    /// 目前登入的帳號（開機登入後由 LoginWindow 傳入）。登出後重新登入會換成新使用者，
    /// 不會是 null（登出流程在換成新使用者前都維持鎖定畫面，不會讓這個屬性處於未驗證狀態）。
    /// </summary>
    public UserAccount CurrentUser
    {
        get => _currentUser;
        private set
        {
            if (SetProperty(ref _currentUser, value))
            {
                OnPropertyChanged(nameof(CurrentUserDisplay));
                OnPropertyChanged(nameof(IsEngineerOrAbove));
                OnPropertyChanged(nameof(IsAdmin));
                OnPropertyChanged(nameof(CanToggleSimulationMode));
                OnPropertyChanged(nameof(CanToggleAxisSettleDelay));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>給UI顯示用，例如「admin（Admin）」。</summary>
    public string CurrentUserDisplay => $"{CurrentUser.Username}（{CurrentUser.Role}）";

    /// <summary>Engineer 或 Admin。注意：座標/光源/裁切設定、模擬模式、安定延遲等進階設定的實際門檻
    /// 已改由「角色權限設定」（<see cref="HasPermission"/>）決定，這個屬性只保留給沒有對應Permission項目的場合用。</summary>
    public bool IsEngineerOrAbove => CurrentUser.Role is UserRole.Engineer or UserRole.Admin;

    /// <summary>僅 Admin（帳號管理、WebAPI/MES設定、操作紀錄、角色權限設定——這些是安全核心邊界，不開放給Operator/Engineer設定）。</summary>
    public bool IsAdmin => CurrentUser.Role == UserRole.Admin;

    /// <summary>判斷目前登入使用者是否有某項可設定權限（Admin永遠true，Operator/Engineer依「角色權限設定」決定）。</summary>
    private bool HasPermission(Permission permission) => RolePermissionManager.IsAllowed(CurrentUser.Role, permission);

    /// <summary>
    /// 角色權限設定視窗存檔後呼叫，讓所有依權限決定的按鈕/Checkbox立即反映新設定，
    /// 不需要使用者登出重新登入才會生效。
    /// </summary>
    public void RefreshPermissions()
    {
        OnPropertyChanged(nameof(CanToggleSimulationMode));
        OnPropertyChanged(nameof(CanToggleAxisSettleDelay));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// 鎖定中：畫面內容保留，僅操作失效，必須重新輸入帳號密碼登入才能解鎖。
    /// 觸發時機：閒置逾時自動登出、使用者手動登出、開機後尚未完成第一次登入。
    /// 三種情況都會彈出 LoginWindow 要求完整登入，不提供「同一使用者僅輸入密碼」的快速解鎖，
    /// 避免畫面停在前一個使用者的工作階段、卻沒人確認當下實際操作者是誰。
    /// </summary>
    public bool IsLocked
    {
        get => _isLocked;
        private set
        {
            if (SetProperty(ref _isLocked, value))
            {
                OnPropertyChanged(nameof(IsUnlocked));
                CommandManager.InvalidateRequerySuggested(); // 計時器觸發不是WPF input事件，不會自動requery
            }
        }
    }

    /// <summary>= !IsLocked，給XAML綁主要內容區的IsEnabled用，鎖定時連鍵盤Tab都不能跳進去操作。</summary>
    public bool IsUnlocked => !IsLocked;

    /// <summary>有使用者操作（滑鼠/鍵盤）時呼叫，重置閒置計時器。鎖定中不重置，要等重新登入。</summary>
    public void ResetIdleTimer()
    {
        if (IsLocked) return;
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    /// <summary>
    /// 閒置多久（分鐘）自動鎖定畫面，直接代理 InspectionConfig.IdleLockTimeoutMinutes。
    /// 僅在帳號管理視窗（Admin限定）提供調整，變更會立即套用到計時器並寫入 AuditLog。
    /// </summary>
    public int IdleLockTimeoutMinutes
    {
        get => _machine.Config.IdleLockTimeoutMinutes;
        set
        {
            int clamped = Math.Clamp(value, 1, 120);
            if (_machine.Config.IdleLockTimeoutMinutes == clamped) return;
            _machine.Config.IdleLockTimeoutMinutes = clamped;
            _machine.Config.Save();
            _idleTimer.Interval = TimeSpan.FromMinutes(clamped);
            ResetIdleTimer();
            OnPropertyChanged();
            if (CurrentUser != null)
                AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, $"修改閒置鎖定時間為 {clamped} 分鐘");
        }
    }

    public ObservableCollection<SlotItem> AreaA_Row1 { get; } = [];
    public ObservableCollection<SlotItem> AreaB_Row1 { get; } = [];

    public AreaStatistics AreaA_Stats { get; } = new();
    public AreaStatistics AreaB_Stats { get; } = new();

    public string Barcode
    {
        get => _barcode;
        set => SetProperty(ref _barcode, value);
    }

    public string CarrierSide
    {
        get => _carrierSide;
        set => SetProperty(ref _carrierSide, value);
    }

    public string AreaATitle
    {
        get => _areaATitle;
        set => SetProperty(ref _areaATitle, value);
    }

    public string AreaBTitle
    {
        get => _areaBTitle;
        set => SetProperty(ref _areaBTitle, value);
    }

    public string CurrentDate
    {
        get => _currentDate;
        set => SetProperty(ref _currentDate, value);
    }

    public string CurrentTime
    {
        get => _currentTime;
        set => SetProperty(ref _currentTime, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
                NotifyRunStateChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool IsInitialized
    {
        get => _isInitialized;
        set => SetProperty(ref _isInitialized, value);
    }

    /// <summary>
    /// X7 光閘遮斷中（LOW）→ 禁止按 RUN。
    /// </summary>
    public bool IsLightGateBlocked
    {
        get => _isLightGateBlocked;
        private set
        {
            if (SetProperty(ref _isLightGateBlocked, value))
            {
                CommandManager.InvalidateRequerySuggested();
                NotifyRunStateChanged();
            }
        }
    }

    /// <summary>
    /// X3/X11 雷射感測未到位（任一 true）→ 禁止按 RUN。
    /// </summary>
    public bool IsBoardMisplaced
    {
        get => _isBoardMisplaced;
        private set
        {
            if (SetProperty(ref _isBoardMisplaced, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand InitAxesCommand { get; }
    public ICommand HomeCommand { get; }
    public ICommand ReadBarcodeCommand { get; }
    public ICommand MoveToPickPositionCommand { get; }
    public ICommand PositionSettingsCommand { get; }
    public ICommand CropSettingsCommand { get; }
    public ICommand LightSettingsCommand { get; }
    public ICommand OpenImageViewerCommand { get; }
    public ICommand OpenResultFolderCommand { get; }
    public ICommand ConfirmBarcodeCommand { get; }
    public ICommand ToggleLightGateBypassCommand { get; }
    public ICommand ToggleBoardSensorBypassCommand { get; }
    public ICommand SlcResetCommand { get; }
    public ICommand WebApiTestCommand { get; }
    public ICommand ChangePasswordCommand { get; }
    public ICommand OpenUserManagementCommand { get; }
    public ICommand OpenAuditLogCommand { get; }
    public ICommand OpenRolePermissionCommand { get; }
    public ICommand LogoutCommand { get; }
    /// <summary>鎖定畫面上的「重新登入」按鈕：LoginWindow被使用者直接關掉/取消時，用來再叫出一次登入視窗。</summary>
    public ICommand RetryLoginCommand { get; }

    /// <summary>
    /// 暫停光閘：啟用時光閘觸發不中斷流程，但 RUN 與原點賦歸會同時鎖定。
    /// </summary>
    public bool IsLightGateBypassActive
    {
        get => _isLightGateBypassActive;
        private set
        {
            if (SetProperty(ref _isLightGateBypassActive, value))
            {
                OnPropertyChanged(nameof(LightGateBypassButtonText));
                CommandManager.InvalidateRequerySuggested();
                NotifyRunStateChanged();
            }
        }
    }

    /// <summary>按鈕顯示文字，隨狀態切換；啟用時顯示「取消暫停光閘」，停用時顯示「暫停光閘」。</summary>
    public string LightGateBypassButtonText =>
        IsLightGateBypassActive ? "取消暫停光閘" : "暫停光閘";

    /// <summary>
    /// Admin 專用安全開關：啟用時完全忽略 X3/X11 雷射感測的板子放置判斷（視為永遠正常），
    /// 讓 RUN 不會被卡住。僅供 Admin 排除感測器異常/暫時無法校正時應急使用，
    /// 不透過角色權限表開放給 Operator/Engineer（跟 <see cref="IsLightGateBypassActive"/> 不同）。
    /// 啟用中訊號歷史與持續追蹤 log 仍照常記錄，只是不會反映到 IsBoardMisplaced/RUN 判斷上。
    /// </summary>
    public bool IsBoardSensorBypassActive
    {
        get => _isBoardSensorBypassActive;
        private set
        {
            if (SetProperty(ref _isBoardSensorBypassActive, value))
            {
                OnPropertyChanged(nameof(BoardSensorBypassButtonText));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    /// <summary>按鈕顯示文字，隨狀態切換；啟用時顯示「取消暫停板子感測」，停用時顯示「暫停板子感測」。</summary>
    public string BoardSensorBypassButtonText =>
        IsBoardSensorBypassActive ? "取消暫停板子感測" : "暫停板子感測";

    private bool _isDryRunning;
    public bool IsDryRunning
    {
        get => _isDryRunning;
        set
        {
            if (SetProperty(ref _isDryRunning, value))
                NotifyRunStateChanged();
        }
    }

    /// <summary>
    /// ZL/ZR 是否已在安全高度範圍內（可允許直接按 RUN 而不需已完成原點賦歸）。
    /// 判斷依據為 InspectionConfig 中的 GetZLSafeHeight / GetZRSafeHeight 以及 ZSafeTolerance。
    /// </summary>
    public bool IsZAtSafeHeight
    {
        get
        {
            try
            {
                bool x12 = _machine.GetPlcXOctal(12) ?? false;
                bool x13 = _machine.GetPlcXOctal(13) ?? false;
                double safeZL = _machine.Config.GetZLSafeHeight(x12, x13);
                double safeZR = _machine.Config.GetZRSafeHeight(x12, x13);
                double tol = _machine.Config.ZSafeTolerance;

                double currentZL = _machine.Axes.TryGetValue("AxisZL", out var zl) ? zl.GetRealPosition() : double.MaxValue;
                double currentZR = _machine.Axes.TryGetValue("AxisZR", out var zr) ? zr.GetRealPosition() : double.MaxValue;

                bool zlSafe = currentZL <= safeZL + tol;
                bool zrSafe = currentZR <= safeZR + tol;
                return zlSafe && zrSafe;
            }
            catch
            {
                return false;
            }
        }
    }

    private bool _isDeviceReady;
    /// <summary>
    /// 裝置已初始化（Config+Servo+PLC），但尚未完成原點賦歸。
    /// </summary>
    public bool IsDeviceReady
    {
        get => _isDeviceReady;
        set
        {
            if (SetProperty(ref _isDeviceReady, value))
                NotifyRunStateChanged();
        }
    }

    /// <summary>
    /// 模擬模式開關。Debug-Sim 組態預設 true，其他組態預設 false。
    /// 可在 UI 動態切換，但執行中（RUN/DryRun）不允許切換——切換時機台正在跑流程，
    /// 中途改變「要不要碰真實硬體」會讓正在進行中的動作跟接下來的動作用不同模式，狀態會兵荒馬亂。
    /// </summary>
    public bool IsSimulationMode
    {
        get => MachineController.SimulationMode;
        set
        {
            if (!CanToggleSimulationMode)
            {
                OnPropertyChanged(); // 還原 CheckBox 顯示成目前實際值，不接受這次變更
                return;
            }
            if (MachineController.SimulationMode == value) return;
            MachineController.SimulationMode = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
            if (CurrentUser != null)
                AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, $"模擬模式切換為{(value ? "開啟" : "關閉")}");
        }
    }

    /// <summary>模擬模式開關是否可操作：僅 Admin 可切換，且機台不在 RUN/DryRun 執行中。</summary>
    public bool CanToggleSimulationMode => IsAdmin && !IsRunning && !IsDryRunning;

    /// <summary>
    /// 模擬模式專用：按 X 鍵循環切換的板子感測狀態（0-3）。
    /// 0=正常, 1=X3異常, 2=X11異常, 3=X3+X11均異常。
    /// 僅在 IsSimulationMode=true 時生效，OnPlcTick 讀此值直接跳過防抖。
    /// </summary>
    private int _simBoardState;

    /// <summary>由 MainWindow 鍵盤事件呼叫，每次循環切換至下一個模擬狀態。非模擬模式下無效。</summary>
    public void ToggleSimBoardMisplaced()
    {
        if (!IsSimulationMode) return;
        _simBoardState = (_simBoardState + 1) % 4;
        string stateDesc = _simBoardState switch
        {
            1 => "X3 異常",
            2 => "X11 異常",
            3 => "X3+X11 均異常",
            _ => "正常"
        };
        _logger.Debug($"[SIM] SimBoardState 切換為 {_simBoardState}（{stateDesc}）");
    }

    /// <summary>ZL/ZR安定延遲checkbox是否可操作，依「角色權限設定」決定。</summary>
    public bool CanToggleAxisSettleDelay => HasPermission(Permission.AxisSettleDelay);

    /// <summary>
    /// ZL/ZR 到位後是否啟用安定延遲（防止振動拍照模糊）。
    /// 變更時同步儲存 Config。
    /// </summary>
    public bool EnableAxisSettleDelay
    {
        get => _machine.Config.EnableAxisSettleDelay;
        set
        {
            if (_machine.Config.EnableAxisSettleDelay == value) return;
            _machine.Config.EnableAxisSettleDelay = value;
            _machine.Config.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 量測值上限，直接代理 InspectionConfig.ValueLimitHigh。
    /// 變更時同步儲存 Config 並重新評估所有 Slot。
    /// </summary>
    public double UpperLimit
    {
        get => _machine.Config.ValueLimitHigh;
        set
        {
            if (_machine.Config.ValueLimitHigh == value) return;
            _machine.Config.ValueLimitHigh = value;
            _machine.Config.Save();
            OnPropertyChanged();
            EvaluateAllSlots();
        }
    }

    /// <summary>
    /// 量測值下限，直接代理 InspectionConfig.ValueLimitLow。
    /// 變更時同步儲存 Config 並重新評估所有 Slot。
    /// </summary>
    public double LowerLimit
    {
        get => _machine.Config.ValueLimitLow;
        set
        {
            if (_machine.Config.ValueLimitLow == value) return;
            _machine.Config.ValueLimitLow = value;
            _machine.Config.Save();
            OnPropertyChanged();
            EvaluateAllSlots();
        }
    }

    /// <summary>
    /// 依目前上下限重新評估所有 Slot 的 IsNg，並更新統計。
    /// </summary>
    private void EvaluateAllSlots()
    {
        foreach (var slot in AreaA_Row1.Concat(AreaB_Row1))
            slot.IsNg = !(LowerLimit <= slot.Value && slot.Value <= UpperLimit);

        AreaA_Stats.Calculate(AreaA_Row1);
        AreaB_Stats.Calculate(AreaB_Row1);
    }

    private static readonly UserAccount NotLoggedInPlaceholder = new()
    {
        Username = "（尚未登入）",
        Role = UserRole.Operator
    };

    /// <summary>
    /// user 為 null 表示開機後尚未完成第一次登入：畫面建立後立刻是鎖定狀態，
    /// 主視窗（含機台狀態監控）正常顯示，但要等 App.xaml.cs 呼叫 CompleteLogin 才能操作。
    /// </summary>
    public MainViewModel(UserAccount? user = null)
    {
        CurrentUser = user ?? NotLoggedInPlaceholder;
        if (user == null)
            IsLocked = true;

        _mesClient.Load();
        // START
        StartCommand = new RelayCommand(OnStart, () => IsInitialized && !IsRunning && !IsDryRunning);
        StopCommand = new RelayCommand(OnStop, () => HasPermission(Permission.Stop));
        // 主流程：逐 Slot 移動、取像、存圖
        // 允許按 Run 的條件：裝置已就緒，且 (已完成原點賦歸 OR ZL/ZR 已達安全高度)，同時非正在執行/非 DryRun，且光閘未遮斷，且板子已放妥，且該角色有RUN權限。
        RunCommand = new RelayCommand(OnRunInspection, () => IsDeviceReady && (IsInitialized || IsZAtSafeHeight) && !IsRunning && !IsDryRunning && !IsLightGateBlocked && !IsBoardMisplaced && HasPermission(Permission.Run));
        // 初始化：Config + Servo + PLC（不含原點賦歸）
        InitAxesCommand = new RelayCommand(OnInitDevices, () => !IsRunning && !IsDryRunning && !IsDeviceReady && HasPermission(Permission.InitDevices));
        // 原點賦歸：光閘必須未遮斷才可按（不因暫停光閘而全域禁用）
        HomeCommand = new RelayCommand(OnHome, () => IsDeviceReady && !IsRunning && !IsDryRunning && !IsLightGateBlocked && HasPermission(Permission.Home));
        // 讀取條碼：Y→491 → 偵測 X12/X13 → 移動 X 軸
        ReadBarcodeCommand = new RelayCommand(OnReadBarcode, () => IsDeviceReady && !IsRunning && !IsDryRunning && HasPermission(Permission.ReadBarcode));
        // 移至取料位：Y 軸移動到取料位座標
        MoveToPickPositionCommand = new RelayCommand(OnMoveToPickPosition, () => IsDeviceReady && !IsRunning && !IsDryRunning && HasPermission(Permission.MoveToPickPosition));
        // 座標設定：依「角色權限設定」決定 Operator/Engineer 是否可開啟，Admin永遠可以
        PositionSettingsCommand = new RelayCommand(OnPositionSettings, () => !IsRunning && !IsDryRunning && HasPermission(Permission.PositionSettings));

        // 裁切設定：依「角色權限設定」決定
        CropSettingsCommand = new RelayCommand(OnCropSettings, () => HasPermission(Permission.CropSettings));

        // 亮度設定：依「角色權限設定」決定
        LightSettingsCommand = new RelayCommand(OnLightSettings, () => HasPermission(Permission.LightSettings));

        // 點擊圖像放大預覽
        OpenImageViewerCommand = new RelayCommand<SlotItem>(OnOpenImageViewer);

        // 開啟 CSV 報表資料夾
        OpenResultFolderCommand = new RelayCommand(OnOpenResultFolder, () => HasPermission(Permission.OpenResultFolder));

        // 條碼 TextBox Enter 確認
        ConfirmBarcodeCommand = new RelayCommand(OnConfirmBarcode);

        // 暫停光閘 toggle
        ToggleLightGateBypassCommand = new RelayCommand(OnToggleLightGateBypass, () => HasPermission(Permission.ToggleLightGateBypass));

        // 暫停板子感測 toggle：僅 Admin 可操作，不透過角色權限表開放
        ToggleBoardSensorBypassCommand = new RelayCommand(OnToggleBoardSensorBypass, () => IsAdmin);

        // SLC Reset
        SlcResetCommand = new RelayCommand(OnSlcReset, () => IsDeviceReady && !IsRunning && !IsDryRunning && HasPermission(Permission.SlcReset));

        // WebAPI 模擬測試：僅Admin
        WebApiTestCommand = new RelayCommand(OnWebApiTest, () => IsAdmin);

        // 修改自己密碼：任何角色都可以
        ChangePasswordCommand = new RelayCommand(OnChangePassword);

        // 帳號管理：僅Admin
        OpenUserManagementCommand = new RelayCommand(OnOpenUserManagement, () => IsAdmin);

        // 操作紀錄檢視：僅Admin
        OpenAuditLogCommand = new RelayCommand(OnOpenAuditLog, () => IsAdmin);

        // 角色權限設定：僅Admin
        OpenRolePermissionCommand = new RelayCommand(OnOpenRolePermission, () => IsAdmin);

        // 登出：鎖定畫面＋彈出LoginWindow要求重新登入，不結束程式
        LogoutCommand = new RelayCommand(OnLogout);

        // 鎖定畫面「重新登入」：LoginWindow被直接關掉/取消後，靠這顆按鈕再叫出一次
        RetryLoginCommand = new RelayCommand(ShowLoginWindow);

        // 先填入空白 Slot 佔位（UI 不會是空的）
        FillSlots(AreaA_Row1, 25, 25);
        FillSlots(AreaB_Row1, 25, 25);

        _timer.Tick += (_, _) =>
        {
            CurrentDate = DateTime.Now.ToString("yyyy/MM/dd");
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
        };

        // 異常燈號的單一事實來源在 _machine.Alarms；這裡只負責在它變動時刷新 UI 綁定的唯讀屬性。
        // StateChanged 可能從非 UI 執行緒觸發（例如背景 Task 裡的 Alarms.Raise），一律 BeginInvoke。
        _machine.Alarms.StateChanged += () => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(IsAlarmActive));
            OnPropertyChanged(nameof(ActiveAlarms));
            NotifyRunStateChanged();
            // 立即嘗試同步一次 PLC 輸出，讓紅燈盡快反映；OnPlcTick 裡的週期性斷言則負責在
            // 這次寫入因 PLC 未連線而失敗時，於下一個 tick 自動補回正確狀態。
            _machine.SyncRedAlarmOutput();
        });

        _plcTimer.Tick += OnPlcTick;

        // MES 重試計時器訂閱
        _mesRetryTimer.Tick += OnMesRetryTick;

        // 閒置逾時：視同自動登出，要求重新完整登入（不是輸入同一使用者密碼的快速解鎖）
        _idleTimer.Interval = TimeSpan.FromMinutes(_machine.Config.IdleLockTimeoutMinutes);
        _idleTimer.Tick += (_, _) =>
        {
            _idleTimer.Stop();
            LockAndRequireLogin("閒置逾時，自動登出");
        };
        ResetIdleTimer();

        // 計時器（時鐘/PLC輪詢）跟MES連線都不在建構式裡馬上啟動：開機第一次登入前，LoginWindow用的
        // 是跟MainWindow同一個UI執行緒（ShowDialog是nested message loop），這些背景工作若提早啟動，
        // 會在使用者於登入視窗輸入帳密的同時持續佔用UI執行緒，造成輸入卡頓。改成登入成功後才啟動。
        if (user != null)
            StartBackgroundWork();
    }

    private bool _backgroundWorkStarted;

    /// <summary>啟動時鐘/PLC輪詢計時器與MES連線，僅在第一次登入成功時執行一次（登出後重新登入不重複啟動）。</summary>
    private void StartBackgroundWork()
    {
        if (_backgroundWorkStarted) return;
        _backgroundWorkStarted = true;

        _timer.Start();
        _plcTimer.Start();

        // ★ 登入成功後非同步連線 MES（不阻塞 UI）
        _ = InitMesAsync();
    }

    /// <summary>程式啟動：建立 WebApidll 並上報 Idle 狀態。</summary>
    private async Task InitMesAsync()
    {
        // 設定檔裡「啟用 MES WebAPI 功能」關閉時，CimApiClient.ConnectAsync() 內部會直接
        // 回傳 Disabled、不發任何連線，這裡只要照實反映狀態，不能落入下面的重試分支，
        // 否則會每 15 秒白白嘗試一次「已停用」的功能。
        if (!_mesClient.IsEnabled)
        {
            MesStatus = MesConnectionStatus.Disabled;
            StatusMessage = "MES WebAPI 功能已停用（設定檔），系統就緒。";
            _mesRetryTimer.Stop();
            return;
        }

        StatusMessage = "正在連線 MES...";
        await _mesClient.ConnectAsync();
        MesStatus = _mesClient.LastStatus;

        if (MesStatus == MesConnectionStatus.Connected)
        {
            StatusMessage = "MES 連線成功，系統就緒。";
            _mesRetryTimer.Stop();
        }
        else
        {
            StatusMessage = "MES 未連線，將每 15 秒自動重試...";
            _mesRetryTimer.Start();
        }
    }

    /// <summary>MES 重試計時器 Tick：尚未連線時每 15 秒自動重試。</summary>
    private async void OnMesRetryTick(object? sender, EventArgs e)
    {
        // 已連線，或功能已被停用，都不該繼續重試
        if (_mesClient.LastStatus == MesConnectionStatus.Connected || !_mesClient.IsEnabled)
        {
            _mesRetryTimer.Stop();
            return;
        }

        // ★ 重試記錄會透過 CimApiClient.ConnectAsync() 內的 WriteLog 寫入 WebApiLog/
        StatusMessage = "MES 重試連線中...";
        await _mesClient.ConnectAsync();
        MesStatus = _mesClient.LastStatus;

        if (MesStatus == MesConnectionStatus.Connected)
        {
            StatusMessage = "MES 重試連線成功！";
            _mesRetryTimer.Stop();
        }
        else
        {
            StatusMessage = "MES 重試失敗，將在 15 秒後再次嘗試...";
        }
    }

    /// <summary>程式關閉時呼叫，上報 MES Disconnected。</summary>
    public async Task DisconnectMesAsync()
        => await _mesClient.DisconnectAsync();

    /// <summary>
    /// 每 100ms 輪詢一次 PLC X1+X2 訊號。
    /// 兩個訊號同時為 true 且持續 0.5 秒後，自動觸發空跑程式。
    /// </summary>
    private void OnPlcTick(object? sender, EventArgs e)
    {
        // 每個 tick 都重新斷言紅燈輸出（依 _machine.Alarms.IsActive）。
        // 這一行是修掉「紅燈卡死關不掉」的關鍵：Y01 是實體 PLC 線圈，任何一次寫入都可能因為
        // PLC 剛好未連線而被 SetPlcYOctal 悄悄丟棄（尤其是開機當下第一次嘗試熄燈——那時候
        // PLC 通常都還沒連上）。靠這裡每 500ms 持續重新斷言，PLC 重新連線後最多一個 tick
        // 就會自動補回正確狀態，不需要使用者手動按任何按鈕才能讓燈熄滅。
        _machine.SyncRedAlarmOutput();

        // X7 光閘狀態：不論任何模式都追蹤，LOW = 遮斷（加入防抖：需連續 LightGateDebounceCount 次才觸發）
        bool x7 = _machine.GetPlcXOctal(7) ?? true;
        bool rawBlocked = !x7;

        // 先更新防抖計數，再 Enqueue，確保歷史裡的 Count 是當下真實值
        if (rawBlocked)
        {
            _lightGateBlockedCount++;
        }
        else
        {
            _lightGateBlockedCount = 0;
        }

        // 每次 Tick 都將原始訊號與當下 count 存入歷史佇列（最多 X7HistorySize 筆）
        _x7SignalHistory.Enqueue((DateTime.Now, x7, rawBlocked, _lightGateBlockedCount));
        if (_x7SignalHistory.Count > X7HistorySize)
            _x7SignalHistory.Dequeue();

        bool blocked = (_lightGateBlockedCount >= LightGateDebounceCount);
         


          //  bool blocked = _lightGateBlockedCount >= LightGateDebounceCount;

        // 光閘確認觸發（從未遮斷 → 遮斷，且已通過防抖確認）
        if (blocked && !_isLightGateBlocked)
        {
            if (!IsLightGateBypassActive)
            {
                // 正常模式：取消流程並強制重新原點賦歸
                _cts?.Cancel();
                IsInitialized = false;
                StatusMessage = "⚠ 光閘觸發！請解除光閘後執行原點賦歸才可繼續 RUN";
                _machine.Alarms.Raise("LightGateX7", "X7 光閘遮斷，流程已取消，等待原點賦歸");

                // ── MES 上報 Alarm（光閘觸發事件）──
                _ = _mesClient.ReportAlarmAsync()
                    .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                        TaskScheduler.FromCurrentSynchronizationContext());

                // 組合前 N 筆 X7 訊號歷史一併寫入 log
                var historySb = new System.Text.StringBuilder();
                historySb.AppendLine($"--- 觸發前 X7 訊號歷史（最多 10 筆，門檻={LightGateDebounceCount}）---");
                int idx = 1;
                foreach (var entry in _x7SignalHistory)
                {
                    string countInfo = entry.Blocked ? $"count={entry.Count}" : "正常";
                    string trigger  = (entry.Blocked && entry.Count >= LightGateDebounceCount) ? " ✅ 本筆觸發" : "";
                    historySb.AppendLine(
                        $"  [{idx++:D2}] {entry.Time:HH:mm:ss.fff} | X7={entry.X7Raw,-5} | rawBlocked={entry.Blocked,-5} | {countInfo}{trigger}");
                }

                CancelEventLogger.Write(
                    "光閘X7",
                    "X7 光閘遮斷觸發，流程已取消，等待原點賦歸\n" + historySb);
            }
            else
            {
                // 取料暫停光閘模式：僅顯示提示，不中斷流程
                StatusMessage = "⚠ 光閘觸發（取料暫停模式，流程不中斷）";
            }
        }

        IsLightGateBlocked = blocked;

        // ─── X3/X11 板子放置雷射感測（任一 true = 未放好）───
        bool x3 = false, x11 = false, rawMisplaced = false, boardMisplacedConfirmed = false, boardOkConfirmed = false;
        bool boardReadingValid = false; // true 表示這次 x3/x11 是實際讀到的值，可信任、可寫入持續追蹤 log

        if (IsSimulationMode)
        {
            // 模擬模式：依 _simBoardState 決定 x3/x11，跳過防抖立即生效
            // bit0=1 → X3 異常；bit1=1 → X11 異常（0=正常,1=X3,2=X11,3=兩者）
            x3  = (_simBoardState & 1) != 0;
            x11 = (_simBoardState & 2) != 0;
            rawMisplaced            = x3 || x11;
            boardMisplacedConfirmed = rawMisplaced;
            boardOkConfirmed        = !rawMisplaced;
            _boardMisplacedCount = 0;
            _boardPlacedCount    = 0;
            boardReadingValid = true;
        }
        else
        {
            bool? rawX3  = _machine.GetPlcXOctal(3);
            bool? rawX11 = _machine.GetPlcXOctal(11);

            if (rawX3 == null || rawX11 == null)
            {
                // PLC 斷線：凍結現有狀態，不更新計數器、不觸發任何轉換
                // 若板子本來就未放好，警報繼續保持；若本來正常，也不會誤觸發
                _boardMisplacedCount = 0;
                _boardPlacedCount    = 0;
                // boardMisplacedConfirmed / boardOkConfirmed 維持 false（宣告時預設值）→ 兩個分支都不進入
            }
            else
            {
                x3  = rawX3.Value;
                x11 = rawX11.Value;
                rawMisplaced = x3 || x11;

                if (rawMisplaced) { _boardMisplacedCount++; _boardPlacedCount = 0; }
                else              { _boardPlacedCount++;    _boardMisplacedCount = 0; }

                boardMisplacedConfirmed = _boardMisplacedCount >= BoardMisplacedDebounceCount;
                boardOkConfirmed        = _boardPlacedCount    >= BoardPlacedDebounceCount;
                boardReadingValid = true;
            }
        }

        // 每次 Tick 都將原始訊號與當下 count 存入歷史佇列（最多 BoardHistorySize 筆）
        _boardSignalHistory.Enqueue((DateTime.Now, x3, x11, rawMisplaced, _boardMisplacedCount, _boardPlacedCount));
        if (_boardSignalHistory.Count > BoardHistorySize)
            _boardSignalHistory.Dequeue();

        // 持續追蹤：只要 X3/X11 原始值跟上次「有效讀值」不同，立刻寫一行 log，
        // 不受防抖/確認狀態、也不受 Run 狀態影響，藉此還原完整的 true/false 時間軸。
        if (boardReadingValid && (x3 != _prevX3RawLogged || x11 != _prevX11RawLogged))
        {
            BoardSensorTraceLogger.Write(x3, x11);
            _prevX3RawLogged = x3;
            _prevX11RawLogged = x11;
        }

        // Admin 暫停板子感測中：完全略過確認/觸發判斷，強制視為正常，不卡 RUN。
        // 訊號歷史與持續追蹤 log 在上面已經照常記錄，僅此段套用判斷結果的邏輯被跳過。
        if (IsBoardSensorBypassActive)
        {
            if (_isBoardMisplaced)
            {
                IsBoardMisplaced = false;
                _boardMisplacedDialogShown = false;
            }
        }
        // 正常 → 未放好（初次確認觸發）
        else if (boardMisplacedConfirmed && !_isBoardMisplaced)
        {
            IsBoardMisplaced = true;
            StatusMessage = "⚠ 板子未放妥（雷射感測 X3/X11），請確認板子正確放置後再繼續";
            _logger.Warn($"板子未放妥 (X3={x3} X11={x11})，Run 已禁用");

            // 組合前 N 筆 X3/X11 訊號歷史一併寫入 log，方便判斷是穩定 false 還是抖動
            var boardHistorySb = new System.Text.StringBuilder();
            boardHistorySb.AppendLine($"--- 觸發前 X3/X11 訊號歷史（最多 {BoardHistorySize} 筆，門檻={BoardMisplacedDebounceCount}）---");
            int boardIdx = 1;
            foreach (var entry in _boardSignalHistory)
            {
                string trigger = (entry.RawMisplaced && entry.MisplacedCount >= BoardMisplacedDebounceCount) ? " ✅ 本筆觸發" : "";
                boardHistorySb.AppendLine(
                    $"  [{boardIdx++:D2}] {entry.Time:HH:mm:ss.fff} | X3={entry.X3Raw,-5} | X11={entry.X11Raw,-5} | rawMisplaced={entry.RawMisplaced,-5} | misplacedCount={entry.MisplacedCount} placedCount={entry.PlacedCount}{trigger}");
            }

            if (IsRunning || IsDryRunning)
            {
                _cts?.Cancel();
                IsInitialized = false;
                CancelEventLogger.Write("板子感測X3/X11", $"板子未放妥 (X3={x3} X11={x11})，流程已取消，等待板子放置\n" + boardHistorySb);
            }
            else
            {
                CancelEventLogger.Write("板子感測X3/X11", $"板子未放妥 (X3={x3} X11={x11})\n" + boardHistorySb);
            }

            if (!_boardMisplacedDialogShown)
            {
                _boardMisplacedDialogShown = true;
                string triggeredSignals = (x3 && x11) ? "X3、X11" : (x3 ? "X3" : "X11");
                // BeginInvoke 讓對話框在 DispatcherTimer tick 結束後才彈出，
                // 避免 MessageBox 建立巢狀訊息泵（re-entrancy）導致 tick 重複執行
                Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    MessageBox.Show(
                        $"偵測到板子未放妥（{triggeredSignals} 訊號異常）。\n請確認板子正確放置後再繼續操作。",
                        "板子未放好",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                });
            }
        }
        // 未放好 → 正常（訊號穩定回到正常後解除）
        else if (boardOkConfirmed && _isBoardMisplaced)
        {
            IsBoardMisplaced = false;
            _boardMisplacedDialogShown = false;
            _logger.Info($"板子放妥確認 (X3={x3} X11={x11})，Run 已恢復");
            StatusMessage = "✔ 板子放妥確認，Run 已恢復";
        }

        // PLC/位置狀態變動可能影響按鈕可用性（例如 Z 到位），通知 WPF 重新查詢 CanExecute
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();

        if (!IsDeviceReady || IsRunning || IsDryRunning) return;

        if (IsLightGateBlocked) return;

        bool x1 = _machine.GetPlcXOctal(1) ?? false; // X1
        bool x2 = _machine.GetPlcXOctal(2) ?? false; // X2

        if (x1 && x2)
        {
            if (!_x1x2Stopwatch.IsRunning)
                _x1x2Stopwatch.Restart();

            if (_x1x2Stopwatch.Elapsed >= DryRunTriggerDuration)
            {
                _x1x2Stopwatch.Reset();
                OnRunInspection();
            }
        }
        else
        {
            _x1x2Stopwatch.Reset();
        }
    }

    /// <summary>
    /// S00：開機自動初始化（由 MainWindow.Loaded 呼叫）
    /// </summary>
    public async Task InitializeAsync()
    {
        // 防止重複初始化
        if (IsInitialized || IsRunning || IsDryRunning)
            return;

        StatusMessage = "正在初始化設備（設定檔 / 伺服軸 / PLC），請稍候...";
        _cts = new CancellationTokenSource();
        // 使用者主動嘗試初始化，視為對「上一次可由使用者操作解除」的異常做確認：
        // 只清可由重試解除的原因，軸警報等硬體原因由 MachineController 在檢查各軸狀態時自行 Raise/Clear。
        _machine.Alarms.Clear("LightGateX7");
        _machine.Alarms.Clear("RunException");

        // Progress 回報會自動回到 UI 執行緒
        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            var result = await Task.Run(
                () => _machine.InitializeAllAsync(progress, _cts.Token));

            IsInitialized = result.AllPassed;

            if (result.AllPassed)
            {
                // S00 完成 → 等待使用者按 START
                StatusMessage = "✔ 初始化完成！請按 RUN 開始作業";
            }
            else
            {
                StatusMessage = "✘ 初始化失敗，請檢查設備後重試";
            }

            // 通知 WPF 重新判斷所有按鈕的 CanExecute
            CommandManager.InvalidateRequerySuggested();

            System.Diagnostics.Debug.WriteLine(result.GetSummary());
        }
        catch (Exception ex)
        {
            StatusMessage = $"✘ 初始化發生錯誤：{ex.Message}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    // ═══════════════════════════════════════
    //  START：完整自動流程
    //  S01a 載台在席偵測(=有料) → S01b 移動讀碼 → S01c 軸復歸 → S03 檢測 → S04 結果
    // ═══════════════════════════════════════

    private async void OnStart()
    {
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _machine.Alarms.Clear("LightGateX7");
        _machine.Alarms.Clear("RunException");

        ResetAllSlots();
        Barcode = "";
        _confirmedBarcode = "";

        var statusProgress = new Progress<string>(msg => StatusMessage = msg);

        // ── MES 上報 Run（非阻塞）──
        _ = _mesClient.ReportRunAsync()
            .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                TaskScheduler.FromCurrentSynchronizationContext());

        try
        {
            // ── S01a：檢查載台在席感測器（PLC X12/X13）──
            // 載台在席 = 料已到位，不需額外等待放料
            StatusMessage = "正在確認載具位置，請稍候...";
            var carrierPos = await Task.Run(() => _machine.DetectCarrierPosition());

            if (carrierPos == MachineController.CarrierPosition.None)
            {
                StatusMessage = "⚠ 未偵測到載具，請確認載具已放置正確後再按 RUN";
                return;
            }

            StatusMessage = $"✔ 偵測到載具位置：{carrierPos}";

            // ── S01b：移動到讀碼位置 → 自動讀碼 ──
            string? code = await Task.Run(
                () => _machine.MoveAndReadBarcodeAsync(carrierPos, statusProgress, _cts.Token));

            if (string.IsNullOrEmpty(code))
            {
                StatusMessage = "條碼讀取失敗，正在將軸退回原點...";
                await Task.Run(() => _machine.MoveBarcodeAxisHomeAsync(statusProgress, _cts.Token));
                StatusMessage = "⚠ 條碼讀取失敗，軸已回原點，請重新放置後按 RUN";
                return;
            }

            // 驗證條碼格式
            var validation = BarcodeValidator.Validate(code);
            if (!validation.IsValid)
            {
                StatusMessage = $"⚠ 條碼格式不符：{validation.Message}";
                return;
            }

            _confirmedBarcode = code;
            Barcode = code;
            StatusMessage = $"✔ 條碼確認：{code}";
            System.Diagnostics.Debug.WriteLine($"[S01] 自動讀碼成功: {code}");

            // ── MES QuerySerialNoData（非阻塞，失敗不中止流程）──
            _ = _mesClient.QueryWorkOrderAsync(_mesClient.EquipmentId, _confirmedBarcode)
                .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                    TaskScheduler.FromCurrentSynchronizationContext());

            // ── S01c：讀碼軸回歸原點 ──
            await Task.Run(
                () => _machine.MoveBarcodeAxisHomeAsync(statusProgress, _cts.Token));

            // ── S03：開始檢測（載台在席已確認有料，直接進入檢測）──
            StatusMessage = $"▶ 開始量測 Slot，條碼：{_confirmedBarcode}";

            var slotProgress = new Progress<SlotInspectionProgress>(report =>
            {
                var collection = report.Target switch
                {
                    SlotInspectionProgress.TargetCollection.AreaA_Row1 => AreaA_Row1,
                    SlotInspectionProgress.TargetCollection.AreaA_Row2 => AreaA_Row1,
                    SlotInspectionProgress.TargetCollection.AreaB_Row1 => AreaB_Row1,
                    SlotInspectionProgress.TargetCollection.AreaB_Row2 => AreaB_Row1,
                    _ => null
                };

                if (collection != null && report.SlotIndex < collection.Count)
                {
                    collection[report.SlotIndex].Value       = report.Value;
                    collection[report.SlotIndex].IsNg        = !(LowerLimit <= report.Value && report.Value <= UpperLimit);
                    collection[report.SlotIndex].MeanIntensity       = report.MeanIntensity;
                    collection[report.SlotIndex].DistanceToRoiCenter = report.DistanceToRoiCenter;
                    collection[report.SlotIndex].DefectBoxes =
                        report.DefectBoundingBoxes.Select(r => new Rect(r.X, r.Y, r.Width, r.Height)).ToList();
                    collection[report.SlotIndex].ImageSource = report.Image;
                }

                StatusMessage = report.StatusText;
            });

            await Task.Run(
                () => _machine.RunInspectionAsync(_confirmedBarcode, slotProgress, _cts.Token));

            // 全部完成 → 計算統計
            AreaA_Stats.Calculate(AreaA_Row1);
            AreaB_Stats.Calculate(AreaB_Row1);

            bool pass = AreaA_Stats.Result == "OK" && AreaB_Stats.Result == "OK";

            // S04：寫彙總結果
            InspectionResultWriter.WriteSummary(_confirmedBarcode, pass);

            // ── MES MeasureResultUpload（非阻塞，失敗不中止流程）──
            // ProductId = 整個條碼；RecipeId = CarrierSide (R/L)
            var endTime1     = DateTime.Now;
            var bumperListStart = BuildBumperResultsData(AreaA_Row1, AreaB_Row1, _confirmedBarcode,
                CarrierSide, LowerLimit, UpperLimit, _runStartTime, endTime1);
            _ = _mesClient.UploadResultAsync(_mesClient.EquipmentId, _confirmedBarcode, CarrierSide, pass,
                    bumperListStart,
                    _runStartTime.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    endTime1.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                    TaskScheduler.FromCurrentSynchronizationContext());

            StatusMessage = $"量測完成  {_confirmedBarcode}  →  {(pass ? "✔ PASS" : "✘ FAIL")}";
            System.Diagnostics.Debug.WriteLine(
                $"[S04] 完成: {_confirmedBarcode} = {(pass ? "PASS" : "FAIL")}");
        }
        catch (OperationCanceledException oce)
        {
            IsInitialized = false;
            StatusMessage = "⏹ 作業已由使用者取消，請執行原點賦歸後再繼續 RUN";
            CancelEventLogger.Write("OnStart", "流程被取消（OperationCanceledException）", oce);
        }
        catch (Exception ex)
        {
            IsInitialized = false;
            StatusMessage = $"✘ 量測發生錯誤：{ex.Message}，請執行原點賦歸後再繼續 RUN";
            System.Diagnostics.Debug.WriteLine(ex);
            CancelEventLogger.Write("OnStart", $"流程例外：{ex.Message}", ex);
            _machine.Alarms.Raise("RunException", ex.Message);
            // ── MES 上報 Alarm（非阻塞）──
            _ = _mesClient.ReportAlarmAsync()
                .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                    TaskScheduler.FromCurrentSynchronizationContext());
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            _confirmedBarcode = "";
            Barcode = "";
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async void OnRunInspection()
    {
        // 注意：這個方法除了RUN按鈕，也會被 OnPlcTick 的 X1/X2 感應器持續觸發邏輯自動呼叫，
        // 所以記錄成中性的「開始量測」，不寫成「使用者按下RUN」，避免錯誤宣稱是手動操作。
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "開始量測 (RUN)");
        IsDryRunning = true;
        _cts = new CancellationTokenSource();
        _machine.Alarms.Clear("LightGateX7");
        _machine.Alarms.Clear("RunException");
        _runStartTime = DateTime.Now;

        // X10 邊緣偵測狀態重置（X7 改為電平觸發，不需 _prevX7）
        _prevX10 = false;

        // 清除上一輪的影像 / 數值 / 狀態
        ResetAllSlots();

        var statusProgress = new Progress<string>(msg => StatusMessage = msg);

        // ── MES 上報 Run（非阻塞）──
        _ = _mesClient.ReportRunAsync()
            .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                TaskScheduler.FromCurrentSynchronizationContext());

        try
        {
            var ct = _cts.Token;

            // ── 模擬模式：跳過所有機台動作，使用假資料直接走 WebAPI 流程 ──
            if (IsSimulationMode)
            {
                await RunSimulationAsync(ct);
                return;
            }

            // ── STEP 0：讀取條碼 ──────────────────────────────────────────
            StatusMessage = "正在移動至條碼讀取位置...";

            // ── ZL/ZR 安全高度確認（在 Y 移動前）──
            bool x12pre = _machine.GetPlcXOctal(12) ?? false;
            bool x13pre = _machine.GetPlcXOctal(13) ?? false;
            double safeZL = _machine.Config.GetZLSafeHeight(x12pre, x13pre);
            double safeZR = _machine.Config.GetZRSafeHeight(x12pre, x13pre);
            _logger.Info($"[OnRunInspection|STEP0] 讀條碼前 Z 安全高度讀取 | X12={x12pre} X13={x13pre} safeZL={safeZL:F3} safeZR={safeZR:F3}");
            await _machine.EnsureZSafeBeforeYMoveAsync(safeZL, safeZR, statusProgress, ct, "OnRunInspection-STEP0");

            // Y 軸移動至讀碼位置
            StatusMessage = $"Y 軸移動至條碼讀取位置 {_machine.Config.BarcodePositionY:F1} mm，請稍候...";
            _logger.Info($"[OnRunInspection|STEP0] Y 軸移動至讀碼位置 {_machine.Config.BarcodePositionY:F1} mm");
            await Task.Yield();
            _machine.Axes["AxisY"].MotMoveAbs(_machine.Config.BarcodePositionY);
            await Task.Delay(150, ct);
            bool yArrived = await MachineController.WaitUntilAsync(
                () => _machine.Axes["AxisY"].Wait(),
                TimeSpan.FromSeconds(30), ct);
            if (!yArrived)
            {
                StatusMessage = "⚠ Y 軸移動超時，流程中止，請確認設備狀態";
                return;
            }
            StatusMessage = $"✔ Y 軸已到達指定位置（{_machine.Config.BarcodePositionY:F1} mm）";

            // 偵測 X12 / X13 載台訊號
            bool x12 = _machine.GetPlcXOctal(12) ?? false;
            bool x13 = _machine.GetPlcXOctal(13) ?? false;

            double targetX = x12 ? _machine.Config.BarcodePositionLeftX : _machine.Config.BarcodePositionRightX;
            CarrierSide = x12 ? "R" : "L";
            // X12=ON: 左相機=C4, 右相機=C3；X13=ON: 左相機=C2, 右相機=C1
            AreaATitle = x12 ? "Area A  (C4)" : "Area A  (C2)";
            AreaBTitle = x12 ? "Area B  (C3)" : "Area B  (C1)";
            string detectedSig = x12 ? "X12" : "X13";
            StatusMessage = $"偵測到 {detectedSig} 訊號，X 軸移動至 {targetX:F1} mm，請稍候...";
            await Task.Yield();
            _machine.Axes["AxisX"].MotMoveAbs(targetX);
            await Task.Delay(150, ct);
            bool xArrived = await MachineController.WaitUntilAsync(
                () => _machine.Axes["AxisX"].Wait(),
                TimeSpan.FromSeconds(30), ct);
            if (!xArrived)
            {
                StatusMessage = "⚠ X 軸移動超時，流程中止，請確認設備狀態";
                return;
            }
            StatusMessage = $"✔ X 軸已到達指定位置（{targetX:F1} mm）";

            // 觸發讀碼器，等待條碼（含讀碼器回應時間）
            StatusMessage = "正在讀取條碼，請稍候...";
            string? code = await _machine.TriggerReadBarcodeAsync(statusProgress, ct);

            if (string.IsNullOrEmpty(code))
            {
                Barcode = "";
                StatusMessage = "⚠ 條碼讀取失敗，請在條碼欄位手動輸入後按 Enter 繼續...";
                _barcodeTcs = new TaskCompletionSource<string>();
                using var ctr = ct.Register(() => _barcodeTcs.TrySetCanceled());
                try
                {
                    code = await _barcodeTcs.Task;
                }
                catch (OperationCanceledException)
                {
                    _confirmedBarcode = "";
                    CarrierSide = "";
                    AreaATitle = "Area A";
                    AreaBTitle = "Area B";
                    StatusMessage = "⚠ 使用者取消手動輸入，流程中止";
                    return;
                }
                finally
                {
                    _barcodeTcs = null;
                }
            }

            _confirmedBarcode = code;
            Barcode = code;
            StatusMessage = $"✔ 條碼確認：{code}，準備開始量測...";

            // ── MES QuerySerialNoData（非阻塞，失敗不中止流程）──
            _ = _mesClient.QueryWorkOrderAsync(_mesClient.EquipmentId, _confirmedBarcode)
                .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                    TaskScheduler.FromCurrentSynchronizationContext());
            // ─────────────────────────────────────────────────────────────
            StatusMessage = "主流程進行中，正在量測各 Slot...";

            var dryRunSlotProgress = new Progress<SlotInspectionProgress>(report =>
            {
                var collection = report.Target switch
                {
                    SlotInspectionProgress.TargetCollection.AreaA_Row1 => AreaA_Row1,
                    SlotInspectionProgress.TargetCollection.AreaA_Row2 => AreaA_Row1,
                    SlotInspectionProgress.TargetCollection.AreaB_Row1 => AreaB_Row1,
                    SlotInspectionProgress.TargetCollection.AreaB_Row2 => AreaB_Row1,
                    _ => null
                };

                if (collection != null && report.SlotIndex < collection.Count)
                {
                    if (report.Image != null)
                        collection[report.SlotIndex].ImageSource = report.Image;
                    if (report.Value != 0)
                    {
                        collection[report.SlotIndex].Value = report.Value;
                        collection[report.SlotIndex].IsNg  = !(LowerLimit <= report.Value && report.Value <= UpperLimit);
                        collection[report.SlotIndex].MeanIntensity       = report.MeanIntensity;
                        collection[report.SlotIndex].DistanceToRoiCenter = report.DistanceToRoiCenter;
                        collection[report.SlotIndex].DefectBoxes =
                            report.DefectBoundingBoxes.Select(r => new Rect(r.X, r.Y, r.Width, r.Height)).ToList();
                    }
                }

                if (!string.IsNullOrEmpty(report.StatusText))
                    StatusMessage = report.StatusText;
            });

            await Task.Run(() => _machine.DryRunAsync(statusProgress, ct, _confirmedBarcode, dryRunSlotProgress));

            // 計算統計並寫入 CSV
            AreaA_Stats.Calculate(AreaA_Row1);
            AreaB_Stats.Calculate(AreaB_Row1);

            bool pass = AreaA_Stats.Result == "OK" && AreaB_Stats.Result == "OK";
            InspectionResultWriter.WriteSummary(_confirmedBarcode, pass);

            // ── MES MeasureResultUpload（非阻塞，失敗不中止流程）──
            // ProductId = 整個條碼；RecipeId = CarrierSide (R/L)
            var endTime2     = DateTime.Now;
            var bumperListRun = BuildBumperResultsData(AreaA_Row1, AreaB_Row1, _confirmedBarcode,
                CarrierSide, LowerLimit, UpperLimit, _runStartTime, endTime2);
            _ = _mesClient.UploadResultAsync(_mesClient.EquipmentId, _confirmedBarcode, CarrierSide, pass,
                    bumperListRun,
                    _runStartTime.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    endTime2.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                            .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                                TaskScheduler.FromCurrentSynchronizationContext());

                        StatusMessage = $"主流程完成  →  {(pass ? "✔ PASS" : "✘ FAIL")}";

                        // ── MES 上報 Idle（RUN 成功完成）──
                        _ = _mesClient.ReportIdleAsync()
                            .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                                TaskScheduler.FromCurrentSynchronizationContext());
                    }
        catch (OperationCanceledException oce)
        {
            IsInitialized = false;
            StatusMessage = "⏹ 主流程已由使用者取消，請執行原點賦歸後再繼續 RUN";
            CancelEventLogger.Write("OnRunInspection", "主流程被取消（OperationCanceledException）", oce);
        }
        catch (Exception ex)
        {
            IsInitialized = false;
            StatusMessage = $"✘ 主流程發生錯誤：{ex.Message}，請執行原點賦歸後再繼續 RUN";
            System.Diagnostics.Debug.WriteLine(ex);
            CancelEventLogger.Write("OnRunInspection", $"主流程例外：{ex.Message}", ex);
            _machine.Alarms.Raise("RunException", ex.Message);
            // ── MES 上報 Alarm（非阻塞）──
            _ = _mesClient.ReportAlarmAsync()
                .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                    TaskScheduler.FromCurrentSynchronizationContext());
        }
        finally
        {
            IsDryRunning = false;
            _cts?.Dispose();
            _cts = null;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async void OnReadBarcode()
    {
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "讀取條碼");
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            StatusMessage = $"Y 軸移動至條碼讀取位置 {_machine.Config.BarcodePositionY} mm，請稍候...";

            // STEP 1: ZL/ZR 安全高度確認（在 Y 移動前）
            var rbProgress = new Progress<string>(msg => StatusMessage = msg);
            bool rbX12 = _machine.GetPlcXOctal(12) ?? false;
            bool rbX13 = _machine.GetPlcXOctal(13) ?? false;
            double rbSafeZL = _machine.Config.GetZLSafeHeight(rbX12, rbX13);
            double rbSafeZR = _machine.Config.GetZRSafeHeight(rbX12, rbX13);
            _logger.Info($"[OnReadBarcode|STEP1] 讀條碼前 Z 安全高度讀取 | X12={rbX12} X13={rbX13} safeZL={rbSafeZL:F3} safeZR={rbSafeZR:F3}");
            await _machine.EnsureZSafeBeforeYMoveAsync(rbSafeZL, rbSafeZR, rbProgress, ct, "OnReadBarcode-STEP1");

            // STEP 1: Y 軸移動到設定位置
            _logger.Info($"[OnReadBarcode|STEP1] Y 軸移動至讀碼位置 {_machine.Config.BarcodePositionY} mm");
            _machine.Axes["AxisY"].MotMoveAbs(_machine.Config.BarcodePositionY);
            bool yArrived = await MachineController.WaitUntilAsync(
                () => _machine.Axes["AxisY"].Wait(),
                TimeSpan.FromSeconds(30), ct);
            if (!yArrived)
            {
                StatusMessage = "⚠ Y 軸移動逾時，請確認設備狀態";
                return;
            }
            StatusMessage = $"✔ Y 軸已到達指定位置（{_machine.Config.BarcodePositionY} mm）";

            // STEP 2: 偵測 X12 / X13 IO 訊號
            bool x12 = _machine.GetPlcXOctal(12) ?? false;
            bool x13 = _machine.GetPlcXOctal(13) ?? false;

            if (!x12 && !x13)
            {
                StatusMessage = "⚠ 未偵測到載具訊號 (X12/X13)，請確認載具位置";
                return;
            }

            // STEP 3: 依據訊號移動 X 軸
            double targetX;
            if (x12)
            {
                targetX = _machine.Config.BarcodePositionLeftX;
                StatusMessage = $"偵測到 X12 訊號，X 軸移動至 {targetX} mm，請稍候...";
            }
            else
            {
                targetX = _machine.Config.BarcodePositionRightX;
                StatusMessage = $"偵測到 X13 訊號，X 軸移動至 {targetX} mm，請稍候...";
            }

            _machine.Axes["AxisX"].MotMoveAbs(targetX);
            bool xArrived = await MachineController.WaitUntilAsync(
                () => _machine.Axes["AxisX"].Wait(),
                TimeSpan.FromSeconds(30), ct);
            if (!xArrived)
            {
                StatusMessage = "⚠ X 軸移動逾時，請確認設備狀態";
                return;
            }

            StatusMessage = $"✔ X 軸已到位（{targetX} mm），正在觸發讀碼...";

            // STEP 4: 觸發讀碼器讀取條碼並顯示到 UI
            var statusProgress = new Progress<string>(msg => StatusMessage = msg);
            string? code = await _machine.TriggerReadBarcodeAsync(statusProgress, ct);

            if (string.IsNullOrEmpty(code))
            {
                Barcode = "";
                StatusMessage = "⚠ 條碼讀取失敗，請在條碼欄位手動輸入後按 Enter 確認...";
                _barcodeTcs = new TaskCompletionSource<string>();
                using var ctr = ct.Register(() => _barcodeTcs.TrySetCanceled());
                try
                {
                    code = await _barcodeTcs.Task;
                    _confirmedBarcode = code;
                    Barcode = code;
                    StatusMessage = $"✔ 條碼已確認（手動）：{code}";
                }
                catch (OperationCanceledException)
                {
                    _confirmedBarcode = "";
                    StatusMessage = "⚠ 使用者取消手動輸入";
                }
                finally
                {
                    _barcodeTcs = null;
                }
            }
            else
            {
                _confirmedBarcode = code;
                Barcode = code;
                StatusMessage = $"✔ 條碼讀取成功：{code}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "⏹ 讀碼作業已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✘ 讀碼發生錯誤：{ex.Message}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void OnStop()
    {
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "按下 STOP");
        IsRunning = false;
        IsDryRunning = false;
        _cts?.Cancel();
        // Stop 後必須重新原點賦歸才可再按 RUN
        IsInitialized = false;
        StatusMessage = "⏹ 作業已由使用者取消，請執行原點賦歸後再繼續 RUN";
        CancelEventLogger.Write("OnStop", "使用者按下 STOP 按鈕，流程已中止");
    }

    private async void OnMoveToPickPosition()
    {
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "移至取料位");
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            double targetY = _machine.Config.BarcodePositionY;
            StatusMessage = $"Y 軸移動至取料位置 {targetY} mm，請稍候...";

            _machine.Axes["AxisY"].MotMoveAbs(targetY);
            bool arrived = await MachineController.WaitUntilAsync(
                () => _machine.Axes["AxisY"].Wait(),
                TimeSpan.FromSeconds(30), ct);

            StatusMessage = arrived
                ? $"✔ Y 軸已到達取料位置（{targetY} mm）"
                : "⚠ Y 軸移動逾時，請確認設備狀態";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "⏹ 移動至取料位置已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✘ 移動至取料位置發生錯誤：{ex.Message}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// 開啟座標設定對話視窗，確認後寫回 InspectionConfig。
    /// </summary>
    private void OnPositionSettings()
    {
        var vm = new PositionSettingsViewModel { CurrentUser = CurrentUser };
        vm.LoadFrom(_machine.Config);

        var win = new PositionSettingsWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };

        if (win.ShowDialog() == true)
        {
            vm.ApplyTo(_machine.Config);
            SlotPositionTable.Save();
            _machine.Config.Save();
            StatusMessage = "✔ 位置設定已儲存";
            AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "套用座標設定");
        }
    }

    private void OnConfirmBarcode()
    {
        if (_barcodeTcs is null) return;
        string input = Barcode.Trim();
        if (string.IsNullOrWhiteSpace(input)) return;
        _barcodeTcs.TrySetResult(input);
    }

    private void OnOpenResultFolder()
    {
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "開啟結果資料夾");
        string folder = Slot_Inspection.Services.InspectionResultWriter.ResultsRootDirectory;
        if (!System.IO.Directory.Exists(folder))
            System.IO.Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start("explorer.exe", folder);
    }

    private void OnOpenImageViewer(SlotItem? slot)
    {
        if (slot?.ImageSource is not System.Windows.Media.Imaging.BitmapSource) return;

        // 直接傳入活的 collection，ImageViewerWindow 內部訂閱變動事件，可即時更新
        var viewer = new Slot_Inspection.Views.ImageViewerWindow(
            [AreaA_Row1, AreaB_Row1],
            slot)
        {
            Owner = Application.Current.MainWindow
        };
        viewer.Show();
    }

    /// <summary>
    /// 開啟相機亮度設定對話視窗；按 OK 後寫回 InspectionConfig 並儲存至 JSON。
    /// </summary>
    private void OnLightSettings()
    {
        var vm = new LightSettingsViewModel(_machine.Config, IsAdmin);
        var win = new Views.LightSettingsWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };
        if (win.ShowDialog() == true)
        {
            vm.ApplyTo(_machine.Config);
            StatusMessage = $"✔ 光源設定已儲存（左={vm.LightIntensityLeft}，右={vm.LightIntensityRight}）";
            AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "套用光源設定");
            if (IsAdmin)
                AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, $"套用曝光時間設定（左={vm.ExposureTimeLeft:F0}µs，右={vm.ExposureTimeRight:F0}µs）");
        }
    }

    /// <summary>
    /// 開啟裁切設定對話視窗
    /// </summary>
    private void OnCropSettings()
    {
        var vm = new CropSettingsViewModel(_machine.Config);

        var win = new Views.CropSettingsWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };

        if (win.ShowDialog() == true)
        {
            vm.ApplyTo(_machine.Config);
            _machine.Config.Save();
            StatusMessage = "✔ 裁切範圍設定已儲存";
            AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "套用裁切設定");
        }
    }


    /// 初始化按鈕：Config + Servo 激磁 + PLC 連線（不含原點賦歸）。
    /// </summary>
    private async void OnInitDevices()
    {
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "初始化設備");
        IsDryRunning = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {

            SlotPositionTable.Load();

            StatusMessage = "正在初始化設備（設定檔 / 伺服軸 / PLC），請稍候...";
            var result = await Task.Run(
                () => _machine.InitializeDevicesAsync(progress, _cts.Token));

            IsDeviceReady = result.AllPassed;

            if (result.AllPassed)
            {
                // 每次初始化完成後強制重置，必須重新原點賦歸才能 RUN
                IsInitialized = false;
                StatusMessage = "✔ 設備初始化成功！請按【原點賦歸】才可繼續 RUN";
            }
            else
            {
                var failed = result.Items.FirstOrDefault(x => !x.Success);
                StatusMessage = $"✘ 設備初始化失敗：{failed?.DeviceName} - {failed?.Message}";
            }

            System.Diagnostics.Debug.WriteLine(result.GetSummary());
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "⏹ 設備初始化已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✘ 設備初始化發生錯誤：{ex.Message}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsDryRunning = false;
            _cts?.Dispose();
            _cts = null;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// 原點賦歸按鈕：所有軸回原點。
    /// </summary>
    private async void OnHome()
    {
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "執行原點賦歸");
        IsDryRunning = true;
        _cts = new CancellationTokenSource();
        _machine.Alarms.Clear("LightGateX7");
        _machine.Alarms.Clear("RunException");
        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            StatusMessage = "正在執行原點賦歸，請稍候...";
            var result = await Task.Run(
                () => _machine.HomeAllAxesPublicAsync(progress, _cts.Token));

            if (result.AllPassed)
            {
                if (IsLightGateBlocked)
                {
                    // 原點賦歸雖成功，但光閘仍遮斷 → 不允許 RUN
                    StatusMessage = "⚠ 原點賦歸完成，但光閘仍遮斷！請解除光閘後再執行原點賦歸";
                }
                else
                {
                    IsInitialized = true;
                    StatusMessage = "✔ 原點賦歸完成！請放入載具後按 RUN 開始";
                }
            }
            else
            {
                var failed = result.Items.FirstOrDefault(x => !x.Success);
                StatusMessage = $"✘ 原點賦歸失敗：{failed?.DeviceName} - {failed?.Message}";
            }

            System.Diagnostics.Debug.WriteLine(result.GetSummary());
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "⏹ 原點賦歸已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✘ 原點賦歸發生錯誤：{ex.Message}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsDryRunning = false;
            _cts?.Dispose();
            _cts = null;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// 檢測開始前，清空所有 Slot 顯示（Value 歸零、NG 清除、統計重置）
    /// </summary>
    private void ResetAllSlots()
    {
        foreach (var slot in AreaA_Row1.Concat(AreaB_Row1))
        {
            slot.ImageSource = null;
            slot.Value = 0;
            slot.IsNg  = false;
        }

        AreaA_Stats.Calculate(AreaA_Row1);
        AreaB_Stats.Calculate(AreaB_Row1);
    }

    /// <summary>
    /// 填入空白 Slot 佔位格（數值歸零、無 NG）
    /// </summary>
    private static void FillSlots(ObservableCollection<SlotItem> target, int startNumber, int count)
    {
        for (int i = 0; i < count; i++)
        {
            target.Add(new SlotItem
            {
                Name = $"SLOT#{startNumber - i}",
                Value = 0,
                IsNg = false,
            });
        }
    }

    /// <summary>
    /// 切換「暫停光閘」模式。
    /// 啟用：光閘觸發不中斷流程，同時鎖定 RUN 與原點賦歸。
    /// 停用：恢復正常光閘防護。
    /// </summary>
    private void OnToggleLightGateBypass()
    {
        IsLightGateBypassActive = !IsLightGateBypassActive;
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role,
            $"暫停光閘切換為{(IsLightGateBypassActive ? "啟用" : "停用")}");

        if (!IsLightGateBypassActive)
        {
            // 停用 bypass 時：重置防抖計數，讓光閘狀態在下一次 PLC 輪詢時重新評估
            // 避免取料期間的遮斷殘留導致 RUN / 原點賦歸持續鎖定
            _lightGateBlockedCount = 0;
            IsLightGateBlocked = false;
        }

        StatusMessage = IsLightGateBypassActive
            ? "⚠ 暫停光閘已啟用：光閘觸發不中斷流程（RUN 與原點賦歸暫時鎖定）"
            : "✔ 暫停光閘已取消，恢復正常光閘防護";
    }

    /// <summary>
    /// 切換「暫停板子感測」模式（Admin 專用）。
    /// 啟用：完全忽略 X3/X11 雷射感測判斷，IsBoardMisplaced 強制為 false，RUN 不會被卡住。
    /// 停用：恢復正常判斷，下一次 PLC tick 會依當下訊號重新評估。
    /// 啟用/停用都會寫入操作紀錄（AuditLogger），啟用時額外要求二次確認，避免誤按。
    /// </summary>
    private void OnToggleBoardSensorBypass()
    {
        if (!IsBoardSensorBypassActive)
        {
            var confirm = MessageBox.Show(
                "啟用後會完全忽略 X3/X11 雷射感測（板子放置檢查），即使板子未放妥 RUN 也不會被擋下。\n" +
                "僅供排除感測器異常時應急使用，請確認板子實際狀態後再繼續。\n\n是否確定啟用？",
                "暫停板子感測（Admin）",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;
        }

        IsBoardSensorBypassActive = !IsBoardSensorBypassActive;
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role,
            $"暫停板子感測（X3/X11）切換為{(IsBoardSensorBypassActive ? "啟用" : "停用")}");

        if (IsBoardSensorBypassActive)
        {
            // 啟用當下立刻解除卡住的狀態，不用等下一次 tick
            IsBoardMisplaced = false;
            _boardMisplacedDialogShown = false;
        }
        else
        {
            // 停用時重置防抖計數，讓下一次 tick 依當下實際訊號重新判斷，不沿用舊的計數殘留
            _boardMisplacedCount = 0;
            _boardPlacedCount = 0;
        }

        StatusMessage = IsBoardSensorBypassActive
            ? "⚠ 暫停板子感測已啟用：X3/X11 雷射感測判斷已忽略，RUN 不受板子放置狀態影響"
            : "✔ 暫停板子感測已取消，恢復正常雷射感測判斷";
    }

    private async void OnSlcReset()
    {
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "執行 SLC Reset");
        StatusMessage = "⏳ SLC Reset 執行中...";
        try
        {
            await _machine.SlcResetAsync();

            // Reset 成功：清除光閘遮斷狀態，讓 RUN / 原點賦歸恢復可用
            _lightGateBlockedCount = 0;
            IsLightGateBlocked = false;

            StatusMessage = "✔ 光柵Reset OK";
            MessageBox.Show("光柵Reset OK", "SLC Reset", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StatusMessage = $"✘ SLC Reset 失敗：{ex.Message}";
        }
    }

    /// <summary>
    /// WebAPI 連線設定：設定 BaseUrl / EquipmentId 並套用至 CimApiClient。
    /// </summary>
    private async void OnWebApiTest()
    {
        try
        {
            var win = new Views.WebApiTestWindow();
            try { win.Owner = Application.Current.MainWindow; } catch { /* Owner 設定失敗不影響開啟 */ }
            win.LoadFrom(_mesClient);
            bool saved = win.ShowDialog() == true;

            // 儲存後：依 IsEnabled 決定是否重連
            if (saved)
            {
                if (!_mesClient.IsEnabled)
                {
                    // 關閉功能：停止重試計時器，直接更新狀態顯示
                    _mesRetryTimer.Stop();
                    MesStatus = MesConnectionStatus.Disabled;
                    StatusMessage = "MES WebAPI 已關閉";
                }
                else
                {
                    // 開啟功能：重新連線
                    _mesRetryTimer.Stop();
                    await InitMesAsync();
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"WebAPI 設定視窗開啟失敗：{ex.Message}";
        }
    }

    /// <summary>修改自己的密碼（任何角色都可以）。</summary>
    private void OnChangePassword()
    {
        var win = new Views.ChangePasswordWindow(CurrentUser.Username)
        {
            Owner = Application.Current.MainWindow
        };
        if (win.ShowDialog() == true)
        {
            StatusMessage = "✔ 密碼已更新";
            AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "修改密碼");
        }
    }

    /// <summary>開啟帳號管理視窗（僅Admin可執行，由CanExecute保證）。</summary>
    private void OnOpenUserManagement()
    {
        var win = new Views.UserManagementWindow(CurrentUser, this)
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
    }

    /// <summary>開啟操作紀錄(Audit Log)檢視視窗（僅Admin可執行，由CanExecute保證）。</summary>
    private void OnOpenAuditLog()
    {
        var win = new Views.AuditLogViewerWindow
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
    }

    /// <summary>開啟角色權限設定視窗（僅Admin可執行，由CanExecute保證）。</summary>
    private void OnOpenRolePermission()
    {
        var win = new Views.RolePermissionWindow(CurrentUser, this)
        {
            Owner = Application.Current.MainWindow
        };
        win.ShowDialog();
    }

    /// <summary>
    /// 登出：要求重新輸入帳號密碼才能繼續操作，不會結束程式、不影響機台連線/運行狀態。
    /// 實際邏輯與閒置逾時自動登出共用 <see cref="LockAndRequireLogin"/>，只是觸發原因不同。
    /// </summary>
    private void OnLogout() => LockAndRequireLogin("登出");

    /// <summary>
    /// 鎖定畫面並彈出 LoginWindow 要求重新完整登入（不是輸入同一使用者密碼的快速解鎖）。
    /// 給「使用者手動登出」跟「閒置逾時自動登出」共用，避免兩條路徑各自維護一份登出邏輯。
    /// </summary>
    private void LockAndRequireLogin(string auditReason)
    {
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, auditReason);
        IsLocked = true;
        ShowLoginWindow();
    }

    /// <summary>
    /// 彈出 LoginWindow；取消或登入失敗都會停在鎖定畫面，不會裸機無人值守。
    /// 拆成獨立方法是因為：使用者若直接把 LoginWindow 關掉（按X或取消），這個視窗就消失了，
    /// 鎖定畫面上必須要有辦法再叫出一次，否則就會卡在「鎖定中卻沒有任何輸入介面」的死局
    /// ——這個方法同時也是鎖定畫面「重新登入」按鈕（RetryLoginCommand）呼叫的對象。
    /// </summary>
    private void ShowLoginWindow()
    {
        var login = new Views.LoginWindow { Owner = Application.Current.MainWindow };
        if (login.ShowDialog() == true && login.AuthenticatedUser != null)
            CompleteLogin(login.AuthenticatedUser);
    }

    /// <summary>
    /// 登入成功後呼叫：設定目前使用者並解除鎖定。開機首次登入（由 App.xaml.cs 呼叫）、
    /// 登出/閒置逾時後重新登入（LockAndRequireLogin）共用同一套邏輯。
    /// </summary>
    public void CompleteLogin(UserAccount user)
    {
        CurrentUser = user;
        IsLocked = false;
        ResetIdleTimer();
        StatusMessage = $"✔ 已登入：{user.Username}（{user.Role}）";
        StartBackgroundWork();
    }

    /// <summary>
    /// 將 AreaA / AreaB 的 SlotItem 轉換成 WebApidll 所需的 BumperResultsData 列表。
    /// </summary>
    /// <summary>
    /// 模擬模式專用流程：跳過所有機台動作，以假資料驗證 WebAPI 送出是否正確。
    /// </summary>
    private async Task RunSimulationAsync(CancellationToken ct)
    {
        // ── STEP 1：條碼 ──────────────────────────────────────────────────
        StatusMessage = "[SIM] 略過機台移動，直接使用條碼欄位值...";
        await Task.Delay(300, ct);

        string simBarcode = string.IsNullOrWhiteSpace(Barcode) ? "001400037514_00214" : Barcode;
        _confirmedBarcode = simBarcode;
        Barcode = simBarcode;
        // 模擬模式：CarrierSide 預設 "L"（若 UI 未設定）
        if (string.IsNullOrEmpty(CarrierSide)) CarrierSide = "L";
        StatusMessage = $"[SIM] 條碼確認：{simBarcode}，CarrierSide={CarrierSide}";

        // ── MES QueryWorkOrder（非阻塞）──────────────────────────────────
        _ = _mesClient.QueryWorkOrderAsync(_mesClient.EquipmentId, _confirmedBarcode)
            .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                TaskScheduler.FromCurrentSynchronizationContext());

        // ── STEP 2：產生假量測資料（共 50 筆）────────────────────────────
        StatusMessage = "[SIM] 產生假量測資料...";
        await Task.Delay(300, ct);

        var rng = new Random();
        int totalSlots = AreaA_Row1.Count + AreaB_Row1.Count; // 通常 50

        // 前 25 筆 → AreaA_Row1，後 25 筆 → AreaB_Row1
        for (int i = 0; i < totalSlots; i++)
        {
            ct.ThrowIfCancellationRequested();
            double val = Math.Round(9.5 + rng.NextDouble(), 4);   // 9.5 ~ 10.5
            bool ng = !(LowerLimit <= val && val <= UpperLimit);
            string loc = $"FL_{(i + 1):D2}";

            if (i < AreaA_Row1.Count)
            {
                AreaA_Row1[i].Name  = loc;
                AreaA_Row1[i].Value = val;
                AreaA_Row1[i].IsNg  = ng;
            }
            else
            {
                int j = i - AreaA_Row1.Count;
                AreaB_Row1[j].Name  = loc;
                AreaB_Row1[j].Value = val;
                AreaB_Row1[j].IsNg  = ng;
            }
        }

        // ── 統計 & CSV ────────────────────────────────────────────────────
        AreaA_Stats.Calculate(AreaA_Row1);
        AreaB_Stats.Calculate(AreaB_Row1);

        bool pass = AreaA_Stats.Result == "OK" && AreaB_Stats.Result == "OK";
        InspectionResultWriter.WriteSummary(_confirmedBarcode, pass);

        // ── STEP 3：WebAPI UploadResult（非阻塞）─────────────────────────
        StatusMessage = "[SIM] 正在呼叫 WebAPI UploadResult...";
        var simEndTime = DateTime.Now;
        var bumperList = BuildBumperResultsData(AreaA_Row1, AreaB_Row1, _confirmedBarcode,
            CarrierSide, LowerLimit, UpperLimit, _runStartTime, simEndTime);
        // ProductId = 整個條碼；RecipeId = CarrierSide (R/L)
        _ = _mesClient.UploadResultAsync(_mesClient.EquipmentId, _confirmedBarcode, CarrierSide, pass,
                bumperList,
                _runStartTime.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                simEndTime.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                TaskScheduler.FromCurrentSynchronizationContext());

        StatusMessage = $"[SIM] 模擬流程完成  →  {(pass ? "✔ PASS" : "✘ FAIL")}";

        // ── MES 上報 Idle（模擬流程成功完成）──
        _ = _mesClient.ReportIdleAsync()
            .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    /// <summary>切割 Barcode，回傳 (RecipeId, ProductId)。
    /// 格式：RecipeId_ProductId（例：001400037514_00214），
    /// 若無底綫線則整個当 RecipeId，ProductId 為空字串。</summary>
    private static (string RecipeId, string ProductId) SplitBarcode(string barcode)
    {
        int idx = barcode.LastIndexOf('_');
        if (idx > 0 && idx < barcode.Length - 1)
            return (barcode[..idx], barcode[(idx + 1)..]);
        return (barcode, "");
    }

    /// <summary>
    /// 將 AreaA / AreaB 的 SlotItem 轉換為 BumperResultsData 列表。
    /// ItemName 格式：SLOT{N:D2}_{camera}（例：SLOT25_C1），相機名由 carrierSide 決定。
    /// carrierSide="R" → AreaA=C4, AreaB=C3；carrierSide="L" → AreaA=C2, AreaB=C1。
    /// </summary>
    private static List<BumperResultsData> BuildBumperResultsData(
        IEnumerable<Models.SlotItem> areaA,
        IEnumerable<Models.SlotItem> areaB,
        string barcode,
        string carrierSide,
        double lowerLimit  = 0,
        double upperLimit  = 0,
        DateTime? startTime = null,
        DateTime? endTime   = null)
    {
        var list = new List<BumperResultsData>();
        var start = startTime ?? DateTime.Now;
        var end   = endTime   ?? DateTime.Now;
        string startStr  = start.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string endStr    = end.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string duringStr = (end - start).TotalSeconds.ToString("F3");

        // carrierSide="R"(X12): AreaA=C4, AreaB=C3
        // carrierSide="L"(X13): AreaA=C2, AreaB=C1
        bool isRight = string.Equals(carrierSide, "R", StringComparison.OrdinalIgnoreCase);
        string cameraA = isRight ? "C4" : "C2";
        string cameraB = isRight ? "C3" : "C1";

        int slotNo = 1;
        foreach (var slot in areaA)
        {
            list.Add(new BumperResultsData
            {
                Barcode          = barcode,
                BumperPosition   = slot.Name,
                Layer            = slot.IsNg ? "NG" : "OK",
                ItemName         = $"SLOT{slotNo:D2}_{cameraA}",
                ItemValue        = slot.Value.ToString("F4"),
                UpperSpecLimit   = upperLimit.ToString("F4"),
                LowerSpecLimit   = lowerLimit.ToString("F4"),
                ItemJudge        = slot.IsNg ? "Fail" : "Pass",
                ProcessStartTime = startStr,
                ProcessEndTime   = endStr,
                DuringTime       = duringStr
            });
            slotNo++;
        }

        slotNo = 1;
        foreach (var slot in areaB)
        {
            list.Add(new BumperResultsData
            {
                Barcode          = barcode,
                BumperPosition   = slot.Name,
                Layer            = slot.IsNg ? "NG" : "OK",
                ItemName         = $"SLOT{slotNo:D2}_{cameraB}",
                ItemValue        = slot.Value.ToString("F4"),
                UpperSpecLimit   = upperLimit.ToString("F4"),
                LowerSpecLimit   = lowerLimit.ToString("F4"),
                ItemJudge        = slot.IsNg ? "Fail" : "Pass",
                ProcessStartTime = startStr,
                ProcessEndTime   = endStr,
                DuringTime       = duringStr
            });
            slotNo++;
        }

        return list;
    }
}
