using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using PLC_IO.Interfaces;
using PLC_IO.Services;

namespace PLC_IO.ViewModels;

/// <summary>
/// 主畫面 ViewModel — 管理 FX PLC 連線與 X/Y 點位顯示
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private const int XCount = 13;
    private const int YCount = 11;
    private const int RefreshMs = 100;

    private FxPlcCommunicator? _plc;
    private SerialBytesCommunicator? _serial;
    private DispatcherTimer? _refreshTimer;
    private bool _isConnected;
    private string _comPort = "3";
    private string _statusText = "未連線";
    private string _txText = "";
    private string _rxText = "";
    private string _commAlive = "";
    private string _errorLog = "";

    // ── 公開屬性 ──

    public ObservableCollection<IoPointViewModel> XPoints { get; } = [];
    public ObservableCollection<IoPointViewModel> YPoints { get; } = [];

    public string ComPort
    {
        get => _comPort;
        set { _comPort = value; OnPropertyChanged(); }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set { _isConnected = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDisconnected)); }
    }

    public bool IsDisconnected => !_isConnected;

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    /// <summary>最後發送命令</summary>
    public string TxText
    {
        get => _txText;
        private set { if (_txText != value) { _txText = value; OnPropertyChanged(); } }
    }

    /// <summary>最後接收回覆</summary>
    public string RxText
    {
        get => _rxText;
        private set { if (_rxText != value) { _rxText = value; OnPropertyChanged(); } }
    }

    /// <summary>通訊狀態</summary>
    public string CommAlive
    {
        get => _commAlive;
        private set { if (_commAlive != value) { _commAlive = value; OnPropertyChanged(); } }
    }

    /// <summary>錯誤記錄</summary>
    public string ErrorLog
    {
        get => _errorLog;
        private set { if (_errorLog != value) { _errorLog = value; OnPropertyChanged(); } }
    }

    // ── 命令 ──

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ToggleYCommand { get; }

    // ── 建構 ──

    public MainViewModel()
    {
        for (int i = 0; i < XCount; i++)
            XPoints.Add(new IoPointViewModel(
                IoPointViewModel.ToOctalAddress("X", i), i, isOutput: false));

        for (int i = 0; i < YCount; i++)
            YPoints.Add(new IoPointViewModel(
                IoPointViewModel.ToOctalAddress("Y", i), i, isOutput: true));

        ConnectCommand = new RelayCommand(_ => Connect(), _ => IsDisconnected);
        DisconnectCommand = new RelayCommand(_ => Disconnect(), _ => IsConnected);
        ToggleYCommand = new RelayCommand(param => ToggleY(param), _ => IsConnected);
    }

    // ── 連線/斷線 ──

    private void Connect()
    {
        try
        {
            _serial = new SerialBytesCommunicator(
                $"COM{ComPort}", 115200, 7, Parity.Even, StopBits.One);

            _plc = new FxPlcCommunicator(_serial);

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RefreshMs) };
            _refreshTimer.Tick += OnRefreshTick;
            _refreshTimer.Start();

            IsConnected = true;
            StatusText = $"已連線 COM{ComPort}";
        }
        catch (Exception ex)
        {
            StatusText = $"連線失敗: {ex.Message}";
        }
    }

    private void Disconnect()
    {
        _refreshTimer?.Stop();
        _refreshTimer = null;
        _plc?.Dispose();
        _plc = null;
        _serial = null;

        IsConnected = false;
        StatusText = "已斷線";
        TxText = "";
        RxText = "";
        CommAlive = "";
        ErrorLog = "";

        foreach (var x in XPoints) x.Status = false;
        foreach (var y in YPoints) y.Status = false;
    }

    // ── 週期更新 ──

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (_plc is null || !_plc.IsConnected) return;

        try
        {
            for (int i = 0; i < XCount; i++)
                XPoints[i].Status = _plc.GetX(i);

            for (int i = 0; i < YCount; i++)
                YPoints[i].Status = _plc.GetY(i);

            var status = $"COM{ComPort} | Dog:{_plc.DogValue} | {_plc.GetStatusSummary()}";
            if (StatusText != status) StatusText = status;

            // 只在內容變化時才更新 UI
            TxText = _plc.LastTxText;
            RxText = _plc.LastRxText;
            CommAlive = $"[{_plc.CommAlive}]  TX:{_plc.TxCount}  RX:{_plc.RxCount}  ERR:{_plc.ErrCount}";
            ErrorLog = _plc.ErrorLog;
        }
        catch (Exception ex)
        {
            Disconnect();
            StatusText = $"通訊中斷: {ex.Message}";
        }
    }

    // ── Y 輸出切換 ──

    private void ToggleY(object? param)
    {
        if (_plc is null || param is not IoPointViewModel point) return;
        _plc.SetY(point.Index, !point.Status);
    }

    // ── INotifyPropertyChanged ──

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        Disconnect();
    }
}