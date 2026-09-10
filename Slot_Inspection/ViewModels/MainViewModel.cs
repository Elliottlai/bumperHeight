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
    private DateTime? _lastQcRunAt; // 最後一次跑完檢測的時間，供獨立「QC 比對」視窗顯示
    private Views.QcInspectionWindow? _qcWindow; // 獨立 QC 比對儀表板，單一實例；關窗時歸 null
    private string _currentDate = DateTime.Now.ToString("yyyy/MM/dd");
    private string _currentTime = DateTime.Now.ToString("HH:mm:ss");
    private bool _isRunning;
    private string _statusMessage = "系統啟動中，請稍候...";
    private bool _isInitialized;
    /// <summary>
    /// 只要 IsInitialized 為 false（不論是 STOP、光閘觸發、板子誤放、流程例外，或任何原因），
    /// 就必須重新原點賦歸（或重新 S00 初始化）才能再按 RUN／移至取料位，
    /// 避免 IsZAtSafeHeight 的「Z 軸已在安全高度可略過賦歸」旁路蓋掉安全防呆（曾因此撞機）。
    /// 由 IsInitialized 的 setter 統一維護，不需個別呼叫端手動設定。
    /// </summary>
    private bool _requireReinitAfterStop;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _plcTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    // EMO 專用快速輪詢：用獨立的 background Thread（不是 DispatcherTimer，也刻意不是
    // System.Threading.Timer／ThreadPool）。原因是實測踩過兩層坑：
    //   1) 掛 DispatcherTimer 會被 UI 執行緒忙碌拖慢（見 OnEmoTickCore 上的說明）。
    //   2) 改成 ThreadPool 版 Timer 後，8/25 現場又遇到「EMO 觸發時軸體 Modbus 逾時重試
    //      量太大，把 ThreadPool 工作執行緒全部佔滿」，導致連 EMO 輪詢自己的 callback 都
    //      排不到執行緒可以跑，整個輪詢直接停擺數十秒（log 完全沒有新的 [EMO-DIAG]），
    //      使用者實際扭轉解除 EMO 後，畫面警示橫幅完全不會消失，因為程式根本沒有在跑。
    // 獨立 Thread 有自己專屬的作業系統執行緒，不會被 ThreadPool 裡其他工作（例如軸體
    // StopAllAxesImmediate 的 Modbus 重試）排擠，確保 EMO 輪詢永遠有機會照原訂間隔執行。
    private Thread? _emoWatchThread;
    private volatile bool _emoWatchRunning;
    private static readonly TimeSpan EmoPollInterval = TimeSpan.FromMilliseconds(100);
    // 光閘（X7）跟 EMO 一樣是保護人身安全的訊號，原本掛在共用的 UI 執行緒 _plcTimer 上，
    // 跟 X3/X11 板子感測、紅燈同步、R/L 即時顯示等一大堆其他功能搶同一個計時器——EMO 已經
    // 實測證實過這種共用計時器在 UI 執行緒忙碌/卡頓時會不可靠，光閘沒理由繼續冒同樣的風險，
    // 所以這裡直接複製 EMO 那一套：獨立 Thread（非 ThreadPool），道理同上。
    private Thread? _lightGateWatchThread;
    private volatile bool _lightGateWatchRunning;
    private static readonly TimeSpan LightGatePollInterval = TimeSpan.FromMilliseconds(100);
    // MES 重試計時器：失敗時每 15 秒自動重試，連線成功後停止
    private readonly DispatcherTimer _mesRetryTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly System.Diagnostics.Stopwatch _x1x2Stopwatch = new();
    private static readonly TimeSpan DryRunTriggerDuration = TimeSpan.FromMilliseconds(500);
    private DateTime _runStartTime;  // 主流程開始時間（用於 MES 上傳 ProcessStartTime）
    private bool _prevX10 = false; // X10 上一次狀態（預設 0，保留供未來使用）
    private bool _isLightGateBlocked; // X7=LOW：光閘遮斷中（UI 綁定用，只能透過 IsLightGateBlocked 的 setter 寫入）
    // 背景執行緒（LightGateWatchLoop）自己的、同步更新的遮斷狀態，專供防抖分支判斷用，
    // 不可以改用 _isLightGateBlocked：那個欄位只會在 ApplyLightGateState 裡透過 BeginInvoke
    // 非同步寫入，UI 執行緒若忙碌會延遲套用，背景執行緒下一輪輪詢讀到的就會是舊值，
    // 導致「明明已經又進入新的遮斷狀態，卻因為還讀到舊的『尚未解除』而判斷錯分支、
    // 誤把新的觸發吃掉」——這個 bug 已經實測發生過一次（G 鍵按住 501ms 卻沒觸發）。
    private bool _lightGateBlockedConfirmed;
    private bool? _lastLoggedX7Raw; // 診斷用：X7 原始讀值一有變化就記 log，不用等防抖確認觸發
    // 觸發/解除防抖改用牆鐘時間，不用「連續 tick 次數」——比照 EMO 已經踩過的雷：DispatcherTimer
    // 在機台忙碌時 tick 間隔常被拉長到數秒，用「連續 N 次 tick」防抖在這種情況下不可靠。
    // 解除跟觸發用同樣的短門檻（接近瞬間）：光閘解除後，真正能不能繼續動作還是要操作員自己
    // 按「原點賦歸」再確認一次，這道人工確認本身就是最後防線，解除防抖只需要濾掉單次雜訊，
    // 不需要讓操作員多等好幾秒站在機台前面乾等。
    private DateTime? _lightGateLowSince;            // 目前這段連續 LOW 從何時開始（未觸發狀態下用）
    private DateTime? _lightGateHighSinceAfterBlock; // 已觸發後，目前這段連續 HIGH 從何時開始（觸發狀態下用）
    private static readonly TimeSpan LightGateTriggerDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan LightGateClearDuration   = TimeSpan.FromMilliseconds(300);
    // X0 緊急停止(EMO)
    private bool _isEmoTriggered; // X0=HIGH：EMO 觸發中（現場實測極性跟電路圖判讀相反，已更正，見 OnEmoTick）
    // 觸發防抖改用「牆鐘時間」而非「連續 tick 次數」：現場實測發現 _emoTimer 雖然設定 100ms，
    // 實際 tick 間隔在機台上常常拉長到數秒（懷疑 UI Dispatcher 執行緒被其他同步呼叫卡住），
    // 用「連續 N 次 tick」的防抖在這種情況下幾乎永遠湊不滿，導致按下 EMO 也不會觸發。
    // 改成「從第一次讀到觸發電位算起，經過 EmoDebounceDuration 仍持續為觸發」，不管中間 tick
    // 實際間隔多長、多不規則，只要訊號真的有持續住，遲早會滿足門檻——同時仍能過濾單次雜訊尖峰
    // （雜訊通常下一個 tick 就掉回去，_emoHighSince 會被清成 null，不會累積時間）。
    private DateTime? _emoHighSince;
    private static readonly TimeSpan EmoDebounceDuration = TimeSpan.FromMilliseconds(300);
    private bool _isEmoAlarmPendingReset; // EMO 觸發過、尚未經使用者按「EMO Reset」確認（即使 X0 已釋放仍維持 true）
    // EMO 診斷 log 節流狀態：避免 tick 洗版，只在「數值有變化」或超過心跳間隔才寫一筆，
    // 但保證固定心跳間隔一定有一筆，讓看 log 的人能確認 tick 真的還在跑、不是卡住或被例外中斷。
    // 現場測試時把 logs\ 下當天的 .log 檔帶回來，搜尋 "[EMO-DIAG]" 就能看到完整時間序列，
    // 兩筆之間的實際時間差就是 _emoTimer 真實的 tick 間隔（拿來確認 timer 有沒有卡住）。
    private (bool? RawX0, bool Simulation, bool Triggered)? _emoDiagLastLogged;
    private DateTime _emoDiagLastLogTime = DateTime.MinValue;
    private static readonly TimeSpan EmoDiagHeartbeatInterval = TimeSpan.FromSeconds(2);
    // X3/X11 板子放置雷射感測
    private bool _isBoardMisplaced;
    private int _boardMisplacedCount;
    private int _boardPlacedCount;
    // 提示視窗目前是否開著：只在 MessageBox 顯示期間為 true，用來防止巢狀訊息泵期間 tick 持續疊出視窗。
    // 人員按「確定」關掉視窗後會變回 false，下一次 tick 若板子仍未放妥就會立刻重新跳出提示，直到板子放好為止。
    private bool _boardMisplacedDialogOpen;
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

    // X7 訊號歷史：每次背景輪詢都記錄，最多保留 30 筆（改用獨立 Thread 100ms 輪詢後，
    // 30 筆約涵蓋 3 秒，跟改用獨立 Thread 之前 10 筆×500ms 的涵蓋時間差不多），觸發遮斷時一起寫 log
    private readonly Queue<(DateTime Time, bool X7Raw, bool RawBlocked)> _x7SignalHistory = new();
    private const int X7HistorySize = 30;
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

    /// <summary>目前套用中的 Recipe 名稱，供首頁唯讀顯示；實際切換由「Recipe 切換」視窗負責並即時寫回 InspectionConfig。</summary>
    public string CurrentRecipeDisplay =>
        string.IsNullOrWhiteSpace(_machine.Config.CurrentRecipeName)
            ? "未指定"
            : _machine.Config.CurrentRecipeName;

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
        MachineRunState.Paused  => "上料模式中",
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

    /// <summary>
    /// 畫面狀態列文字。集中在這裡寫 log 而不是在 101 個賦值處各自補一行——
    /// 現場測試沒有網路，只能事後帶 log 檔回來分析，操作員當下畫面看到的訊息若沒進 log，
    /// 就只能靠人工描述回報還原，等於每次都要重寫一次「0820客服描述問題.txt」。
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetProperty(ref _statusMessage, value))
                _logger.Info($"[Status] {value}");
        }
    }

    public bool IsInitialized
    {
        get => _isInitialized;
        set
        {
            SetProperty(ref _isInitialized, value);
            // 只要未完成初始化，一律要求重新原點賦歸才能按 RUN／移至取料位，
            // 避免 IsZAtSafeHeight 的「Z 軸已在安全高度可略過賦歸」旁路蓋掉安全防呆（曾因此撞機）。
            _requireReinitAfterStop = !value;
        }
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
    /// X0 緊急停止(EMO)觸發中（HIGH，見 OnEmoTick）→ 禁止按 RUN／原點賦歸。
    /// </summary>
    public bool IsEmoTriggered
    {
        get => _isEmoTriggered;
        private set
        {
            if (SetProperty(ref _isEmoTriggered, value))
            {
                CommandManager.InvalidateRequerySuggested();
                NotifyRunStateChanged();
            }
        }
    }

    /// <summary>
    /// EMO 曾觸發、尚未經使用者按「EMO Reset」確認 → true。跟 <see cref="IsEmoTriggered"/> 不同：
    /// 後者跟著 X0 即時訊號走（放開就變 false，用來即時鎖定/解鎖 RUN、原點賦歸等按鈕），
    /// 這個旗標則要求操作員實際確認過現場安全、按下 Reset，才會變回 false，異常燈號也才會熄滅。
    /// </summary>
    public bool IsEmoAlarmPendingReset
    {
        get => _isEmoAlarmPendingReset;
        private set
        {
            if (SetProperty(ref _isEmoAlarmPendingReset, value))
                CommandManager.InvalidateRequerySuggested();
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
    public ICommand QcInspectionCommand { get; }
    public ICommand OpenImageViewerCommand { get; }
    public ICommand OpenResultFolderCommand { get; }
    public ICommand ConfirmBarcodeCommand { get; }
    public ICommand ToggleLightGateBypassCommand { get; }
    public ICommand ToggleBoardSensorBypassCommand { get; }
    public ICommand SlcResetCommand { get; }
    public ICommand EmoResetCommand { get; }
    public ICommand WebApiTestCommand { get; }
    public ICommand ChangePasswordCommand { get; }
    public ICommand OpenUserManagementCommand { get; }
    public ICommand OpenAuditLogCommand { get; }
    public ICommand OpenRolePermissionCommand { get; }
    public ICommand OpenMaterialRecipeMapCommand { get; }
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

    /// <summary>
    /// 按鈕顯示文字，隨狀態切換；啟用時（光閘關閉，光閘觸發不中斷流程）顯示「上料下料模式ON」，
    /// 停用時（光閘開啟，恢復正常防護）顯示「上料下料模式OFF」。同一功能用 ON/OFF 表示開關兩態，避免被誤認成兩個不同功能。
    /// </summary>
    public string LightGateBypassButtonText =>
        IsLightGateBypassActive ? "上料下料模式ON" : "上料下料模式OFF";

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

    /// <summary>
    /// 模擬模式專用：按 E 鍵切換的 EMO(X0) 觸發狀態，供測試「按下 EMO 後畫面/流程行為」用，
    /// 不需要接真實 PLC。僅在 IsSimulationMode=true 時生效，OnPlcTick 讀此值直接跳過防抖。
    /// </summary>
    private bool _simEmoTriggered;

    /// <summary>由 MainWindow 鍵盤事件呼叫，切換模擬 EMO 觸發/復歸。非模擬模式下無效。</summary>
    public void ToggleSimEmo()
    {
        if (!IsSimulationMode) return;
        _simEmoTriggered = !_simEmoTriggered;
        _logger.Debug($"[SIM] SimEmoTriggered 切換為 {_simEmoTriggered}");
    }

    /// <summary>
    /// 模擬模式專用：按 G 鍵切換的 X7 光閘遮斷狀態，供測試「光閘觸發時軸立即煞停」用，
    /// 不需要接真實 PLC。僅在 IsSimulationMode=true 時生效，OnPlcTick 讀此值直接跳過防抖。
    /// </summary>
    private bool _simLightGateBlocked;

    /// <summary>由 MainWindow 鍵盤事件呼叫，切換模擬 X7 光閘遮斷/解除。非模擬模式下無效。</summary>
    public void ToggleSimLightGate()
    {
        if (!IsSimulationMode) return;
        _simLightGateBlocked = !_simLightGateBlocked;
        _logger.Debug($"[SIM] SimLightGateBlocked 切換為 {_simLightGateBlocked}");
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
        RunCommand = new RelayCommand(OnRunInspection, () => IsDeviceReady && (IsInitialized || (IsZAtSafeHeight && !_requireReinitAfterStop)) && !IsRunning && !IsDryRunning && !IsLightGateBlocked && !IsEmoTriggered && !IsEmoAlarmPendingReset && !IsBoardMisplaced && HasPermission(Permission.Run));
        // 初始化：Config + Servo + PLC（不含原點賦歸）
        InitAxesCommand = new RelayCommand(OnInitDevices, () => !IsRunning && !IsDryRunning && !IsDeviceReady && HasPermission(Permission.InitDevices));
        // 原點賦歸：光閘必須未遮斷才可按（不因暫停光閘而全域禁用）
        HomeCommand = new RelayCommand(OnHome, () => IsDeviceReady && !IsRunning && !IsDryRunning && !IsLightGateBlocked && !IsEmoTriggered && !IsEmoAlarmPendingReset && HasPermission(Permission.Home));
        // 讀取條碼：Y→491 → 偵測 X12/X13 → 移動 X 軸
        ReadBarcodeCommand = new RelayCommand(OnReadBarcode, () => IsDeviceReady && !IsRunning && !IsDryRunning && HasPermission(Permission.ReadBarcode));
        // 移至取料位：Y 軸移動到取料位座標（與 RUN 一致：需已復歸或 Z 已在安全高度且未被要求重新復歸）
        MoveToPickPositionCommand = new RelayCommand(OnMoveToPickPosition, () => IsDeviceReady && (IsInitialized || (IsZAtSafeHeight && !_requireReinitAfterStop)) && !IsRunning && !IsDryRunning && !IsEmoAlarmPendingReset && HasPermission(Permission.MoveToPickPosition));
        // 座標設定：依「角色權限設定」決定 Operator/Engineer 是否可開啟，Admin永遠可以
        PositionSettingsCommand = new RelayCommand(OnPositionSettings, () => !IsRunning && !IsDryRunning &&
            (HasPermission(Permission.PositionSettings) || HasPermission(Permission.RecipeSwitch)));

        // 裁切設定：依「角色權限設定」決定
        CropSettingsCommand = new RelayCommand(OnCropSettings, () => HasPermission(Permission.CropSettings));

        // 亮度設定：依「角色權限設定」決定
        LightSettingsCommand = new RelayCommand(OnLightSettings, () => HasPermission(Permission.LightSettings));

        // QC 比對：獨立儀表板。開視窗 / 查看比對結果 / 重新比對 不鎖（任何登入者都可以看）；
        // 視窗內「用目前這片存為基準」與「允許範圍」欄位/儲存 僅 Engineer 以上可用，
        // 見 OnOpenQcInspection 傳進去的 canEditQc。
        QcInspectionCommand = new RelayCommand(OnOpenQcInspection);

        // 點擊圖像放大預覽
        OpenImageViewerCommand = new RelayCommand<SlotItem>(OnOpenImageViewer);

        // 開啟 CSV 報表資料夾
        OpenResultFolderCommand = new RelayCommand(OnOpenResultFolder, () => HasPermission(Permission.OpenResultFolder));

        // 條碼 TextBox Enter 確認
        ConfirmBarcodeCommand = new RelayCommand(OnConfirmBarcode);

        // 暫停光閘 toggle
        ToggleLightGateBypassCommand = new RelayCommand(OnToggleLightGateBypass, () => !IsRunning && !IsDryRunning && HasPermission(Permission.ToggleLightGateBypass));

        // 暫停板子感測 toggle：僅 Admin 可操作，不透過角色權限表開放
        ToggleBoardSensorBypassCommand = new RelayCommand(OnToggleBoardSensorBypass, () => IsAdmin);

        // SLC Reset
        SlcResetCommand = new RelayCommand(OnSlcReset, () => IsDeviceReady && !IsRunning && !IsDryRunning && HasPermission(Permission.SlcReset));

        // EMO Reset：EMO 曾觸發過就必須手動按這顆才能清除異常燈號/訊息，即使 X0 已釋放也一樣
        EmoResetCommand = new RelayCommand(OnEmoReset, () => IsEmoAlarmPendingReset && !IsEmoTriggered && HasPermission(Permission.SlcReset));

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

        // 料號設定表：僅Admin
        OpenMaterialRecipeMapCommand = new RelayCommand(OnOpenMaterialRecipeMap, () => IsAdmin);

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
        // EMO 背景輪詢不用 Tick 事件訂閱，見 StartBackgroundWork() 裡 _emoBgTimer 的建立方式。

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
        // 獨立 Thread（非 ThreadPool），見上面 _emoWatchThread 欄位的說明。
        _emoWatchRunning = true;
        _emoWatchThread = new Thread(EmoWatchLoop)
        {
            IsBackground = true,
            Name = "EmoWatch",
            Priority = ThreadPriority.AboveNormal,
        };
        _emoWatchThread.Start();

        // 光閘（X7）獨立 Thread，見上面 _lightGateWatchThread 欄位的說明。
        _lightGateWatchRunning = true;
        _lightGateWatchThread = new Thread(LightGateWatchLoop)
        {
            IsBackground = true,
            Name = "LightGateWatch",
            Priority = ThreadPriority.AboveNormal,
        };
        _lightGateWatchThread.Start();

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
    /// 若目前 EMO 正觸發中，把捕捉到的例外（通常是軸體 Modbus 通訊逾時等技術性錯誤）換成
    /// 操作員看得懂的具體原因，避免顯示「已重試 N 次仍失敗」這類跟 EMO 無關的技術訊息。
    /// 沒有 EMO 觸發時，原樣顯示 fallback（既有訊息不變）。
    /// </summary>
    private string DescribeOperatorFacingError(string fallback) =>
        IsEmoTriggered ? "偵測到緊急停止(EMO)已觸發，機台動作已中斷" : fallback;

    /// <summary>
    /// 依當下 EMO 狀態（不論來自真實 X0 或模擬模式）套用邊緣觸發的副作用（鎖定 RUN、告警等 UI 狀態），
    /// 並更新 IsEmoTriggered。只由 <see cref="OnEmoTickCore"/> 透過 Dispatcher.Invoke 呼叫。
    /// 注意：取消流程（_cts.Cancel()）與強制煞停（StopAllAxesImmediate）**不**在這裡做——
    /// 那兩個呼叫已經在 OnEmoTickCore 偵測到剛觸發的當下、在背景執行緒直接執行過一次了，
    /// 這裡如果重複呼叫，會對同一批軸體同時送出兩輪 Stop 指令，實測會讓 RS485 Modbus
    /// 逾時/重試互相搶通訊埠，反而讓整個停止流程拖更久、连带拖慢這裡 Dispatcher.Invoke 排隊
    /// （8/25 現場測試看到「UI 延遲很久才顯示 EMO」，log 顯示 4 軸的 MotStop 逾時重試訊息
    /// 反覆出現超過 20 秒，就是這裡曾經重複呼叫造成的）。
    /// </summary>
    private void ProcessEmoState(bool emoNow)
    {
        if (emoNow && !_isEmoTriggered)
        {
            IsInitialized = false; // setter 會自動鎖上 _requireReinitAfterStop
            // EMO 觸發後強制要求重新初始化（不只是原點復歸）：EMO 發生當下軸體 RS485 連線
            // 常常已經不穩定（MotStop 逾時、收不到回應），只做原點復歸不會重新建立連線，
            // 必須先走一次 S00 初始化。IsDeviceReady=false 會鎖住 HomeCommand、解鎖 InitAxesCommand。
            IsDeviceReady = false;
            // EMO 是硬停止：直接把「執行中」旗標清掉，不依賴當下流程方法的 finally。
            // 已知情況：EMO 剛好在原點賦歸的 Y 軸階段按下時，賦歸輪詢與強制煞停會在同一個
            // COM 埠上撞在一起、卡進不理會 timeout / CancellationToken 的 Modbus 讀取，
            // 導致 OnHome 的 finally 永遠不執行、IsDryRunning 卡在 true，
            // EMO Reset 後初始化 / 原點賦歸 / RUN 三顆按鈕全部解不開，只能重開程式。
            // 這裡直接清旗標，讓按鈕一定解得開。（卡住的舊工作等下次初始化 Connect() 關掉 COM 埠時會自行拋例外收掉。）
            IsRunning = false;
            IsDryRunning = false;
            IsEmoAlarmPendingReset = true;
            StatusMessage = "⚠ 緊急停止(EMO)已觸發！機台動作已中斷，請確認機台安全後釋放 EMO，再重新執行初始化與原點賦歸";
            _machine.Alarms.Raise("EMO", "X0 緊急停止觸發，流程已取消，等待操作員復歸與原點賦歸");
            // 診斷用：曾懷疑「EMO 觸發後 Reset 完卻按不動初始化」是因為觸發當下流程正在跑，
            // 取消後 IsRunning/IsDryRunning 沒有正常被 finally 區塊重設回 false 卡住——這裡先記錄
            // 觸發當下的狀態，之後在 OnEmoReset 再記一次，兩相對照就能看出是不是這裡卡住。
            _logger.Info($"[EMO] 觸發當下流程狀態：IsRunning={IsRunning} IsDryRunning={IsDryRunning}");
            _ = _mesClient.ReportAlarmAsync()
                .ContinueWith(_ => MesStatus = _mesClient.LastStatus, TaskScheduler.FromCurrentSynchronizationContext());
            CancelEventLogger.Write("EMO_X0", "緊急停止(EMO)觸發，流程已取消，等待原點賦歸");
        }
        else if (!emoNow && _isEmoTriggered)
        {
            // 注意：這裡刻意不清除 "EMO" 警報、不熄異常燈號——EMO 是最高等級的安全訊號，
            // 光是 X0 訊號恢復正常（旋鈕被放開）不代表操作員已經確認過現場安全，
            // 必須等操作員按下「EMO Reset」（見 OnEmoReset）才真正清除，見 IsEmoAlarmPendingReset。
            StatusMessage = "✔ EMO 已放開，請按下「EMO Reset」確認現場安全後，再重新執行初始化與原點賦歸";
        }
        IsEmoTriggered = emoNow;
    }

    /// <summary>
    /// <see cref="_emoWatchThread"/> 的主迴圈。跑在專屬的 background Thread，不是 UI 執行緒、
    /// 也不是 ThreadPool——即使 ThreadPool 被其他工作（例如軸體 Modbus 重試）全部佔滿，
    /// 這裡仍然保證能照 <see cref="EmoPollInterval"/> 繼續執行。
    /// </summary>
    private void EmoWatchLoop()
    {
        while (_emoWatchRunning)
        {
            try
            {
                OnEmoTickCore();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[EMO-DIAG] 背景輪詢例外");
            }

            Thread.Sleep(EmoPollInterval);
        }
    }

    /// <summary>
    /// EMO(X0) 專用快速輪詢邏輯，實際跑在 <see cref="_emoWatchThread"/> 這個獨立 background Thread，
    /// 不是 UI 執行緒、也不是 ThreadPool——避免 EMO 這種最高等級的安全訊號被 UI 執行緒忙碌/卡頓
    /// 拖累，也不受 ThreadPool 被其他工作佔滿影響（見 <see cref="_emoWatchThread"/> 上的說明）。
    /// 讀 X0＋累計「牆鐘時間」防抖（見 <see cref="_emoHighSince"/> 上的說明）都在這裡完成，
    /// 完全不受 UI 執行緒影響；只有最後真的要更新 UI 綁定屬性時才切回 UI 執行緒（Dispatcher.Invoke）。
    /// </summary>
    private void OnEmoTickCore()
    {
        // X0 緊急停止(EMO)狀態：HIGH = 觸發（現場已用 PLC 本體 X0 指示燈實測確認）。
        // PLC 斷線(null)時凍結現有狀態，不誤判為「未觸發」
        // （跟 X7 目前的 ?? true 寫法不同——EMO 是最關鍵的安全訊號，斷線時絕不能悄悄當成正常）。
        bool simulation = IsSimulationMode;
        bool? rawX0 = null;
        bool triggered;

        if (simulation)
        {
            // 模擬模式：依 _simEmoTriggered（按 E 鍵切換）決定，跳過防抖立即生效，
            // 且不讀真實 PLC（模擬模式下 PLC 多半未連線，GetPlcXOctal 只會回傳 null）。
            // 注意：模擬模式下這裡完全不會去讀 X0，實機測試前務必確認 IsSimulationMode 是 false，
            // 否則按下實體 EMO 鈕不會有任何反應——這是最常見的「UI 不顯示觸發」原因之一。
            _emoHighSince = null;
            triggered = _simEmoTriggered;
        }
        else
        {
            rawX0 = _machine.GetPlcXOctal(0);
            if (rawX0.HasValue)
            {
                bool rawEmo = rawX0.Value;
                if (rawEmo)
                    _emoHighSince ??= DateTime.Now; // 第一次讀到觸發電位才記錄起始時間，之後持續為 true 不覆蓋
                else
                    _emoHighSince = null; // 只要有一次讀到未觸發，視為雜訊或真的放開，計時歸零重來

                triggered = _emoHighSince.HasValue
                    && (DateTime.Now - _emoHighSince.Value) >= EmoDebounceDuration;
            }
            else
            {
                // PLC 斷線，凍結現有狀態，不當成觸發也不當成解除。
                triggered = _isEmoTriggered;
            }
        }

        // 剛從「未觸發」變成「觸發」的當下，立即（在背景執行緒，不等 UI 執行緒排隊）取消流程、
        // 強制煞停軸體——這兩個呼叫本身跟 UI 無關，不需要切回 UI 執行緒，機台停下來的動作
        // 不能因為 UI 執行緒卡住而跟著延遲。ProcessEmoState 裡也有同一段呼叫（供其他呼叫路徑
        // 共用邏輯），Cancel() 是幂等操作、StopAllAxesImmediate 內部有自己的序列化保護，
        // 這裡先做一次不會造成安全疑慮，只是確保「卡 UI 執行緒不會拖慢煞停」。
        if (triggered && !_isEmoTriggered)
        {
            _cts?.Cancel();
            _ = Task.Run(() => _machine.StopAllAxesImmediate("EMO 緊急停止觸發"));
        }

        // 其餘 UI 綁定狀態（IsEmoTriggered、StatusMessage、CommandManager…）與 MES 回報
        // （TaskScheduler.FromCurrentSynchronizationContext() 需要 UI 執行緒的 SynchronizationContext）
        // 一定要切回 UI 執行緒才能正確更新，但改用 BeginInvoke（非阻塞）而不是 Invoke。
        // 8/26 現場實測發現用 Invoke 會有更嚴重的問題：如果 UI 執行緒因為其他原因（跟這次
        // EMO 無關的某個呼叫）整個卡住，Invoke 會讓「這條專屬背景執行緒」也跟著永久卡住，
        // 導致連 EmoWatchLoop 自己都無法再往下跑、完全停止輪詢（log 連一筆心跳都不會有，
        // 比先前任何一次卡死都更早、更徹底）——等於獨立執行緒的保護完全失效。
        // 改成 BeginInvoke 後，這裡只是把「套用畫面狀態」這件事丟進 UI 執行緒的佇列就馬上
        // return，EmoWatchLoop 永遠不會被 UI 執行緒卡住，能持續輪詢、持續留下 log 心跳；
        // 畫面套用本身如果因為 UI 執行緒卡住而延遲，最多就是画面慢一點更新，不會回頭拖累偵測。
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        var capturedRawX0 = rawX0;
        dispatcher.BeginInvoke(() =>
        {
            _machine.SyncRedAlarmOutput();
            LogEmoDiag(capturedRawX0, simulation, triggered);
            ProcessEmoState(triggered);
        });
    }

    /// <summary>
    /// EMO 診斷 log：只在數值有變化，或超過 <see cref="EmoDiagHeartbeatInterval"/> 沒寫過時才輸出，
    /// 避免 tick 洗版。目的是讓現場測試後把 logs\ 下的檔案帶回來，不需要重現問題也能從時間序列判斷根因：
    ///   - 全程 raw=(sim) → 現場忘記把「模擬模式」關掉，沒有真的在讀 PLC。
    ///   - 全程 raw=PLC斷線(null) → PLC 通訊沒接上，不是 EMO 邏輯本身的問題。
    ///   - raw 全程都是 LOW，按下實體 EMO 也沒變化 → 電路配線 X0 對應的可能不是 EMO 迴路本身
    ///     （已排除：8/25 現場用 PLC 本體 X0 指示燈確認過，按下去燈會亮）。
    ///   - raw 有跳到 HIGH，但兩筆不同狀態的 log 之間時間差遠大於 100ms（例如數秒）→
    ///     _emoTimer 實際 tick 間隔被拉長，不是防抖邏輯本身的問題，要往「UI 執行緒被什麼卡住」查。
    /// </summary>
    private void LogEmoDiag(bool? rawX0, bool simulation, bool triggered)
    {
        var state = (rawX0, simulation, triggered);
        bool changed = _emoDiagLastLogged is null || _emoDiagLastLogged.Value != state;
        bool heartbeatDue = DateTime.Now - _emoDiagLastLogTime >= EmoDiagHeartbeatInterval;
        if (!changed && !heartbeatDue) return;

        _emoDiagLastLogged = state;
        _emoDiagLastLogTime = DateTime.Now;

        string rawText = simulation
            ? $"(sim){(triggered ? "ON" : "OFF")}"
            : rawX0.HasValue ? (rawX0.Value ? "HIGH" : "LOW") : "PLC斷線(null)";
        string highSinceText = _emoHighSince.HasValue
            ? $"{(DateTime.Now - _emoHighSince.Value).TotalMilliseconds:F0}ms"
            : "-";

        _logger.Debug($"[EMO-DIAG] raw={rawText} sim={simulation} highFor={highSinceText}/{EmoDebounceDuration.TotalMilliseconds:F0}ms " +
            $"IsEmoTriggered={triggered} IsEmoAlarmPendingReset={_isEmoAlarmPendingReset}");
    }

    /// <summary>
    /// <see cref="_lightGateWatchThread"/> 的主迴圈，架構跟 <see cref="EmoWatchLoop"/> 完全對稱。
    /// </summary>
    private void LightGateWatchLoop()
    {
        while (_lightGateWatchRunning)
        {
            try
            {
                OnLightGateTickCore();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "[LightGate] 背景輪詢例外");
            }

            Thread.Sleep(LightGatePollInterval);
        }
    }

    /// <summary>
    /// 光閘（X7）專用快速輪詢，實際跑在 <see cref="_lightGateWatchThread"/> 這個獨立 background Thread，
    /// 理由跟 <see cref="OnEmoTickCore"/> 完全一樣：不受 UI 執行緒忙碌、也不受 ThreadPool 被其他工作
    /// （例如軸體 Modbus 重試）佔滿影響。讀 X7＋累計牆鐘時間防抖都在這裡完成；
    /// 只有真的發生「觸發」或「解除」狀態轉換時，才需要切回 UI 執行緒套用畫面/警報狀態。
    /// </summary>
    private void OnLightGateTickCore()
    {
        // 不論任何模式都追蹤，LOW = 遮斷。x7ReadingValid=false 代表這次是 PLC 斷線，或連線
        // 沒斷但讀取已經停止更新（IsXDataFresh=false，GetPlcXOctal 皆回傳 null），此時凍結
        // 現有狀態、不更新任何計時、不觸發任何轉換——絕不能把「讀不到最新狀態」當成
        // 「光閘暢通」而誤解除警報。
        bool x7 = true;
        bool rawBlocked = false;
        bool x7ReadingValid;
        if (IsSimulationMode)
        {
            // 模擬模式：依 _simLightGateBlocked（按 G 鍵切換）決定，刻意不跳過防抖，直接走
            // 跟真實 PLC 一模一樣的判斷邏輯，方便沒有真實硬體時也能驗證防抖時間反應。
            rawBlocked = _simLightGateBlocked;
            x7 = !rawBlocked;
            x7ReadingValid = true;
        }
        else
        {
            bool? rawX7 = _machine.GetPlcXOctal(7);
            if (rawX7 == null)
            {
                x7ReadingValid = false;
            }
            else
            {
                x7 = rawX7.Value;
                rawBlocked = !x7;
                x7ReadingValid = true;
            }
        }

        // 診斷用：原始讀值（或斷線狀態）一有變化就記一行，不用等防抖確認觸發。
        bool? x7RawForLog = x7ReadingValid ? x7 : null;
        if (x7RawForLog != _lastLoggedX7Raw)
        {
            _logger.Debug($"[LightGate] X7 原始讀值變化：{(x7RawForLog?.ToString() ?? "null(斷線/讀取已停止更新)")}" +
                $"（{(x7RawForLog == false ? "遮斷中" : "暢通")}）");
            _lastLoggedX7Raw = x7RawForLog;
        }

        // 每次輪詢都將原始訊號存入歷史佇列（最多 X7HistorySize 筆），純背景執行緒內部狀態，
        // 不需要切回 UI 執行緒。
        _x7SignalHistory.Enqueue((DateTime.Now, x7, rawBlocked));
        if (_x7SignalHistory.Count > X7HistorySize)
            _x7SignalHistory.Dequeue();

        if (!x7ReadingValid) return;

        var now = DateTime.Now;
        // 用 _lightGateBlockedConfirmed（背景執行緒自己同步維護），不要用 _isLightGateBlocked——
        // 後者只會透過 ApplyLightGateState 的 BeginInvoke 非同步更新，UI 執行緒忙碌時會延遲，
        // 這裡如果讀到舊值，會判斷錯分支、把真正的新觸發/新解除吃掉（見欄位上的說明）。
        bool blocked = _lightGateBlockedConfirmed; // 預設維持現狀，只有下面滿足對應門檻才轉換

        if (!_lightGateBlockedConfirmed)
        {
            // 目前是「暢通」狀態：要持續 LOW 滿 LightGateTriggerDuration 才確認觸發
            if (rawBlocked)
            {
                _lightGateLowSince ??= now;
                if (now - _lightGateLowSince.Value >= LightGateTriggerDuration)
                    blocked = true;
            }
            else
            {
                _lightGateLowSince = null;
            }
        }
        else
        {
            // 目前是「遮斷」狀態：要持續 HIGH 滿 LightGateClearDuration 才確認解除
            if (!rawBlocked)
            {
                _lightGateHighSinceAfterBlock ??= now;
                if (now - _lightGateHighSinceAfterBlock.Value >= LightGateClearDuration)
                    blocked = false;
            }
            else
            {
                _lightGateHighSinceAfterBlock = null;
            }
        }

        if (blocked == _lightGateBlockedConfirmed)
            return; // 沒有狀態轉換，不用切回 UI 執行緒，避免每 100ms 都排一次空的 BeginInvoke

        bool justTriggered = blocked && !_lightGateBlockedConfirmed;
        bool justCleared = !blocked && _lightGateBlockedConfirmed;

        // 立刻同步更新，不等 UI 執行緒的 BeginInvoke——下一輪輪詢（100ms 後）才不會讀到舊值。
        _lightGateBlockedConfirmed = blocked;

        if (justTriggered && !IsLightGateBypassActive)
        {
            // 跟 EMO 一致：安全動作（取消流程＋強制煞停）直接在背景執行緒做，不等 UI 執行緒排隊，
            // 機台停下來的動作不能因為 UI 執行緒卡住而跟著延遲。CancellationToken.Cancel() 是
            // 幂等操作，StopAllAxesImmediate 內部有自己的序列化保護，這裡先做一次不會有安全疑慮。
            _cts?.Cancel();
            _ = Task.Run(() => _machine.StopAllAxesImmediate("X7 光閘遮斷"));
        }

        if (justCleared)
        {
            // 光閘確認解除：清空計時器，讓下一次觸發從頭重新累積。
            _lightGateLowSince = null;
            _lightGateHighSinceAfterBlock = null;
            _logger.Info($"[LightGate] 光閘已持續暢通滿 {LightGateClearDuration.TotalMilliseconds:F0}ms，確認解除遮斷" +
                "（IsLightGateBlocked → false）。注意：異常燈號/Alarm 不會因此自動清除，" +
                "仍需執行原點賦歸或重新 Init 才能繼續 RUN——這是既有設計，不是這次改動造成的。");
        }

        // 其餘 UI 綁定狀態（IsLightGateBlocked、StatusMessage、Alarm、MES 回報…）一定要切回
        // UI 執行緒才能正確更新，但用 BeginInvoke（非阻塞）而不是 Invoke——原因跟 EMO 完全一樣：
        // 如果用 Invoke，UI 執行緒一旦卡住，這條專屬背景執行緒也會被拖著一起卡住，等於獨立
        // 執行緒的保護整個失效（EMO 那邊 8/26 現場已經實測踩過這個坑）。
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        dispatcher.BeginInvoke(() => ApplyLightGateState(blocked, justTriggered));
    }

    /// <summary>
    /// 套用光閘狀態轉換對應的 UI 綁定狀態／警報／MES 回報，只由 <see cref="OnLightGateTickCore"/>
    /// 透過 Dispatcher.BeginInvoke 呼叫，只在真的發生「觸發」或「解除」轉換時才會被呼叫。
    /// </summary>
    private void ApplyLightGateState(bool blocked, bool justTriggered)
    {
        if (justTriggered)
        {
            if (!IsLightGateBypassActive)
            {
                IsInitialized = false;
                StatusMessage = "⚠ 光閘觸發！請解除光閘後執行原點賦歸才可繼續 RUN";
                _machine.Alarms.Raise("LightGateX7", "X7 光閘遮斷，流程已取消，等待原點賦歸");

                // ── MES 上報 Alarm（光閘觸發事件）──
                _ = _mesClient.ReportAlarmAsync()
                    .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                        TaskScheduler.FromCurrentSynchronizationContext());

                // 組合前 N 筆 X7 訊號歷史一併寫入 log
                var historySb = new System.Text.StringBuilder();
                historySb.AppendLine($"--- 觸發前 X7 訊號歷史（最多 {X7HistorySize} 筆，觸發門檻={LightGateTriggerDuration.TotalMilliseconds:F0}ms 持續 LOW）---");
                int idx = 1;
                foreach (var entry in _x7SignalHistory)
                {
                    historySb.AppendLine(
                        $"  [{idx++:D2}] {entry.Time:HH:mm:ss.fff} | X7={entry.X7Raw,-5} | rawBlocked={entry.RawBlocked,-5}");
                }

                CancelEventLogger.Write(
                    "光閘X7",
                    "X7 光閘遮斷觸發，流程已取消，等待原點賦歸\n" + historySb);
            }
            else
            {
                // 上料模式：僅顯示提示，不中斷流程
                StatusMessage = "⚠ 光閘觸發（上料模式，流程不中斷）";
            }
        }

        IsLightGateBlocked = blocked;
    }

    /// <summary>
    /// 每 500ms 輪詢一次 PLC 板子感測等訊號（EMO 已移到獨立的 <see cref="EmoWatchLoop"/>，
    /// 光閘 X7 已移到獨立的 <see cref="LightGateWatchLoop"/>，都不再共用這個 UI 執行緒計時器）。
    /// X1+X2 兩個訊號同時為 true 且持續 0.5 秒後，自動觸發空跑程式。
    /// </summary>
    private void OnPlcTick(object? sender, EventArgs e)
    {
        // 每個 tick 都重新斷言紅燈輸出（依 _machine.Alarms.IsActive）。
        // 這一行是修掉「紅燈卡死關不掉」的關鍵：Y01 是實體 PLC 線圈，任何一次寫入都可能因為
        // PLC 剛好未連線而被 SetPlcYOctal 悄悄丟棄（尤其是開機當下第一次嘗試熄燈——那時候
        // PLC 通常都還沒連上）。靠這裡每 500ms 持續重新斷言，PLC 重新連線後最多一個 tick
        // 就會自動補回正確狀態，不需要使用者手動按任何按鈕才能讓燈熄滅。
        _machine.SyncRedAlarmOutput();

        // 即時載台側別（R/L）：累積 debounce 狀態，穩定後更新畫面顯示，不用等按 RUN 才知道目前是哪一側。
        // 只影響畫面顯示；RUN 當下實際判斷仍走 OnRunInspection 自己的阻塞式輪詢（DetermineCarrierSideDebouncedAsync）。
        if (!IsSimulationMode)
        {
            _machine.UpdateLiveCarrierSideTick();
            if (_machine.LiveCarrierSide is { } liveSide)
            {
                string liveSideText = liveSide.X12 ? "R" : "L";
                if (CarrierSide != liveSideText)
                {
                    // 第三層：確認「畫面綁定的屬性」真的有被寫入新值，不是只有 MachineController
                    // 內部的 LiveCarrierSide 更新了、卻沒傳到這裡。
                    _logger.Debug($"[LiveCarrierSide] UI 套用：CarrierSide {CarrierSide} → {liveSideText}");
                    CarrierSide = liveSideText;
                    AreaATitle = liveSide.X12 ? "Area A  (C4)" : "Area A  (C2)";
                    AreaBTitle = liveSide.X12 ? "Area B  (C3)" : "Area B  (C1)";
                }
            }
        }

        // X7 光閘：已改成跟 EMO 一樣的獨立背景 Thread（見 LightGateWatchLoop/OnLightGateTickCore），
        // 不再掛在這個共用的 UI 執行緒 _plcTimer 上，這裡不需要再處理。

        // ─── X3/X11 板子放置雷射感測（任一 true = 未放好）───
        // x3/x11 一律保留「感測器實際原始值」供歷史/追蹤 log 還原真實時間軸；
        // x3Effective/x11Effective 才是套用 Admin 個別停用設定後、真正用於誤放判斷的值。
        bool x3 = false, x11 = false, x3Effective = false, x11Effective = false;
        bool rawMisplaced = false, boardMisplacedConfirmed = false, boardOkConfirmed = false;
        bool boardReadingValid = false; // true 表示這次 x3/x11 是實際讀到的值，可信任、可寫入持續追蹤 log

        if (IsSimulationMode)
        {
            // 模擬模式：依 _simBoardState 決定 x3/x11，跳過防抖立即生效
            // bit0=1 → X3 異常；bit1=1 → X11 異常（0=正常,1=X3,2=X11,3=兩者）
            x3  = (_simBoardState & 1) != 0;
            x11 = (_simBoardState & 2) != 0;
            x3Effective  = x3  && !_machine.Config.PauseX3BoardSensor;
            x11Effective = x11 && !_machine.Config.PauseX11BoardSensor;
            rawMisplaced            = x3Effective || x11Effective;
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
                x3Effective  = x3  && !_machine.Config.PauseX3BoardSensor;
                x11Effective = x11 && !_machine.Config.PauseX11BoardSensor;
                rawMisplaced = x3Effective || x11Effective;

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

            ShowBoardMisplacedDialog(x3Effective, x11Effective);
        }
        // 已在警報中且板子仍未放妥：人員把提示視窗關掉卻沒有移動 / 重放產品
        // → 每次 tick 都重新跳出提示，直到板子確實放好（boardOkConfirmed）為止。
        // ShowBoardMisplacedDialog 內部用 _boardMisplacedDialogOpen 擋掉「視窗還開著就再疊一個」的情況，
        // 所以這裡放心每個 tick 都呼叫，實際上只會在使用者按掉上一個視窗後才再彈。
        else if (boardMisplacedConfirmed && _isBoardMisplaced)
        {
            ShowBoardMisplacedDialog(x3Effective, x11Effective);
        }
        // 未放好 → 正常（訊號穩定回到正常後解除）
        else if (boardOkConfirmed && _isBoardMisplaced)
        {
            IsBoardMisplaced = false;
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
        // 見 OnInitDevices 的說明：用本地 cts，finally 只在仍是「自己這條」時才清 _cts。
        var myCts = new CancellationTokenSource();
        _cts = myCts;
        // 使用者主動嘗試初始化，視為對「上一次可由使用者操作解除」的異常做確認：
        // 只清可由重試解除的原因，軸警報等硬體原因由 MachineController 在檢查各軸狀態時自行 Raise/Clear。
        _machine.Alarms.Clear("LightGateX7");
        _machine.Alarms.Clear("RunException");

        // Progress 回報會自動回到 UI 執行緒
        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            var result = await Task.Run(
                () => _machine.InitializeAllAsync(progress, myCts.Token));

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
            if (ReferenceEquals(_cts, myCts)) _cts = null;
            myCts.Dispose();
        }
    }

    // ═══════════════════════════════════════
    //  START：完整自動流程
    //  S01a 載台在席偵測(=有料) → S01b 移動讀碼 → S01c 軸復歸 → S03 檢測 → S04 結果
    // ═══════════════════════════════════════

    private async void OnStart()
    {
        IsRunning = true;
        // 見 OnInitDevices 的說明：用本地 cts，finally 只在仍是「自己這條」時才清 _cts。
        var myCts = new CancellationTokenSource();
        _cts = myCts;
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
                () => _machine.MoveAndReadBarcodeAsync(carrierPos, statusProgress, myCts.Token));

            if (string.IsNullOrEmpty(code))
            {
                StatusMessage = "條碼讀取失敗，正在將軸退回原點...";
                await Task.Run(() => _machine.MoveBarcodeAxisHomeAsync(statusProgress, myCts.Token));
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
                () => _machine.MoveBarcodeAxisHomeAsync(statusProgress, myCts.Token));

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
                () => _machine.RunInspectionAsync(_confirmedBarcode, slotProgress, myCts.Token));

            // 全部完成 → 計算統計
            AreaA_Stats.Calculate(AreaA_Row1);
            AreaB_Stats.Calculate(AreaB_Row1);

            bool pass = AreaA_Stats.Result == "OK" && AreaB_Stats.Result == "OK";

            // S04：寫彙總結果
            InspectionResultWriter.WriteSummary(_confirmedBarcode, pass);
            RunQcAutoCheck();

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
            StatusMessage = $"⏹ {DescribeOperatorFacingError("作業已由使用者取消")}，請執行原點賦歸後再繼續 RUN";
            CancelEventLogger.Write("OnStart", "流程被取消（OperationCanceledException）", oce);
        }
        catch (Exception ex)
        {
            IsInitialized = false;
            StatusMessage = $"✘ {DescribeOperatorFacingError($"量測發生錯誤：{ex.Message}")}，請執行原點賦歸後再繼續 RUN";
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
            if (ReferenceEquals(_cts, myCts)) _cts = null;
            myCts.Dispose();
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

        if (IsLightGateBypassActive)
        {
            IsLightGateBypassActive = false;
            AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "RUN 觸發自動恢復光閘保護（自動切換為下料模式）");
        }

        IsDryRunning = true;
        // 見 OnInitDevices 的說明：用本地 cts，finally 只在仍是「自己這條」時才清 _cts。
        var myCts = new CancellationTokenSource();
        _cts = myCts;
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
            var ct = myCts.Token;

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

            // 偵測 X12 / X13 載台訊號（連續讀值一致才確認，避免單次雜訊誤判 R/L）
            (bool x12, bool x13) = await _machine.DetermineCarrierSideDebouncedAsync(ct);

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
            RunQcAutoCheck();

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
            StatusMessage = $"⏹ {DescribeOperatorFacingError("主流程已由使用者取消")}，請執行原點賦歸後再繼續 RUN";
            CancelEventLogger.Write("OnRunInspection", "主流程被取消（OperationCanceledException）", oce);
        }
        catch (Exception ex)
        {
            IsInitialized = false;
            StatusMessage = $"✘ {DescribeOperatorFacingError($"主流程發生錯誤：{ex.Message}")}，請執行原點賦歸後再繼續 RUN";
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
            if (ReferenceEquals(_cts, myCts)) _cts = null;
            myCts.Dispose();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async void OnReadBarcode()
    {
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "讀取條碼");
        IsRunning = true;
        // 見 OnInitDevices 的說明：用本地 cts，finally 只在仍是「自己這條」時才清 _cts。
        var myCts = new CancellationTokenSource();
        _cts = myCts;
        var ct = myCts.Token;

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
            StatusMessage = $"⏹ {DescribeOperatorFacingError("讀碼作業已取消")}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✘ {DescribeOperatorFacingError($"讀碼發生錯誤：{ex.Message}")}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsRunning = false;
            if (ReferenceEquals(_cts, myCts)) _cts = null;
            myCts.Dispose();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void OnStop()
    {
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "按下 STOP");
        _cts?.Cancel();
        // 取消 CancellationToken 無法中斷已送出的軸移動指令，必須直接對軸體下 MotStop 才能真正停下來
        _machine.StopAllAxesImmediate("使用者按下 STOP");
        IsRunning = false;
        IsDryRunning = false;
        // Stop 後必須重新原點賦歸才可再按 RUN（IsInitialized setter 會同步鎖上 _requireReinitAfterStop，
        // 擋掉 IsZAtSafeHeight 的旁路，直到 Home/S00 初始化成功才解除，避免曾發生過的撞機情形）
        IsInitialized = false;
        StatusMessage = "⏹ 作業已由使用者取消，機台已立即停止，請執行原點賦歸後再繼續 RUN";
        CancelEventLogger.Write("OnStop", "使用者按下 STOP 按鈕，流程已中止，所有軸已立即停止");
        CommandManager.InvalidateRequerySuggested();
    }

    private async void OnMoveToPickPosition()
    {
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "移至取料位");
        IsRunning = true;
        // 見 OnInitDevices 的說明：用本地 cts，finally 只在仍是「自己這條」時才清 _cts。
        var myCts = new CancellationTokenSource();
        _cts = myCts;
        var ct = myCts.Token;

        try
        {
            double targetY = _machine.Config.BarcodePositionY;

            // 移動 Y 軸前先確認 ZL/ZR 在安全高度，避免碰撞
            bool mpX12 = _machine.GetPlcXOctal(12) ?? false;
            bool mpX13 = _machine.GetPlcXOctal(13) ?? false;
            double mpSafeZL = _machine.Config.GetZLSafeHeight(mpX12, mpX13);
            double mpSafeZR = _machine.Config.GetZRSafeHeight(mpX12, mpX13);
            var mpProgress = new Progress<string>(msg => StatusMessage = msg);
            await _machine.EnsureZSafeBeforeYMoveAsync(mpSafeZL, mpSafeZR, mpProgress, ct, "OnMoveToPickPosition");

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
            StatusMessage = $"⏹ {DescribeOperatorFacingError("移動至取料位置已取消")}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✘ {DescribeOperatorFacingError($"移動至取料位置發生錯誤：{ex.Message}")}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsRunning = false;
            if (ReferenceEquals(_cts, myCts)) _cts = null;
            myCts.Dispose();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// 開啟「Recipe 切換」對話視窗（Recipe 切換／備份／刪除，也可微調座標），確認後寫回 InspectionConfig。
    /// </summary>
    private void OnPositionSettings()
    {
        // 只有真正的 PositionSettings 權限（Admin/Engineer，或 Admin 額外開放的 OP）才能編輯座標本身、
        // 管理 Recipe 備份/刪除；只有 RecipeSwitch 權限（一般開放給 OP）只能選 Recipe + 讀取套用，
        // 座標分頁顯示但唯讀，備份/建立/刪除按鈕停用。
        var vm = new PositionSettingsViewModel
        {
            CurrentUser = CurrentUser,
            CanEditCoordinates = HasPermission(Permission.PositionSettings),
        };
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

        // 視窗內「讀取套用／備份／刪除」Recipe 會立即寫回 _machine.Config 並存檔，
        // 即使使用者最後按取消，Recipe 也可能已經變了，所以不論回傳值都要刷新首頁顯示。
        OnPropertyChanged(nameof(CurrentRecipeDisplay));
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
        var config = _machine.Config;
        bool admin = IsAdmin;
        var vm = new LightSettingsViewModel(config, admin, _machine.TryApplyLightIntensityNow);
        var win = new Views.LightSettingsWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };
        if (win.ShowDialog() != true) return;

        // 依「儲存前 vs 使用者編輯後」實際有變動的分頁組出狀態列訊息，避免永遠只顯示亮度設定
        var changes = new List<string>();
        bool lightIntensityChanged = config.LightIntensityLeft != vm.LightIntensityLeft || config.LightIntensityRight != vm.LightIntensityRight;
        if (lightIntensityChanged)
            changes.Add($"亮度（左={vm.LightIntensityLeft}，右={vm.LightIntensityRight}）");
        if (config.ValueLimitLow != vm.ValueLimitLow || config.ValueLimitHigh != vm.ValueLimitHigh)
            changes.Add($"Value極限（下限={vm.ValueLimitLow}，上限={vm.ValueLimitHigh}）");
        if (admin && (config.ExposureTimeLeft != vm.ExposureTimeLeft || config.ExposureTimeRight != vm.ExposureTimeRight))
            changes.Add($"曝光時間（左={vm.ExposureTimeLeft:F0}µs，右={vm.ExposureTimeRight:F0}µs）");
        if (admin && (config.PauseX3BoardSensor != vm.PauseX3BoardSensor || config.PauseX11BoardSensor != vm.PauseX11BoardSensor))
            changes.Add($"板子感測停用（X3={(vm.PauseX3BoardSensor ? "停用" : "啟用")}，X11={(vm.PauseX11BoardSensor ? "停用" : "啟用")}）");
        vm.ApplyTo(config);

        // 亮度變更時立即送到硬體套用，讓操作員馬上肉眼確認，不必等下次開燈才生效；
        // 失敗時明確回報在狀態列，避免像先前一樣調了沒反應卻不知道是指令沒送到
        string? lightApplyMessage = null;
        if (lightIntensityChanged)
        {
            bool applied = _machine.TryApplyLightIntensityNow(vm.LightIntensityLeft, vm.LightIntensityRight, out string applyMsg);
            lightApplyMessage = applyMsg;
            if (!applied)
                _logger.Warn($"[LightSettings] 亮度即時套用失敗：{applyMsg}");
        }

        StatusMessage = changes.Count > 0
            ? $"✔ 設定已儲存：{string.Join("；", changes)}" + (lightApplyMessage != null ? $"（{lightApplyMessage}）" : "")
            : "✔ 設定已儲存（內容無變更）";

        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "套用光源設定");
        if (admin)
        {
            AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, $"套用曝光時間設定（左={vm.ExposureTimeLeft:F0}µs，右={vm.ExposureTimeRight:F0}µs）");
            AuditLogger.Write(CurrentUser.Username, CurrentUser.Role,
                $"套用板子感測停用設定（X3停用={vm.PauseX3BoardSensor}，X11停用={vm.PauseX11BoardSensor}）");
        }
    }

    /// <summary>如果目前 Recipe 有「這一輪偵測到的側別（R/L）」的 QC 基準，跑完一輪後自動比對並在狀態列提示
    /// （不強制彈窗，避免量產連續RUN被中斷）。R、L 各自比對自己那份基準，不會互相比錯。</summary>
    private void RunQcAutoCheck()
    {
        _lastQcRunAt = DateTime.Now;
        _qcWindow?.NotifyInspectionCompleted(); // 開著獨立 QC 比對視窗的話，讓它即時重新比對

        // QC 比對總開關（可在「QC 比對」視窗切換）：關閉時不自動比對、不寫狀態列、不產生 CSV
        if (!_machine.Config.QcCompareEnabled) return;

        string recipeName = _machine.Config.CurrentRecipeName;
        string side = CarrierSide?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(recipeName)
            || !AxisRecipeManager.IsValidSide(side)
            || !AxisRecipeManager.HasBaseline(recipeName, side)) return;
        try
        {
            var baseline = AxisRecipeManager.LoadBaseline(recipeName, side);
            var rows = QcComparisonViewModel.BuildRows(baseline, AreaA_Row1, AreaB_Row1, _machine.Config);
            QcComparisonResultWriter.Write(_confirmedBarcode, recipeName, side, rows);
            int exceedCount = rows.Count(r => r.ExceedsTolerance);
            StatusMessage += exceedCount > 0
                ? $"　⚠ QC比對（{side}側）：{exceedCount} 個 Slot 偏離基準（按「🔍 QC 比對」查看明細）"
                : $"　✔ QC比對（{side}側）：全部貼近基準";
        }
        catch
        {
            // 基準檔損毀或報表寫入失敗等非致命錯誤，不影響主流程
        }
    }

    /// <summary>
    /// 開啟獨立的「QC 比對」儀表板視窗（非強制回應，可一直開著）。
    /// 生效 Recipe / 側別 / 條碼 / 量測結果都自動帶入；每跑完一輪 <see cref="RunQcAutoCheck"/> 會通知它重新比對。
    /// 單一實例：已開著就把它帶到最上層，不重複開。
    /// </summary>
    private void OnOpenQcInspection()
    {
        if (_qcWindow != null)
        {
            _qcWindow.Activate();
            return;
        }

        var vm = new QcInspectionViewModel(
            _machine.Config, CurrentUser,
            canUseQcRecipe: HasPermission(Permission.QcRecipe),
            canEditQc: IsEngineerOrAbove,   // 存基準、改允許範圍：僅 Engineer / Admin（Operator 只能看）
            () => new QcLiveSnapshot(
                _machine.Config.CurrentRecipeName ?? "",
                CarrierSide ?? "",
                _confirmedBarcode ?? "",
                _lastQcRunAt,
                AreaA_Row1.ToList(),
                AreaB_Row1.ToList()));

        _qcWindow = new Views.QcInspectionWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };
        _qcWindow.Closed += (_, _) => _qcWindow = null;

        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "開啟 QC 比對視窗");
        _qcWindow.Show();
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
        // 用本地變數持有自己的 cts：EMO 卡死的舊流程工作稍後才解掉時，它的 finally 不會
        // 誤 Dispose / 清掉「這次初始化」正在用的 _cts（見下方 finally 的 ReferenceEquals 判斷）。
        var myCts = new CancellationTokenSource();
        _cts = myCts;
        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {

            SlotPositionTable.Load();

            StatusMessage = "正在初始化設備（設定檔 / 伺服軸 / PLC），請稍候...";
            var result = await Task.Run(
                () => _machine.InitializeDevicesAsync(progress, myCts.Token));

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
            StatusMessage = $"⏹ {DescribeOperatorFacingError("設備初始化已取消")}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✘ {DescribeOperatorFacingError($"設備初始化發生錯誤：{ex.Message}")}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsDryRunning = false;
            if (ReferenceEquals(_cts, myCts)) _cts = null;
            myCts.Dispose();
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
        // 見 OnInitDevices 的說明：用本地 cts，finally 只在仍是「自己這條」時才清 _cts。
        var myCts = new CancellationTokenSource();
        _cts = myCts;
        _machine.Alarms.Clear("LightGateX7");
        _machine.Alarms.Clear("RunException");
        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            StatusMessage = "正在執行原點賦歸，請稍候...";
            var result = await Task.Run(
                () => _machine.HomeAllAxesPublicAsync(progress, myCts.Token));

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
            StatusMessage = $"⏹ {DescribeOperatorFacingError("原點賦歸已取消")}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✘ {DescribeOperatorFacingError($"原點賦歸發生錯誤：{ex.Message}")}";
            System.Diagnostics.Debug.WriteLine(ex);
        }
        finally
        {
            IsDryRunning = false;
            if (ReferenceEquals(_cts, myCts)) _cts = null;
            myCts.Dispose();
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
    /// 在「上料模式」（關閉光閘，光閘觸發不中斷流程）與「下料模式」（開啟光閘，恢復正常防護）之間切換。
    /// 上料模式：同時鎖定 RUN 與原點賦歸。
    /// 下料模式：恢復正常光閘防護。
    /// </summary>
    private void OnToggleLightGateBypass()
    {
        _logger.Info($"[LightGateBypass] OnToggleLightGateBypass 被觸發，切換前 IsLightGateBypassActive={IsLightGateBypassActive}，" +
            $"User={CurrentUser.Username}, Role={CurrentUser.Role}, HasPermission={HasPermission(Permission.ToggleLightGateBypass)}");
        IsLightGateBypassActive = !IsLightGateBypassActive;
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role,
            $"光閘保護切換為{(IsLightGateBypassActive ? "上料模式" : "下料模式")}");

        if (!IsLightGateBypassActive)
        {
            // 切換為下料模式時：重置防抖計時，讓光閘狀態在下一次 PLC 輪詢時重新評估
            // 避免取料期間的遮斷殘留導致 RUN / 原點賦歸持續鎖定
            _lightGateLowSince = null;
            _lightGateHighSinceAfterBlock = null;
            _lightGateBlockedConfirmed = false; // 同步重置背景執行緒自己的狀態，避免跟 UI 顯示不一致
            IsLightGateBlocked = false;
        }

        StatusMessage = IsLightGateBypassActive
            ? "⚠ 已切換為上料模式：光閘觸發不中斷流程（RUN 與原點賦歸暫時鎖定）"
            : "✔ 已切換為下料模式，恢復正常光閘防護";
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

    /// <summary>
    /// 跳出「板子未放妥」提示視窗。
    /// 初次觸發、以及之後每個 PLC tick 只要板子仍未放妥都會呼叫這裡；
    /// _boardMisplacedDialogOpen 確保同一時間只會有一個視窗：使用者按掉上一個後，
    /// 下一次 tick 才會再彈一個，等於「不放好就一直提醒」，直到訊號穩定回到正常。
    /// </summary>
    /// <param name="x3Effective">套用 Admin 個別停用設定後、X3 是否算異常</param>
    /// <param name="x11Effective">套用 Admin 個別停用設定後、X11 是否算異常</param>
    private void ShowBoardMisplacedDialog(bool x3Effective, bool x11Effective)
    {
        // 視窗還開著就不要再疊一個：MessageBox 的巢狀訊息泵期間 tick 仍會持續進來
        if (_boardMisplacedDialogOpen) return;
        _boardMisplacedDialogOpen = true;

        // 用 Effective 值判斷觸發原因（已停用的訊號即使原始值異常也不算數，避免誤導使用者）
        string triggeredSignals = (x3Effective && x11Effective) ? "X3、X11" : (x3Effective ? "X3" : "X11");

        // BeginInvoke 讓對話框在 DispatcherTimer tick 結束後才彈出，
        // 避免 MessageBox 建立巢狀訊息泵（re-entrancy）導致 tick 重複執行
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                MessageBox.Show(
                    $"偵測到板子未放妥（{triggeredSignals} 訊號異常）。\n請確認板子正確放置後再繼續操作。",
                    "板子未放好",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            finally
            {
                _boardMisplacedDialogOpen = false;
            }
        });
    }

    private async void OnSlcReset()
    {
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "執行 SLC Reset");
        StatusMessage = "⏳ SLC Reset 執行中...";
        try
        {
            await _machine.SlcResetAsync();

            // Reset 成功：清除光閘遮斷狀態，讓 RUN / 原點賦歸恢復可用
            _lightGateLowSince = null;
            _lightGateHighSinceAfterBlock = null;
            _lightGateBlockedConfirmed = false; // 同步重置背景執行緒自己的狀態，避免跟 UI 顯示不一致
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
    /// 操作員確認 EMO 已釋放、現場安全後手動按下，才真正清除 "EMO" 異常燈號/訊息。
    /// 只在 X0 已恢復正常（!IsEmoTriggered）且確實有待確認的 EMO 事件（IsEmoAlarmPendingReset）時可按。
    /// </summary>
    private void OnEmoReset()
    {
        AuditLogger.Write(CurrentUser.Username, CurrentUser.Role, "執行 EMO Reset 確認");
        _machine.Alarms.Clear("EMO");
        IsEmoAlarmPendingReset = false;
        // 保險：EMO 觸發當下若流程方法卡死、旗標沒被清乾淨，這裡再強制清一次，
        // 確保初始化按鈕的解鎖條件（!IsRunning && !IsDryRunning && !IsDeviceReady）一定成立。
        IsRunning = false;
        IsDryRunning = false;
        IsInitialized = false;
        StatusMessage = "✔ EMO 已復歸確認，請重新執行初始化與原點賦歸後再繼續 RUN";
        // 診斷用：InitAxesCommand 的 CanExecute 要 !IsRunning && !IsDryRunning && !IsDeviceReady
        // && 有權限 同時成立才會解鎖。這裡印出這四個條件的當下值，如果初始化按鈕按不動，
        // 直接看這行就知道是哪個條件卡住，不用等重現、不用找機台上的 log。
        _logger.Info($"[EMO] EMO Reset 完成，初始化按鈕解鎖條件：IsRunning={IsRunning} IsDryRunning={IsDryRunning} " +
            $"IsDeviceReady={IsDeviceReady}（要是 false 才能按）HasInitPermission={HasPermission(Permission.InitDevices)}");
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

    /// <summary>開啟料號設定表編輯視窗（僅Admin可執行，由CanExecute保證）。</summary>
    private void OnOpenMaterialRecipeMap()
    {
        var win = new Views.MaterialRecipeMapWindow(CurrentUser)
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
        RunQcAutoCheck();

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
