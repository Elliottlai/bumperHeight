using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using DeltaAxis_RS485.Interfaces;
using DeltaAxis_RS485.Models;
using DeltaAxis_RS485.Services;

namespace DeltaAxis_RS485.ViewModels;

public class AxisViewModel : INotifyPropertyChanged, IDisposable
{
    private AsdaB3Controller? _controller;
    private IModbusRtuClient? _modbus;
    private bool _disposed;
    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    /// <summary>輪詢間隔 (ms)</summary>
    public int PollingIntervalMs { get; set; } = 100;

    /// <summary>Modbus 通訊互斥鎖，確保 polling 與指令不會同時存取 serial port</summary>
    private readonly object _modbusLock = new();

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
    //  極限 / 原點 / 煞車 狀態
    // ============================
    private bool _isPositiveLimitActive;
    public bool IsPositiveLimitActive { get => _isPositiveLimitActive; private set => SetField(ref _isPositiveLimitActive, value); }

    private bool _isNegativeLimitActive;
    public bool IsNegativeLimitActive { get => _isNegativeLimitActive; private set => SetField(ref _isNegativeLimitActive, value); }

    private bool _isBrakeReleased;
    public bool IsBrakeReleasedState { get => _isBrakeReleased; private set => SetField(ref _isBrakeReleased, value); }

    // ============================
    //  驅動器狀態 (P0.046)
    // ============================
    private DriverStatusFlags _driverStatus;
    public DriverStatusFlags DriverStatus { get => _driverStatus; private set => SetField(ref _driverStatus, value); }

    public bool IsSrdy => DriverStatus.HasFlag(DriverStatusFlags.ServoReady);
    public bool IsSon => DriverStatus.HasFlag(DriverStatusFlags.ServoOn);
    public bool IsZeroSpeed => DriverStatus.HasFlag(DriverStatusFlags.ZeroSpeed);
    public bool IsTargetSpeedReached => DriverStatus.HasFlag(DriverStatusFlags.TargetSpeedReached);
    public bool IsTargetPositionReached => DriverStatus.HasFlag(DriverStatusFlags.TargetPositionReached);
    public bool IsTorqueLimited => DriverStatus.HasFlag(DriverStatusFlags.TorqueLimited);
    public bool IsAlarm => DriverStatus.HasFlag(DriverStatusFlags.Alarm);
    public bool IsBrakeReleased => DriverStatus.HasFlag(DriverStatusFlags.BrakeRelease);
    public bool IsHomeComplete => DriverStatus.HasFlag(DriverStatusFlags.HomeComplete);
    public bool IsOverloadWarning => DriverStatus.HasFlag(DriverStatusFlags.OverloadWarning);
    public bool IsWarning => DriverStatus.HasFlag(DriverStatusFlags.Warning);

    // ============================
    //  絕對座標狀態 (P0.050)
    // ============================
    private AbsoluteStatusFlags _absoluteStatus;
    public AbsoluteStatusFlags AbsoluteStatus { get => _absoluteStatus; private set => SetField(ref _absoluteStatus, value); }

    public bool IsAbsolutePositionOk => !AbsoluteStatus.HasFlag(AbsoluteStatusFlags.AbsolutePosition);
    public bool IsBatteryVoltageOk => !AbsoluteStatus.HasFlag(AbsoluteStatusFlags.BatteryVoltage);
    public bool IsAbsoluteRevolutionOk => !AbsoluteStatus.HasFlag(AbsoluteStatusFlags.AbsoluteRevolution);
    public bool IsPuuStatusOk => !AbsoluteStatus.HasFlag(AbsoluteStatusFlags.Puu);
    public bool IsAbsoluteCoordinateOk => !AbsoluteStatus.HasFlag(AbsoluteStatusFlags.AbsoluteCoordinate);

    // ============================
    // 監視變數映射 (P0.009~P0.012)
    // ============================
    private int _feedbackPositionPuu;
    public int FeedbackPositionPuu { get => _feedbackPositionPuu; private set => SetField(ref _feedbackPositionPuu, value); }

    private ushort _alarmCodeDecimal;
    public ushort AlarmCodeDecimal { get => _alarmCodeDecimal; private set => SetField(ref _alarmCodeDecimal, value); }

    private ushort _diStatusIntegrated;
    public ushort DiStatusIntegrated { get => _diStatusIntegrated; private set => SetField(ref _diStatusIntegrated, value); }

    private ushort _doStatusHardware;
    public ushort DoStatusHardware { get => _doStatusHardware; private set => SetField(ref _doStatusHardware, value); }

    public string DiStatusIntegratedHex => $"0x{DiStatusIntegrated:X4}";
    public string DoStatusHardwareHex => $"0x{DoStatusHardware:X4}";

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
    public ICommand HomingCommand { get; }
    public ICommand BrakeReleaseCommand { get; }
    public ICommand BrakeLockCommand { get; }
    public ICommand StopCommand { get; }

    // ============================
    //  Jog 增量移動
    // ============================
    public ICommand JogPositive0003Command { get; }
    public ICommand JogNegative0003Command { get; }
    public ICommand JogPositive001Command { get; }
    public ICommand JogNegative001Command { get; }
    public ICommand JogPositive01Command { get; }
    public ICommand JogNegative01Command { get; }
    public ICommand JogPositive1Command { get; }
    public ICommand JogNegative1Command { get; }
    public ICommand JogPositive10Command { get; }
    public ICommand JogNegative10Command { get; }

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
        MoveToPositionCommand = new RelayCommand(DoMoveToPosition, () => IsConnected && IsServoOn);
        HomingCommand = new RelayCommand(DoHoming, () => IsConnected && IsServoOn);
        ClearAlarmCommand = new RelayCommand(DoClearAlarm, () => IsConnected);
        ApplySettingsCommand = new RelayCommand(DoApplySettings, () => IsConnected);
        RebuildAbsOriginCommand = new RelayCommand(DoRebuildAbsOrigin, () => IsConnected);
        RefreshStatusCommand = new RelayCommand(DoRefreshStatus, () => IsConnected);
        BrakeReleaseCommand = new RelayCommand(DoBrakeRelease, () => IsConnected);
        BrakeLockCommand = new RelayCommand(DoBrakeLock, () => IsConnected);

        // Jog 增量移動指令
        JogPositive0003Command = new RelayCommand(() => DoJog(0.003), () => IsConnected && IsServoOn);
        JogNegative0003Command = new RelayCommand(() => DoJog(-0.003), () => IsConnected && IsServoOn);
        JogPositive001Command = new RelayCommand(() => DoJog(0.01), () => IsConnected && IsServoOn);
        JogNegative001Command = new RelayCommand(() => DoJog(-0.01), () => IsConnected && IsServoOn);
        JogPositive01Command = new RelayCommand(() => DoJog(0.1), () => IsConnected && IsServoOn);
        JogNegative01Command = new RelayCommand(() => DoJog(-0.1), () => IsConnected && IsServoOn);
        JogPositive1Command = new RelayCommand(() => DoJog(1.0), () => IsConnected && IsServoOn);
        JogNegative1Command = new RelayCommand(() => DoJog(-1.0), () => IsConnected && IsServoOn);
        JogPositive10Command = new RelayCommand(() => DoJog(10.0), () => IsConnected && IsServoOn);
        JogNegative10Command = new RelayCommand(() => DoJog(-10.0), () => IsConnected && IsServoOn);
        StopCommand = new RelayCommand(DoStop, () => IsConnected);
    }

    // ============================
    //  即時輪詢
    // ============================

    /// <summary>啟動背景狀態輪詢</summary>
    private void StartPolling()
    {
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;
        var dispatcher = System.Windows.Application.Current.Dispatcher;

        _pollTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(PollingIntervalMs));
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    DriverSnapshot snapshot;
                    // 用 lock 確保不會和指令同時存取 serial port
                    lock (_modbusLock)
                    {
                        snapshot = _controller!.ReadSnapshot();
                    }
                    dispatcher.Invoke(() => ApplySnapshot(snapshot));
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    // 通訊異常時靜默跳過
                }
            }
        }, token);
    }

    /// <summary>停止背景狀態輪詢</summary>
    private void StopPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
        _pollTask = null;
    }

    /// <summary>將快照套用至 ViewModel 屬性</summary>
    private void ApplySnapshot(DriverSnapshot snapshot)
    {
        DriverStatus = snapshot.DriverStatus;
        HasAlarm = snapshot.HasAlarm;
        AbsOk = snapshot.AbsOk;
        IsBrakeReleasedState = snapshot.IsBrakeReleased;
        IsServoOn = snapshot.IsServoOn;

        MultiTurnPosition = snapshot.MultiTurnPosition;
        SingleTurnPosition = snapshot.SingleTurnPosition;
        CurrentPositionMm = snapshot.FeedbackPositionPuu / PuuPerMm;

        AbsoluteStatus = snapshot.AbsoluteStatus;

        IsPositiveLimitActive = (snapshot.DiStatus & AsdaB3RegisterMap.DI7_Bit) == 0;
        IsNegativeLimitActive = (snapshot.DiStatus & AsdaB3RegisterMap.DI8_Bit) == 0;

        // 監視變數映射
        FeedbackPositionPuu = snapshot.FeedbackPositionPuu;
        AlarmCodeDecimal = snapshot.AlarmCodeDecimal;
        DiStatusIntegrated = snapshot.DiStatusIntegrated;
        DoStatusHardware = snapshot.DoStatusHardware;

        // DEBUG: 顯示 P0.046 原始值，確認 Bit1 (SON) 狀態
        StatusMessage = $"P0.046=0x{(ushort)snapshot.DriverStatus:X4} SON={snapshot.IsServoOn}";

        NotifyStatusProperties();
        OnPropertyChanged(nameof(DiStatusIntegratedHex));
        OnPropertyChanged(nameof(DoStatusHardwareHex));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>通知所有衍生狀態屬性變更</summary>
    private void NotifyStatusProperties()
    {
        OnPropertyChanged(nameof(IsSrdy));
        OnPropertyChanged(nameof(IsSon));
        OnPropertyChanged(nameof(IsZeroSpeed));
        OnPropertyChanged(nameof(IsTargetSpeedReached));
        OnPropertyChanged(nameof(IsTargetPositionReached));
        OnPropertyChanged(nameof(IsTorqueLimited));
        OnPropertyChanged(nameof(IsAlarm));
        OnPropertyChanged(nameof(IsBrakeReleased));
        OnPropertyChanged(nameof(IsHomeComplete));
        OnPropertyChanged(nameof(IsOverloadWarning));
        OnPropertyChanged(nameof(IsWarning));
        OnPropertyChanged(nameof(IsAbsolutePositionOk));
        OnPropertyChanged(nameof(IsBatteryVoltageOk));
        OnPropertyChanged(nameof(IsAbsoluteRevolutionOk));
        OnPropertyChanged(nameof(IsPuuStatusOk));
        OnPropertyChanged(nameof(IsAbsoluteCoordinateOk));
        OnPropertyChanged(nameof(DI1_ServoOn));
        OnPropertyChanged(nameof(DI2_PulseClear));
        OnPropertyChanged(nameof(DI3_TorqueSel_Bit0));
        OnPropertyChanged(nameof(DI4_TorqueSel_Bit1));
        OnPropertyChanged(nameof(DI5_AlarmClear));
        OnPropertyChanged(nameof(DI6_NegLimit));
        OnPropertyChanged(nameof(DI7_PosLimit));
        OnPropertyChanged(nameof(DI8_None));
        OnPropertyChanged(nameof(DI9_Home));
        OnPropertyChanged(nameof(DI10_None));
        OnPropertyChanged(nameof(DI11_None));
        OnPropertyChanged(nameof(DI12_None));
        OnPropertyChanged(nameof(DI13_None));
    }

    // ============================
    //  Command 實作
    // ============================

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

            // 連線成功後啟動即時輪詢
            StartPolling();
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
            StopPolling();
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
        StatusMessage = "已激磁";
    });

    private void DoServoOff() => RunSafe(() =>
    {
        _controller!.ServoOff();
        StatusMessage = "已解磁";
    });

    private void DoClearAlarm() => RunSafe(() =>
    {
        _controller!.ClearAlarm();
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
        StatusMessage = $"已移動至 {TargetPositionMm:F2} mm";
    });

    private void DoRebuildAbsOrigin() => RunSafe(() =>
    {
        _controller!.RebuildAbsoluteOrigin();
        StatusMessage = "Abs origin 已重建";
    });

    private void DoHoming() => RunSafe(() =>
    {
        StatusMessage = "原點復歸中...";
        _controller!.ExecuteHoming();
        StatusMessage = "原點復歸完成";
    });

    private void DoBrakeRelease() => RunSafe(() =>
    {
        _controller!.SetBrake(release: true);
        StatusMessage = "電磁煞車已釋放";
    });

    private void DoBrakeLock() => RunSafe(() =>
    {
        _controller!.SetBrake(release: false);
        StatusMessage = "電磁煞車已鎖住";
    });

    private void DoRefreshStatus()
    {
        RunSafe(() =>
        {
            var snapshot = _controller!.ReadSnapshot();
            ApplySnapshot(snapshot);
        });
    }

    private void DoStop() => RunSafe(() =>
    {
        _controller!.Stop();
        StatusMessage = "已停止";
    });

    /// <summary>
    /// 安全執行指令：用 lock 確保和 polling 互斥存取 Modbus，
    /// 執行完畢後立即讀取最新狀態更新 UI。
    /// </summary>
    private void RunSafe(Action action)
    {
        try
        {
            lock (_modbusLock)
            {
                action();
            }

            // 指令完成後立即讀取最新硬體狀態
            DriverSnapshot snapshot;
            lock (_modbusLock)
            {
                snapshot = _controller!.ReadSnapshot();
            }
            ApplySnapshot(snapshot);
        }
        catch (Exception ex)
        {
            StatusMessage = $"錯誤: {ex.Message}";
        }

        CommandManager.InvalidateRequerySuggested();
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

    // ============================
    //  DI 整合狀態各 bit (P0.011 MonitorCode 0x27)
    // ============================
    public bool DI1_ServoOn => (DiStatusIntegrated & 0x0001) != 0;
    public bool DI2_PulseClear => (DiStatusIntegrated & 0x0002) != 0;
    public bool DI3_TorqueSel_Bit0 => (DiStatusIntegrated & 0x0004) != 0;
    public bool DI4_TorqueSel_Bit1 => (DiStatusIntegrated & 0x0008) != 0;
    public bool DI5_AlarmClear => (DiStatusIntegrated & 0x0010) != 0;
    public bool DI6_NegLimit => (DiStatusIntegrated & 0x0020) != 0;
    public bool DI7_PosLimit => (DiStatusIntegrated & 0x0040) != 0;
    public bool DI8_None => (DiStatusIntegrated & 0x0080) != 0;
    public bool DI9_Home => (DiStatusIntegrated & 0x0100) != 0;
    public bool DI10_None => (DiStatusIntegrated & 0x0200) != 0;
    public bool DI11_None => (DiStatusIntegrated & 0x0400) != 0;
    public bool DI12_None => (DiStatusIntegrated & 0x0800) != 0;
    public bool DI13_None => (DiStatusIntegrated & 0x1000) != 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopPolling();
        _controller?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void DoJog(double incrementMm) => RunSafe(() =>
    {
        double target = CurrentPositionMm + incrementMm;
        _controller!.MoveToPositionMm(target);
        StatusMessage = $"Jog {incrementMm:+0.000;-0.000} mm → {target:F3} mm";
    });


}