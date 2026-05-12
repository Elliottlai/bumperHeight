using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DeltaAxis_RS485.Models;

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

    public MainViewModel()
    {
        // ============================
        // 軸組態（實務上可從 JSON 讀取）
        // ============================
        var configs = new List<AxisConfig>
        {
            new() { Name = "Y軸", PortName = "COM12", SlaveId = 1 },
            new() { Name = "X軸", PortName = "COM13", SlaveId = 1 },
            new() { Name = "ZL軸", PortName = "COM14", SlaveId = 1 },
            new() { Name = "ZR軸", PortName = "COM15", SlaveId = 1 },
        };

        foreach (var cfg in configs)
            Axes.Add(new AxisViewModel(cfg));

        if (Axes.Count > 0)
            SelectedAxis = Axes[0];

        // 為 Y 軸設定移動前安全檢查：確保 ZL/ZR 已連線、Servo On，且高度低於安全高度 25 mm
        SetupYAxisPreMoveCheck();

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
    }

    /// <summary>Y 軸移動前需確保 ZL/ZR 軸皆已連線、Servo On，且當前高度低於安全高度。</summary>
    private const double ZSafeHeightMm = 25.0;

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