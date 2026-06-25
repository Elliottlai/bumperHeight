using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

    private string _slotInspectionConfigPath =
        @"..\..\..\..\Slot_Inspection\bin\Debug\net8.0-windows\Config";

    /// <summary>
    /// Slot_Inspection主程式的Config資料夾路徑。預設值是這個repo在開發機上的相對位置，
    /// 實際部署到FAE機台上時，請改成Slot_Inspection實際安裝路徑下的Config資料夾。
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

    public MainViewModel()
    {
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

        // 為 Y 軸設定移動前安全檢查：確保 ZL/ZR 已連線、Servo On，且高度低於安全高度 25 mm
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

        LoadSafetyConfigCommand = new RelayCommand(LoadSafetyConfig);

        // 開機嘗試自動讀取一次（路徑不對也不影響其他功能，僅顯示狀態訊息）
        LoadSafetyConfig();
    }

    /// <summary>
    /// 讀取 Slot_Inspection 的安全參考設定（25個Slot Y座標、取料位置、ZL/ZR相機高度）。
    /// 讀取成功後：更新 Y 軸的取料點參考位置，並讓 ZL/ZR 的 ExtraMoveWarning 套用最新數值。
    /// </summary>
    private void LoadSafetyConfig()
    {
        try
        {
            _safetySnapshot = SlotInspectionConfigReader.Load(SlotInspectionConfigPath);

            // 更新 Y 軸的取料點參考位置（用真正讀到的 BarcodePositionY，不再寫死）
            if (Axes.Count > 0)
            {
                double pickPositionY = _safetySnapshot.KnownYPositions.Count > 0
                    ? _safetySnapshot.KnownYPositions[^1] // KnownYPositions 最後一個元素是 BarcodePositionY
                    : 0;
                Axes[0].Config.ReferencePositions.Clear();
                Axes[0].Config.ReferencePositions.Add(new AxisReferencePosition
                {
                    Name = "取料點",
                    PositionMm = pickPositionY,
                    WarnToleranceMm = 5.0
                });
            }

            SafetyConfigStatusMessage =
                $"✔ 已讀取（{_safetySnapshot.LoadedAt:HH:mm:ss}）：" +
                $"已知Y位置{_safetySnapshot.KnownYPositions.Count}點，" +
                $"ZL深度上限{_safetySnapshot.ZLCameraHeightLimit:F2}mm，" +
                $"ZR深度上限{_safetySnapshot.ZRCameraHeightLimit:F2}mm";
        }
        catch (Exception ex)
        {
            _safetySnapshot = null;
            SafetyConfigStatusMessage = $"✘ 讀取失敗：{ex.Message}";
        }
    }

    /// <summary>Y 軸移動前需確保 ZL/ZR 軸皆已連線、Servo On，且當前高度低於安全高度。</summary>
    private const double ZSafeHeightMm = 5.0;

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

            if (zlAxis.CurrentPositionMm >= ZSafeHeightMm)
                return (false, $"ZL 軸高度 {zlAxis.CurrentPositionMm:F2} mm 超過安全高度 {ZSafeHeightMm} mm");

            if (zrAxis.CurrentPositionMm >= ZSafeHeightMm)
                return (false, $"ZR 軸高度 {zrAxis.CurrentPositionMm:F2} mm 超過安全高度 {ZSafeHeightMm} mm");

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
        foreach (var ax in Axes) ax.Dispose();
        GC.SuppressFinalize(this);
    }
}
