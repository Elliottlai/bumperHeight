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
    //  運動中旗標
    // ============================
    private bool _isMoving;
    public bool IsMoving { get => _isMoving; private set => SetField(ref _isMoving, value); }

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
    public double TargetPositionMm
    {
        get => _targetPositionMm;
        set { if (SetField(ref _targetPositionMm, value)) RaiseTargetUpperLimitState(); }
    }

    /// <summary>
    /// 定位控制的目標上限（mm）。移動 / Jog 的目標位置超過這個值就會被擋下（不移動），
    /// 且介面會在「移動」按鈕旁顯示紅字提示。null＝不限制。
    /// 只由 MainViewModel 從設定檔（DeltaAxisSettings.json）依軸名載入，介面上不提供修改。
    /// </summary>
    private double? _targetUpperLimitMm;
    public double? TargetUpperLimitMm
    {
        get => _targetUpperLimitMm;
        set { if (SetField(ref _targetUpperLimitMm, value)) RaiseTargetUpperLimitState(); }
    }

    private void RaiseTargetUpperLimitState()
    {
        OnPropertyChanged(nameof(IsTargetOverUpperLimit));
        OnPropertyChanged(nameof(TargetUpperLimitMessage));
        // 讓「移動」按鈕在打字當下就即時反映可否按（不用等游標離開欄位觸發 RequerySuggested）
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>目前輸入的目標位置是否超過設定檔給的上限。</summary>
    public bool IsTargetOverUpperLimit
        => _targetUpperLimitMm is double lim && _targetPositionMm > lim;

    /// <summary>超過上限時要顯示在「移動」按鈕旁的紅字；沒超過時為空字串。</summary>
    public string TargetUpperLimitMessage
        => IsTargetOverUpperLimit
            ? $"超過上限 {_targetUpperLimitMm!.Value:0.###} mm"
            : "";

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


    public string AlarmCodeHex => $"0x{AlarmCodeDecimal:X4}";

    // ============================
    //  Y 軸移動前安全前置檢查
    //  回傳 (true, "") 表示允許移動；(false, reason) 表示禁止並附上原因
    // ============================
    public Func<(bool Ok, string Reason)>? PreMoveCheck { get; set; }

    // ============================
    //  目標位置額外警告檢查（例如跨軸讀取Y軸位置、比對Slot_Inspection設定的相機高度）
    //  由 MainViewModel 依需要設定。回傳 (true, "") 表示無風險；(false, reason) 表示有風險，
    //  會跳出確認視窗顯示 reason，使用者選擇是否仍要繼續（不是強制擋下）。
    // ============================
    public Func<double, (bool Ok, string Reason)>? ExtraMoveWarning { get; set; }

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

    // ============================
    //  Jog 按鈕文字：依軸組態（Config.PositiveDirectionLabel/NegativeDirectionLabel）
    //  顯示實際物理方向意義（例如「+10 下降」），避免FAE誤判方向造成撞機。
    //  未設定方向說明時，維持原本的「+10」「-10」純數學正負號顯示。
    // ============================
    private string JogLabel(double incrementMm)
    {
        string sign = incrementMm >= 0 ? "+" : "-";
        string number = Math.Abs(incrementMm).ToString("0.###");
        string dirWord = incrementMm >= 0 ? Config.PositiveDirectionLabel : Config.NegativeDirectionLabel;
        return string.IsNullOrEmpty(dirWord) ? $"{sign}{number}" : $"{sign}{number} {dirWord}";
    }

    public string JogPositive0003Label => JogLabel(0.003);
    public string JogNegative0003Label => JogLabel(-0.003);
    public string JogPositive001Label => JogLabel(0.01);
    public string JogNegative001Label => JogLabel(-0.01);
    public string JogPositive01Label => JogLabel(0.1);
    public string JogNegative01Label => JogLabel(-0.1);
    public string JogPositive1Label => JogLabel(1.0);
    public string JogNegative1Label => JogLabel(-1.0);
    public string JogPositive10Label => JogLabel(10.0);
    public string JogNegative10Label => JogLabel(-10.0);

    public AxisViewModel(AxisConfig config)
    {
        Config = config;

        TargetSpeed = config.Motion.TargetSpeed;
        AccelerationTime = config.Motion.AccelerationTime;
        DecelerationTime = config.Motion.DecelerationTime;
        DelayTime = config.Motion.DelayTime;
        PuuPerMm = config.Motion.PuuPerMm;
        InPositionTimeout = config.Motion.InPositionTimeout;

        ConnectCommand = new RelayCommand(DoConnect, () => !IsConnected && !IsMoving);
        DisconnectCommand = new RelayCommand(DoDisconnect, () => IsConnected && !IsMoving);
        ServoOnCommand = new RelayCommand(DoServoOn, () => IsConnected && !IsServoOn && !IsMoving);
        ServoOffCommand = new RelayCommand(DoServoOff, () => IsConnected && IsServoOn && !IsMoving);
        MoveToPositionCommand = new RelayCommand(DoMoveToPosition,
            () => IsConnected && IsServoOn && !IsMoving && !IsTargetOverUpperLimit);
        HomingCommand = new RelayCommand(DoHoming, () => IsConnected && IsServoOn && !IsMoving);
        ClearAlarmCommand = new RelayCommand(DoClearAlarm, () => IsConnected && !IsMoving);
        ApplySettingsCommand = new RelayCommand(DoApplySettings, () => IsConnected && !IsMoving);
        RebuildAbsOriginCommand = new RelayCommand(DoRebuildAbsOrigin, () => IsConnected && !IsMoving);
        RefreshStatusCommand = new RelayCommand(DoRefreshStatus, () => IsConnected && !IsMoving);
        BrakeReleaseCommand = new RelayCommand(DoBrakeRelease, () => IsConnected && !IsMoving);
        BrakeLockCommand = new RelayCommand(DoBrakeLock, () => IsConnected && !IsMoving);

        // Jog 增量移動指令
        JogPositive0003Command = new RelayCommand(() => DoJog(0.003), () => IsConnected && IsServoOn && !IsMoving);
        JogNegative0003Command = new RelayCommand(() => DoJog(-0.003), () => IsConnected && IsServoOn && !IsMoving);
        JogPositive001Command = new RelayCommand(() => DoJog(0.01), () => IsConnected && IsServoOn && !IsMoving);
        JogNegative001Command = new RelayCommand(() => DoJog(-0.01), () => IsConnected && IsServoOn && !IsMoving);
        JogPositive01Command = new RelayCommand(() => DoJog(0.1), () => IsConnected && IsServoOn && !IsMoving);
        JogNegative01Command = new RelayCommand(() => DoJog(-0.1), () => IsConnected && IsServoOn && !IsMoving);
        JogPositive1Command = new RelayCommand(() => DoJog(1.0), () => IsConnected && IsServoOn && !IsMoving);
        JogNegative1Command = new RelayCommand(() => DoJog(-1.0), () => IsConnected && IsServoOn && !IsMoving);
        JogPositive10Command = new RelayCommand(() => DoJog(10.0), () => IsConnected && IsServoOn && !IsMoving);
        JogNegative10Command = new RelayCommand(() => DoJog(-10.0), () => IsConnected && IsServoOn && !IsMoving);
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

        // 運動中偵測：當位置到達且零速度時自動清除 IsMoving
        if (IsMoving && IsTargetPositionReached && IsZeroSpeed)
        {
            IsMoving = false;
            StatusMessage = "到位完成";
        }

        // 運動中若發生警報，也自動清除 IsMoving
        if (IsMoving && IsAlarm)
        {
            IsMoving = false;
            StatusMessage = $"運動中斷 - 警報 0x{AlarmCodeDecimal:X4}";
        }

        if (!IsMoving)
        {
            StatusMessage = $"P0.046=0x{(ushort)snapshot.DriverStatus:X4} SON={snapshot.IsServoOn}";
        }

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
        _controller!.ClearAlarm();
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

    /// <summary>
    /// 觸發移動（不阻塞等待到位），由 polling 偵測 TPOS+ZSPD 自動清除 IsMoving
    /// </summary>
    private void DoMoveToPosition()
    {
        if (!RunPreMoveCheck()) return;
        if (!WithinTargetUpperLimit(TargetPositionMm)) return;
        if (!ConfirmTargetSafety(TargetPositionMm)) return;
        RunSafe(() =>
        {
            int puu = _controller!.MmToPuu(TargetPositionMm);
            _controller.UpdatePr1TargetPosition(puu);
            _controller.TriggerPr1();
            IsMoving = true;
            StatusMessage = $"移動中 → {TargetPositionMm:F2} mm";
        });
    }

    private void DoRebuildAbsOrigin() => RunSafe(() =>
    {
        _controller!.RebuildAbsoluteOrigin();
        StatusMessage = "Abs origin 已重建";
    });

    private void DoHoming()
    {
        if (!RunPreMoveCheck()) return;
        RunSafe(() =>
        {
            // 觸發 Homing PR#0，不阻塞等待
            _modbus!.WriteRegister(AsdaB3RegisterMap.P5_007_PrTrigger, 0);
            IsMoving = true;
            StatusMessage = "原點賦歸中...";
        });
    }

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
        IsMoving = false;
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
            IsMoving = false;
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

    /// <summary>
    /// Jog 增量移動：只觸發 PR，不阻塞等待到位
    /// </summary>
    private void DoJog(double incrementMm)
    {
        if (!RunPreMoveCheck()) return;
        double target = CurrentPositionMm + incrementMm;
        if (!WithinTargetUpperLimit(target)) return;
        if (!ConfirmTargetSafety(target)) return;
        RunSafe(() =>
        {
            int puu = _controller!.MmToPuu(target);
            _controller.UpdatePr1TargetPosition(puu);
            _controller.TriggerPr1();
            IsMoving = true;
            StatusMessage = $"Jog {incrementMm:+0.000;-0.000} mm → {target:F3} mm";
        });
    }

    /// <summary>
    /// 檢查目標位置是否在使用者設定的上限內。
    /// 未設上限（null）直接通過；超過上限則寫 StatusMessage 並回傳 false（呼叫端不移動）。
    /// </summary>
    private bool WithinTargetUpperLimit(double targetMm)
    {
        if (_targetUpperLimitMm is double lim && targetMm > lim)
        {
            StatusMessage = $"目標 {targetMm:F3} mm 超過設定上限 {lim:F3} mm，已阻擋";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 執行 PreMoveCheck；若沒有設定則直接通過。
    /// 回傳 true 表示可以移動，false 表示禁止移動。
    /// </summary>
    private bool RunPreMoveCheck()
    {
        if (PreMoveCheck == null) return true;
        var (ok, reason) = PreMoveCheck();
        if (!ok)
        {
            StatusMessage = $"安全檢查未通過: {reason}";
        }
        return ok;
    }

    /// <summary>
    /// 移動前的「警告但仍可繼續」檢查：
    /// 1) 目標位置是否接近 Config.ReferencePositions 裡設定的已知參考位置（例如取料點）
    /// 2) ExtraMoveWarning（由MainViewModel設定，例如跨軸讀取Y軸位置、比對Slot_Inspection的相機高度）
    /// 任一檢查觸發都會跳出確認視窗，使用者可選擇繼續或取消；不會強制擋下移動。
    /// 回傳 true 表示可以繼續移動，false 表示使用者選擇取消。
    /// </summary>
    private bool ConfirmTargetSafety(double targetMm)
    {
        foreach (var refPos in Config.ReferencePositions)
        {
            if (Math.Abs(targetMm - refPos.PositionMm) <= refPos.WarnToleranceMm)
            {
                var result = System.Windows.MessageBox.Show(
                    $"目標位置 {targetMm:F3} mm 接近參考點「{refPos.Name}」" +
                    $"({refPos.PositionMm:F3} mm ± {refPos.WarnToleranceMm:F1} mm)。\n\n是否確定繼續移動？",
                    "接近參考位置警告",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    StatusMessage = $"已取消：目標位置接近「{refPos.Name}」";
                    return false;
                }
            }
        }

        if (ExtraMoveWarning != null)
        {
            var (ok, reason) = ExtraMoveWarning(targetMm);
            if (!ok)
            {
                var result = System.Windows.MessageBox.Show(
                    $"{reason}\n\n是否確定繼續移動？",
                    "安全警告",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    StatusMessage = $"已取消：{reason}";
                    return false;
                }
            }
        }

        return true;
    }
}
