using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DeltaAxis_RS485.Interfaces;
using DeltaAxis_RS485.Models;
using DeltaAxis_RS485.Services;

namespace DeltaAxis_RS485.ViewModels;

public class AxisViewModel : INotifyPropertyChanged, IDisposable
{
    private AsdaB3Controller? _controller;
    private IModbusRtuClient? _modbus;
    private bool _disposed;

    public AxisConfig Config { get; }
    public string DisplayName => $"{Config.Name} ({Config.PortName})";

    // ============================
    //  連線狀態
    // ============================
    private bool _isConnected;
    public bool IsConnected { get => _isConnected; private set => SetField(ref _isConnected, value); }

    // ============================
    //  運動參數
    // ============================
    private int _targetSpeed;
    public int TargetSpeed { get => _targetSpeed; set => SetField(ref _targetSpeed, value); }

    private int _accelerationTime;
    public int AccelerationTime { get => _accelerationTime; set => SetField(ref _accelerationTime, value); }

    private int _decelerationTime;
    public int DecelerationTime { get => _decelerationTime; set => SetField(ref _decelerationTime, value); }

    private int _delayTime;
    public int DelayTime { get => _delayTime; set => SetField(ref _delayTime, value); }

    private double _puuPerMm;
    public double PuuPerMm { get => _puuPerMm; set => SetField(ref _puuPerMm, value); }

    private int _inPositionTimeout;
    public int InPositionTimeout { get => _inPositionTimeout; set => SetField(ref _inPositionTimeout, value); }

    // ============================
    //  移動目標
    // ============================
    private double _targetPositionMm;
    public double TargetPositionMm { get => _targetPositionMm; set => SetField(ref _targetPositionMm, value); }

    // ============================
    //  狀態
    // ============================
    private bool _isServoOn;
    public bool IsServoOn { get => _isServoOn; private set => SetField(ref _isServoOn, value); }

    private bool _hasAlarm;
    public bool HasAlarm { get => _hasAlarm; private set => SetField(ref _hasAlarm, value); }

    private bool _absOk;
    public bool AbsOk { get => _absOk; private set => SetField(ref _absOk, value); }

    private double _currentPositionMm;
    public double CurrentPositionMm { get => _currentPositionMm; private set => SetField(ref _currentPositionMm, value); }

    private int _multiTurnPosition;
    public int MultiTurnPosition { get => _multiTurnPosition; private set => SetField(ref _multiTurnPosition, value); }

    private int _singleTurnPosition;
    public int SingleTurnPosition { get => _singleTurnPosition; private set => SetField(ref _singleTurnPosition, value); }

    private string _statusMessage = "未連線";
    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }

    // ============================
    //  Commands
    // ============================
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ServoOnCommand { get; }
    public ICommand ServoOffCommand { get; }
    public ICommand ClearAlarmCommand { get; }
    public ICommand ApplySettingsCommand { get; }
    public ICommand MoveToPositionCommand { get; }
    public ICommand RebuildAbsOriginCommand { get; }
    public ICommand RefreshStatusCommand { get; }

    public AxisViewModel(AxisConfig config)
    {
        Config = config;

        TargetSpeed = config.Motion.TargetSpeed;
        AccelerationTime = config.Motion.AccelerationTime;
        DecelerationTime = config.Motion.DecelerationTime;
        DelayTime = config.Motion.DelayTime;
        PuuPerMm = config.Motion.PuuPerMm;
        InPositionTimeout = config.Motion.InPositionTimeout;

        ConnectCommand = new RelayCommand(DoConnect, () => !IsConnected);
        DisconnectCommand = new RelayCommand(DoDisconnect, () => IsConnected);
        ServoOnCommand = new RelayCommand(DoServoOn, () => IsConnected && !IsServoOn);
        ServoOffCommand = new RelayCommand(DoServoOff, () => IsConnected && IsServoOn);
        ClearAlarmCommand = new RelayCommand(DoClearAlarm, () => IsConnected);
        ApplySettingsCommand = new RelayCommand(DoApplySettings, () => IsConnected);
        MoveToPositionCommand = new RelayCommand(DoMoveToPosition, () => IsConnected && IsServoOn);
        RebuildAbsOriginCommand = new RelayCommand(DoRebuildAbsOrigin, () => IsConnected);
        RefreshStatusCommand = new RelayCommand(DoRefreshStatus, () => IsConnected);
    }

    private void DoConnect()
    {
        try
        {
            var connSettings = new ConnectionSettings
            {
                PortName = Config.PortName,
                BaudRate = Config.BaudRate,
                SlaveId = Config.SlaveId
            };
            var motionSettings = new MotionSettings
            {
                TargetSpeed = TargetSpeed,
                AccelerationTime = AccelerationTime,
                DecelerationTime = DecelerationTime,
                DelayTime = DelayTime,
                InPositionTimeout = InPositionTimeout,
                PuuPerMm = PuuPerMm
            };

            _modbus = new ModbusRtuClient();
            _controller = new AsdaB3Controller(_modbus, connSettings, motionSettings);
            _controller.Connect();

            IsConnected = true;
            StatusMessage = $"{Config.PortName} 已連線";
        }
        catch (Exception ex)
        {
            StatusMessage = $"連線失敗: {ex.Message}";
        }
    }

    private void DoDisconnect()
    {
        try
        {
            _controller?.Dispose();
            _controller = null;
            _modbus = null;
            IsConnected = false;
            IsServoOn = false;
            StatusMessage = "已斷線";
        }
        catch (Exception ex)
        {
            StatusMessage = $"斷線失敗: {ex.Message}";
        }
    }

    private void DoServoOn() => RunSafe(() =>
    {
        _controller!.ServoOn();
        IsServoOn = true;
        StatusMessage = "已激磁";
    });

    private void DoServoOff() => RunSafe(() =>
    {
        _controller!.ServoOff();
        IsServoOn = false;
        StatusMessage = "已解磁";
    });

    private void DoClearAlarm() => RunSafe(() =>
    {
        _controller!.ClearAlarm();
        HasAlarm = false;
        StatusMessage = "警報已清除";
    });

    private void DoApplySettings() => RunSafe(() =>
    {
        _controller!.TargetSpeed = TargetSpeed;
        _controller.AccelerationTime = AccelerationTime;
        _controller.DecelerationTime = DecelerationTime;
        _controller.DelayTime = DelayTime;
        _controller.InPositionTimeout = InPositionTimeout;
        _controller.PuuPerMm = PuuPerMm;
        _controller.ApplySettings();
        StatusMessage = "參數已寫入";
    });

    private void DoMoveToPosition() => RunSafe(() =>
    {
        _controller!.MoveToPositionMm(TargetPositionMm);
        DoRefreshStatus();
        StatusMessage = $"已移動至 {TargetPositionMm:F2} mm";
    });

    private void DoRebuildAbsOrigin() => RunSafe(() =>
    {
        _controller!.RebuildAbsoluteOrigin();
        AbsOk = _controller.AbsOk();
        StatusMessage = "Abs origin 已重建";
    });

    private void DoRefreshStatus() => RunSafe(() =>
    {
        HasAlarm = _controller!.HasAlarm();
        IsServoOn = _controller.IsServoOn;
        AbsOk = _controller.AbsOk();
        MultiTurnPosition = _controller.GetMultiTurnPosition();
        SingleTurnPosition = _controller.GetSingleTurnPosition();
        CurrentPositionMm = MultiTurnPosition / PuuPerMm;
    });

    private void RunSafe(Action action)
    {
        try { action(); }
        catch (Exception ex) { StatusMessage = $"錯誤: {ex.Message}"; }
    }

    // ============================
    //  INotifyPropertyChanged
    // ============================
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
        _controller?.Dispose();
        GC.SuppressFinalize(this);
    }
}