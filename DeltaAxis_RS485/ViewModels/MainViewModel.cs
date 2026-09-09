using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DeltaAxis_RS485.Models;
using DeltaAxis_RS485.Services;

namespace DeltaAxis_RS485.ViewModels;

public class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private bool _disposed;

    public ObservableCollection<AxisViewModel> Axes { get; } = [];

    private AxisViewModel? _selectedAxis;
    public AxisViewModel? SelectedAxis
    {
        get => _selectedAxis;
        set => SetField(ref _selectedAxis, value);
    }

    public ICommand ConnectAllCommand { get; }
    public ICommand DisconnectAllCommand { get; }
    public ICommand LoadSafetyConfigCommand { get; }

    // ============================
    //  Slot_Inspection 安全參考設定（方案A：直接讀取主程式的Config JSON）
    // ============================

    private string _slotInspectionConfigPath = "";

    /// <summary>
    /// Slot_Inspection主程式的Config資料夾路徑（UI 輸入框）。留白時交給 <see cref="SlotInspectionConfigLocator"/>
    /// 自動尋找；填了就當作最優先的候選。讀取成功後這裡會被改寫成實際採用的絕對路徑。
    /// </summary>
    public string SlotInspectionConfigPath
    {
        get => _slotInspectionConfigPath;
        set => SetField(ref _slotInspectionConfigPath, value);
    }

    private string _safetyConfigStatusMessage = "尚未讀取安全參考設定";
    public string SafetyConfigStatusMessage
    {
        get => _safetyConfigStatusMessage;
        private set => SetField(ref _safetyConfigStatusMessage, value);
    }

    private SlotInspectionSafetySnapshot? _safetySnapshot;

    // ── 設定檔持久化 + 「自動跟隨主程式變更」機制 ──
    private readonly DeltaAxisSettings _settings;
    private string? _resolvedConfigFolder;          // 目前生效的 Config 資料夾（絕對路徑）
    private string? _watchedFolder;                 // FileSystemWatcher 目前正在監看的資料夾
    private FileSystemWatcher? _configWatcher;
    private System.Timers.Timer? _reloadDebounce;   // 事件去抖動：檔案連發變更時只在安靜後重讀一次
    private readonly object _reloadLock = new();

    private bool _autoFollow;
    /// <summary>主程式存檔改設定檔時，是否自動重新載入安全設定。</summary>
    public bool AutoFollowSlotInspectionConfig
    {
        get => _autoFollow;
        set
        {
            if (!SetField(ref _autoFollow, value)) return;
            _settings.AutoFollowSlotInspectionConfig = value;
            _settings.Save();
            if (value && _resolvedConfigFolder != null) ArmConfigWatcher(_resolvedConfigFolder);
            else DisarmConfigWatcher();
        }
    }

    public MainViewModel()
    {
        // 讀取本工具自己的小設定檔（上次的 Config 路徑、自動跟隨偏好）
        _settings = DeltaAxisSettings.Load();
        _autoFollow = _settings.AutoFollowSlotInspectionConfig;               // 直接寫 backing field，避免在建構期間觸發存檔/掛 watcher
        if (!string.IsNullOrWhiteSpace(_settings.SlotInspectionConfigFolder))
            _slotInspectionConfigPath = _settings.SlotInspectionConfigFolder!;

        // ============================
        // 軸組態（實務上可從 JSON 讀取）
        // ============================
        var configs = new List<AxisConfig>
        {
            new()
            {
                Name = "Y軸", PortName = "COM12", SlaveId = 1,
                PositiveDirectionLabel = "遠離原點", NegativeDirectionLabel = "靠近原點",
                JogButtonsMultiRow = true
                // 取料點參考位置改由 LoadSafetyConfig() 從 Slot_Inspection 的
                // InspectionConfig.json 動態讀取後填入，不再寫死數字。
            },
            new()
            {
                Name = "X軸", PortName = "COM13", SlaveId = 1,
                PositiveDirectionLabel = "遠離原點", NegativeDirectionLabel = "靠近原點",
                JogButtonsMultiRow = true
            },
            new()
            {
                Name = "ZL軸", PortName = "COM14", SlaveId = 1,
                PositiveDirectionLabel = "下降", NegativeDirectionLabel = "上升"
            },
            new()
            {
                Name = "ZR軸", PortName = "COM15", SlaveId = 1,
                PositiveDirectionLabel = "下降", NegativeDirectionLabel = "上升"
            },
        };

        foreach (var cfg in configs)
            Axes.Add(new AxisViewModel(cfg));

        if (Axes.Count > 0)
            SelectedAxis = Axes[0];

        // 為 Y 軸設定移動前安全檢查：確保 ZL/ZR 已連線、絕對座標正常，且高度低於安全高度門檻
        // （門檻優先取自 Slot_Inspection 主程式設定，見 SetupYAxisPreMoveCheck / ResolveZSafeCeiling）
        SetupYAxisPreMoveCheck();

        // 為 ZL/ZR 設定「目標位置額外警告」：比對 Slot_Inspection 設定的Y軸已知位置/相機高度上限
        SetupZAxisExtraMoveWarning();

        ConnectAllCommand = new RelayCommand(() =>
        {
            foreach (var ax in Axes)
                if (ax.ConnectCommand.CanExecute(null))
                    ax.ConnectCommand.Execute(null);
        });

        DisconnectAllCommand = new RelayCommand(() =>
        {
            foreach (var ax in Axes)
                if (ax.DisconnectCommand.CanExecute(null))
                    ax.DisconnectCommand.Execute(null);
        });

        LoadSafetyConfigCommand = new RelayCommand(() => LoadSafetyConfig(triggeredByWatcher: false));

        // 開機嘗試自動讀取一次（找不到也不影響其他功能，僅顯示狀態訊息）
        LoadSafetyConfig(triggeredByWatcher: false);
    }

    /// <summary>
    /// 讀取 Slot_Inspection 的安全參考設定（25個Slot Y座標、取料位置、ZL/ZR相機高度）。
    /// 讀取成功後：更新 Y 軸的取料點參考位置，並讓 ZL/ZR 的 ExtraMoveWarning 套用最新數值。
    /// </summary>
    private void LoadSafetyConfig(bool triggeredByWatcher)
    {
        // 先決定要從哪個資料夾讀：UI 輸入框 → 環境變數 → 上次成功路徑 → exe 旁 → 開發機相對路徑 → 機台網路路徑
        string? folder = SlotInspectionConfigLocator.Resolve(
            uiHint: _slotInspectionConfigPath,
            lastGood: _settings.SlotInspectionConfigFolder);

        if (folder == null)
        {
            _safetySnapshot = null;
            DisarmConfigWatcher();
            SafetyConfigStatusMessage =
                "✘ 找不到 Slot_Inspection 的 Config 資料夾（已試過 UI 指定路徑、環境變數 " +
                $"{SlotInspectionConfigLocator.EnvVarName}、上次路徑、exe 旁、開發機相對路徑、機台網路路徑）";
            return;
        }

        try
        {
            var snapshot = SlotInspectionConfigReader.Load(folder);
            _safetySnapshot = snapshot;
            _resolvedConfigFolder = folder;

            // 記住這次成功的路徑，下次開機直接用
            _settings.SlotInspectionConfigFolder = folder;
            _settings.Save();
            // 把 UI 輸入框同步成實際採用的絕對路徑，讓使用者看得到讀的是哪一份
            SlotInspectionConfigPath = folder;

            // 更新 Y 軸的取料點參考位置（用真正讀到的 BarcodePositionY，不再寫死）
            if (Axes.Count > 0)
            {
                double pickPositionY = snapshot.KnownYPositions.Count > 0
                    ? snapshot.KnownYPositions[^1] // KnownYPositions 最後一個元素是 BarcodePositionY
                    : 0;
                Axes[0].Config.ReferencePositions.Clear();
                Axes[0].Config.ReferencePositions.Add(new AxisReferencePosition
                {
                    Name = "取料點",
                    PositionMm = pickPositionY,
                    WarnToleranceMm = 5.0
                });
            }

            // 依「自動跟隨」偏好掛上 / 維持 FileSystemWatcher
            if (_autoFollow) ArmConfigWatcher(folder);
            else DisarmConfigWatcher();

            // 有 snapshot 就一定照主程式設定（含容差）
            double zlCeil = snapshot.ZLSafeHeightCeiling + snapshot.ZSafeTolerance;
            double zrCeil = snapshot.ZRSafeHeightCeiling + snapshot.ZSafeTolerance;
            string prefix = triggeredByWatcher ? "↻ 偵測到主程式設定變更，已自動重新載入 ｜ " : "";

            SafetyConfigStatusMessage =
                $"{prefix}✔ 已讀取（{snapshot.LoadedAt:HH:mm:ss}）自 {folder}｜" +
                $"已知Y位置{snapshot.KnownYPositions.Count}點，" +
                $"ZL深度上限{snapshot.ZLCameraHeightLimit:F2}mm，ZR深度上限{snapshot.ZRCameraHeightLimit:F2}mm，" +
                $"Y移動前Z需低於 ZL{zlCeil:F2}/ZR{zrCeil:F2}mm（主程式安全高度+容差{snapshot.ZSafeTolerance:F2}）" +
                $"｜自動跟隨：{(_autoFollow ? "開" : "關")}";
        }
        catch (Exception ex)
        {
            // 已經有一份讀好的設定時，遇到暫時性失敗（例如剛好讀到主程式寫一半的檔）就保留舊的，只提示；
            // 從頭到尾沒讀成功過才真的清成 null。
            if (_safetySnapshot == null)
                SafetyConfigStatusMessage = $"✘ 讀取失敗（{folder}）：{ex.Message}";
            else
                SafetyConfigStatusMessage =
                    $"⚠ 這次重讀失敗（{ex.Message}），仍沿用上次讀到的設定（{_safetySnapshot.LoadedAt:HH:mm:ss}）";
        }
    }

    // ============================
    //  「自動跟隨主程式變更」：監看 Config 資料夾裡的兩個 JSON，改了就自動重讀
    // ============================

    private void ArmConfigWatcher(string folder)
    {
        if (!_autoFollow) { DisarmConfigWatcher(); return; }

        // 已經在監看同一個資料夾就不重建
        if (_configWatcher != null &&
            string.Equals(_watchedFolder, folder, StringComparison.OrdinalIgnoreCase))
            return;

        DisarmConfigWatcher();
        try
        {
            var w = new FileSystemWatcher(folder)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
                             | NotifyFilters.FileName | NotifyFilters.CreationTime,
                Filter = "*.json",
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            w.Changed += OnConfigFileChanged;
            w.Created += OnConfigFileChanged;
            w.Renamed += OnConfigFileChanged;
            _configWatcher = w;
            _watchedFolder = folder;
        }
        catch (Exception ex)
        {
            // 監看不起來（例如網路磁碟不支援）不影響已讀到的設定，只是不會自動更新
            SafetyConfigStatusMessage =
                $"（自動跟隨無法啟用：{ex.Message}；已讀取的設定仍有效，可手動按「讀取安全設定」更新）";
        }
    }

    private void DisarmConfigWatcher()
    {
        if (_configWatcher != null)
        {
            _configWatcher.EnableRaisingEvents = false;
            _configWatcher.Changed -= OnConfigFileChanged;
            _configWatcher.Created -= OnConfigFileChanged;
            _configWatcher.Renamed -= OnConfigFileChanged;
            _configWatcher.Dispose();
            _configWatcher = null;
        }
        _watchedFolder = null;
    }

    private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
    {
        var name = Path.GetFileName(e.Name) ?? "";
        if (!name.Equals("InspectionConfig.json", StringComparison.OrdinalIgnoreCase) &&
            !name.Equals("SlotPositions.json", StringComparison.OrdinalIgnoreCase))
            return;

        // 主程式用 File.WriteAllText 存檔：一次存檔會連發好幾個事件，且事件當下檔案可能還在寫一半。
        // 所以不立刻重讀，改成「重設一個 800ms 計時器」，事件連發期間一直往後延，安靜下來才真的重讀一次。
        lock (_reloadLock)
        {
            _reloadDebounce ??= CreateDebounceTimer();
            _reloadDebounce.Stop();
            _reloadDebounce.Start();
        }
    }

    private System.Timers.Timer CreateDebounceTimer()
    {
        var t = new System.Timers.Timer(800) { AutoReset = false };
        t.Elapsed += (_, _) =>
        {
            // FileSystemWatcher / 計時器的 callback 在背景執行緒，套用到 UI 綁定要切回 UI 執行緒（非阻塞）
            var disp = Application.Current?.Dispatcher;
            if (disp != null) disp.BeginInvoke(new Action(() => LoadSafetyConfig(triggeredByWatcher: true)));
            else LoadSafetyConfig(triggeredByWatcher: true);
        };
        return t;
    }

    /// <summary>
    /// 由主程式安全高度算出「Y 軸移動前，Z 實際高度必須低於的門檻」。
    /// 只要有成功讀到主程式設定（safeHeight 非 null），就完全照它：門檻 = 安全高度 + 容差（即使安全高度是 0）。
    /// 完全沒讀到設定時（safeHeight 為 null）才退回 DeltaAxis 自己的保守預設值。
    /// </summary>
    private static (double Ceiling, bool FromConfig) ResolveZSafeCeiling(double? safeHeight, double? tolerance)
    {
        if (safeHeight is null)
            return (FallbackZSafeHeightMm, false);

        double tol = tolerance is > 0 ? tolerance.Value : 1.0;
        return (safeHeight.Value + tol, true);
    }

    /// <summary>
    /// Y 軸移動前 Z 安全高度門檻的 fallback：僅在「完全沒讀到主程式設定」時才用這個。
    /// 只要成功讀到 Slot_Inspection 設定，門檻一律用「主程式安全高度 + ZSafeTolerance」，見 <see cref="ResolveZSafeCeiling"/>。
    /// </summary>
    private const double FallbackZSafeHeightMm = 5.0;

    private void SetupYAxisPreMoveCheck()
    {
        // Axes[0]=Y, [1]=X, [2]=ZL, [3]=ZR（對應 configs 的順序）
        if (Axes.Count < 4) return;

        var yAxis = Axes[0];
        var zlAxis = Axes[2];
        var zrAxis = Axes[3];

        yAxis.PreMoveCheck = () =>
        {
            if (!zlAxis.IsConnected)
                return (false, "ZL 軸未連線");

            if (!zrAxis.IsConnected)
                return (false, "ZR 軸未連線");

            if (!zlAxis.IsAbsoluteCoordinateOk)
                return (false, "ZL 軸絕對座標異常");

            if (!zrAxis.IsAbsoluteCoordinateOk)
                return (false, "ZR 軸絕對座標異常");

            // 門檻優先用主程式（Slot_Inspection）的安全高度設定，沒讀到才用 fallback
            var (zlCeil, zlFromCfg) = ResolveZSafeCeiling(
                _safetySnapshot?.ZLSafeHeightCeiling, _safetySnapshot?.ZSafeTolerance);
            var (zrCeil, zrFromCfg) = ResolveZSafeCeiling(
                _safetySnapshot?.ZRSafeHeightCeiling, _safetySnapshot?.ZSafeTolerance);

            if (zlAxis.CurrentPositionMm >= zlCeil)
                return (false, $"ZL 軸高度 {zlAxis.CurrentPositionMm:F2} mm 未低於安全高度門檻 {zlCeil:F2} mm" +
                               $"（{(zlFromCfg ? "主程式設定" : "DeltaAxis 預設值")}）");

            if (zrAxis.CurrentPositionMm >= zrCeil)
                return (false, $"ZR 軸高度 {zrAxis.CurrentPositionMm:F2} mm 未低於安全高度門檻 {zrCeil:F2} mm" +
                               $"（{(zrFromCfg ? "主程式設定" : "DeltaAxis 預設值")}）");

            return (true, string.Empty);
        };
    }

    /// <summary>
    /// 對應驗收需求的兩項Z軸安全檢查（都是警告但仍可繼續，不是強制擋下）：
    /// 1) Y軸目前位置若不在Slot_Inspection設定的已知Slot/取料位置上，調整Z軸要警告
    /// 2) Z軸目標位置若超過Slot_Inspection設定的相機高度（下降過深），要警告
    /// 容許誤差設 0.5mm（浮點數比較需要一點誤差範圍，跟Slot_Inspection實際的Recipe精度量級一致）。
    /// </summary>
    private const double KnownPositionToleranceMm = 0.5;

    private void SetupZAxisExtraMoveWarning()
    {
        if (Axes.Count < 4) return;

        var yAxis = Axes[0];
        var zlAxis = Axes[2];
        var zrAxis = Axes[3];

        zlAxis.ExtraMoveWarning = target => CheckZAxisSafety(yAxis, target, "ZL", () => _safetySnapshot?.ZLCameraHeightLimit);
        zrAxis.ExtraMoveWarning = target => CheckZAxisSafety(yAxis, target, "ZR", () => _safetySnapshot?.ZRCameraHeightLimit);
    }

    private (bool Ok, string Reason) CheckZAxisSafety(
        AxisViewModel yAxis, double zTargetMm, string zAxisName, Func<double?> getDepthLimit)
    {
        if (_safetySnapshot == null)
            return (true, string.Empty); // 還沒讀到Slot_Inspection設定，無法檢查，不擋（也不誤報）

        // 1) Y軸目前位置是否在已知Slot/取料位置上
        bool yAtKnownPosition = _safetySnapshot.KnownYPositions
            .Any(p => Math.Abs(p - yAxis.CurrentPositionMm) <= KnownPositionToleranceMm);
        if (!yAtKnownPosition)
        {
            return (false,
                $"Y軸目前位置 {yAxis.CurrentPositionMm:F3} mm 不在Slot_Inspection設定的已知Slot/取料位置上，" +
                $"此時調整{zAxisName}軸有撞機風險");
        }

        // 2) Z軸目標是否超過設定的相機高度（下降過深）
        double? depthLimit = getDepthLimit();
        if (depthLimit.HasValue && zTargetMm > depthLimit.Value)
        {
            return (false,
                $"{zAxisName}軸目標 {zTargetMm:F3} mm 超過Slot_Inspection設定的相機高度上限 {depthLimit.Value:F3} mm（下降過深）");
        }

        return (true, string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisarmConfigWatcher();
        lock (_reloadLock)
        {
            _reloadDebounce?.Stop();
            _reloadDebounce?.Dispose();
            _reloadDebounce = null;
        }
        foreach (var ax in Axes) ax.Dispose();
        GC.SuppressFinalize(this);
    }
}
