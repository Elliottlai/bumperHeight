using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Slot_Inspection.Models;
using Slot_Inspection.Services;
using Slot_Inspection.Views;

namespace Slot_Inspection.ViewModels;

public sealed class MainViewModel : ObservableObject
{
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
    private readonly DispatcherTimer _plcTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly System.Diagnostics.Stopwatch _x1x2Stopwatch = new();
    private static readonly TimeSpan DryRunTriggerDuration = TimeSpan.FromMilliseconds(500);
    private bool _prevX10 = false; // X10 上一次狀態（預設 0，保留供未來使用）
    private bool _isLightGateBlocked; // X7=LOW：光閘遮斷中
    private int _lightGateBlockedCount = 0; // 連續讀到 LOW 的次數（防抖用）
    private const int LightGateDebounceCount = 3; // 需連續 N 次（×100ms）LOW 才視為真正觸發
    private bool _isLightGateBypassActive; // 取料暫停光閘旗標
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
        MesConnectionStatus.Connected    => "MES ? 已連線",
        MesConnectionStatus.Disconnected => "MES ? 未連線",
        _                                => "MES — 未嘗試"
    };

    /// <summary>供 XAML DataTrigger 比對用的字串（避免 enum 型別引用）。</summary>
    public string MesStatusKey => _mesStatus.ToString();

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
        set => SetProperty(ref _isRunning, value);
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
    public ICommand SlcResetCommand { get; }

    /// <summary>
    /// 取料暫停光閘：啟用時光閘觸發不中斷流程，但 RUN 與原點賦歸同時鎖定。
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
            }
        }
    }

    /// <summary>按鈕顯示文字，隨狀態切換。</summary>
    public string LightGateBypassButtonText =>
        IsLightGateBypassActive ? "取消暫停光閘" : "取料暫停光閘";

    private bool _isDryRunning;
    public bool IsDryRunning
    {
        get => _isDryRunning;
        set => SetProperty(ref _isDryRunning, value);
    }

    private bool _isDeviceReady;
    /// <summary>
    /// 裝置已初始化（Config+Servo+PLC），但尚未完成原點賦歸。
    /// </summary>
    public bool IsDeviceReady
    {
        get => _isDeviceReady;
        set => SetProperty(ref _isDeviceReady, value);
    }

    /// <summary>
    /// 模擬模式開關。Debug-Sim 組態預設 true，其他組態預設 false。
    /// 可在 UI 動態切換。
    /// </summary>
    public bool IsSimulationMode
    {
        get => MachineController.SimulationMode;
        set
        {
            MachineController.SimulationMode = value;
            OnPropertyChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

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

    public MainViewModel()
    {
        // START
        StartCommand = new RelayCommand(OnStart, () => IsInitialized && !IsRunning && !IsDryRunning);
        StopCommand = new RelayCommand(OnStop);
        // 主流程：逐 Slot 移動、取像、存圖（需已完成原點賦歸 + 光閘未遮斷 + bypass 未啟用）
        RunCommand = new RelayCommand(OnRunInspection, () => IsDeviceReady && IsInitialized && !IsRunning && !IsDryRunning && !IsLightGateBlocked && !IsLightGateBypassActive);
        // 初始化：Config + Servo + PLC（不含原點賦歸）
        InitAxesCommand = new RelayCommand(OnInitDevices, () => !IsRunning && !IsDryRunning && !IsDeviceReady);
        // 原點賦歸：光閘必須未遮斷且 bypass 未啟用才可按
        HomeCommand = new RelayCommand(OnHome, () => IsDeviceReady && !IsRunning && !IsDryRunning && !IsLightGateBlocked && !IsLightGateBypassActive);
        // 讀取條碼：Y→491 → 偵測 X12/X13 → 移動 X 軸
        ReadBarcodeCommand = new RelayCommand(OnReadBarcode, () => IsDeviceReady && !IsRunning && !IsDryRunning);
        // 移至取料位：Y 軸移動到取料位座標
        MoveToPickPositionCommand = new RelayCommand(OnMoveToPickPosition, () => IsDeviceReady && !IsRunning && !IsDryRunning);
        // 座標設定：任何時候都可開啟
        PositionSettingsCommand = new RelayCommand(OnPositionSettings);


        // 裁切設定：任何時候都可開啟
        CropSettingsCommand = new RelayCommand(OnCropSettings);

        // 亮度設定：任何時候都可開啟
        LightSettingsCommand = new RelayCommand(OnLightSettings);

        // 點擊圖像放大預覽
        OpenImageViewerCommand = new RelayCommand<SlotItem>(OnOpenImageViewer);

        // 開啟 CSV 報表資料夾
        OpenResultFolderCommand = new RelayCommand(OnOpenResultFolder);

        // 條碼 TextBox Enter 確認
        ConfirmBarcodeCommand = new RelayCommand(OnConfirmBarcode);

        // 取料暫停光閘 toggle
        ToggleLightGateBypassCommand = new RelayCommand(OnToggleLightGateBypass);

        // SLC Reset
        SlcResetCommand = new RelayCommand(OnSlcReset, () => IsDeviceReady && !IsRunning && !IsDryRunning);

        // 先填入空白 Slot 佔位（UI 不會是空的）
        FillSlots(AreaA_Row1, 25, 25);
        FillSlots(AreaB_Row1, 25, 25);

        _timer.Tick += (_, _) =>
        {
            CurrentDate = DateTime.Now.ToString("yyyy/MM/dd");
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
        };
        _timer.Start();

        _plcTimer.Tick += OnPlcTick;
        _plcTimer.Start();
    }

    /// <summary>
    /// 每 100ms 輪詢一次 PLC X1+X2 訊號。
    /// 兩個訊號同時為 true 且持續 0.5 秒後，自動觸發空跑程式。
    /// </summary>
    private void OnPlcTick(object? sender, EventArgs e)
    {
        // X7 光閘狀態：不論任何模式都追蹤，LOW = 遮斷（加入防抖：需連續 LightGateDebounceCount 次才觸發）
        bool x7 = _machine.GetPlcXOctal(7) ?? true;
        bool rawBlocked = !x7;

        if (rawBlocked)
        {
            _lightGateBlockedCount = Math.Min(_lightGateBlockedCount + 1, LightGateDebounceCount + 1);
        }
        else
        {
            _lightGateBlockedCount = 0;
        }

        bool blocked = _lightGateBlockedCount >= LightGateDebounceCount;

        // 光閘確認觸發（從未遮斷 → 遮斷，且已通過防抖確認）
        if (blocked && !_isLightGateBlocked)
        {
            if (!IsLightGateBypassActive)
            {
                // 正常模式：取消流程並強制重新原點賦歸
                _cts?.Cancel();
                IsInitialized = false;
                StatusMessage = "⚠ 光閘觸發！請解除光閘後執行原點賦歸才可繼續 RUN";
            }
            else
            {
                // 取料暫停光閘模式：僅顯示提示，不中斷流程
                StatusMessage = "⚠ 光閘觸發（取料暫停模式，流程不中斷）";
            }
        }

        IsLightGateBlocked = blocked;

        if (!IsDeviceReady || IsRunning || IsDryRunning) return;

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

        ResetAllSlots();
        Barcode = "";
        _confirmedBarcode = "";

        var statusProgress = new Progress<string>(msg => StatusMessage = msg);

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
            _ = _mesClient.QueryWorkOrderAsync("SlotInspection", _confirmedBarcode)
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
            _ = _mesClient.UploadResultAsync("SlotInspection", _confirmedBarcode, pass)
                .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                    TaskScheduler.FromCurrentSynchronizationContext());

            StatusMessage = $"量測完成  {_confirmedBarcode}  →  {(pass ? "✔ PASS" : "✘ FAIL")}";
            System.Diagnostics.Debug.WriteLine(
                $"[S04] 完成: {_confirmedBarcode} = {(pass ? "PASS" : "FAIL")}");
        }
        catch (OperationCanceledException)
        {
            IsInitialized = false;
            StatusMessage = "⏹ 作業已由使用者取消，請執行原點賦歸後再繼續 RUN";
        }
        catch (Exception ex)
        {
            IsInitialized = false;
            StatusMessage = $"✘ 量測發生錯誤：{ex.Message}，請執行原點賦歸後再繼續 RUN";
            System.Diagnostics.Debug.WriteLine(ex);
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
        IsDryRunning = true;
        _cts = new CancellationTokenSource();

        // X10 邊緣偵測狀態重置（X7 改為電平觸發，不需 _prevX7）
        _prevX10 = false;

        // 清除上一輪的影像 / 數值 / 狀態
        ResetAllSlots();

        var statusProgress = new Progress<string>(msg => StatusMessage = msg);

        try
        {


            // ── STEP 0：讀取條碼 ──────────────────────────────────────────
            StatusMessage = "正在移動至條碼讀取位置...";
            var ct = _cts.Token;

            // Y 軸移動至讀碼位置
            StatusMessage = $"Y 軸移動至條碼讀取位置 {_machine.Config.BarcodePositionY:F1} mm，請稍候...";
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
            _ = _mesClient.QueryWorkOrderAsync("SlotInspection", _confirmedBarcode)
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
            _ = _mesClient.UploadResultAsync("SlotInspection", _confirmedBarcode, pass)
                .ContinueWith(_ => MesStatus = _mesClient.LastStatus,
                    TaskScheduler.FromCurrentSynchronizationContext());

            StatusMessage = $"主流程完成  →  {(pass ? "✔ PASS" : "✘ FAIL")}";
        }
        catch (OperationCanceledException)
        {
            IsInitialized = false;
            StatusMessage = "⏹ 主流程已由使用者取消，請執行原點賦歸後再繼續 RUN";
        }
        catch (Exception ex)
        {
            IsInitialized = false;
            StatusMessage = $"✘ 主流程發生錯誤：{ex.Message}，請執行原點賦歸後再繼續 RUN";
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

    private async void OnReadBarcode()
    {
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            StatusMessage = $"Y 軸移動至條碼讀取位置 {_machine.Config.BarcodePositionY} mm，請稍候...";

            // STEP 1: Y 軸移動到設定位置
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
        IsRunning = false;
        IsDryRunning = false;
        _cts?.Cancel();
        // Stop 後必須重新原點賦歸才可再按 RUN
        IsInitialized = false;
        StatusMessage = "⏹ 作業已由使用者取消，請執行原點賦歸後再繼續 RUN";
    }

    private async void OnMoveToPickPosition()
    {
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
        var vm = new PositionSettingsViewModel();
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
        var vm = new LightSettingsViewModel(_machine.Config);
        var win = new Views.LightSettingsWindow
        {
            DataContext = vm,
            Owner = Application.Current.MainWindow
        };
        if (win.ShowDialog() == true)
        {
            vm.ApplyTo(_machine.Config);
            StatusMessage = $"✔ 光源設定已儲存（左={vm.LightIntensityLeft}，右={vm.LightIntensityRight}）";
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
        }
    }


    /// 初始化按鈕：Config + Servo 激磁 + PLC 連線（不含原點賦歸）。
    /// </summary>
    private async void OnInitDevices()
    {
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
        IsDryRunning = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            StatusMessage = "正在執行原點復歸，請稍候...";
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
                    StatusMessage = "✔ 原點復歸完成！請放入載具後按 RUN 開始";
                }
            }
            else
            {
                var failed = result.Items.FirstOrDefault(x => !x.Success);
                StatusMessage = $"✘ 原點復歸失敗：{failed?.DeviceName} - {failed?.Message}";
            }

            System.Diagnostics.Debug.WriteLine(result.GetSummary());
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "⏹ 原點復歸已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✘ 原點復歸發生錯誤：{ex.Message}";
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
    /// 切換「取料暫停光閘」模式。
    /// 啟用：光閘觸發不中斷流程，同時鎖定 RUN 與原點賦歸。
    /// 停用：恢復正常光閘防護。
    /// </summary>
    private void OnToggleLightGateBypass()
    {
        IsLightGateBypassActive = !IsLightGateBypassActive;

        if (!IsLightGateBypassActive)
        {
            // 停用 bypass 時：重置防抖計數，讓光閘狀態在下一次 PLC 輪詢時重新評估
            // 避免取料期間的遮斷殘留導致 RUN / 原點賦歸持續鎖定
            _lightGateBlockedCount = 0;
            IsLightGateBlocked = false;
        }

        StatusMessage = IsLightGateBypassActive
            ? "⚠ 取料暫停光閘已啟用：RUN 與原點賦歸暫時鎖定"
            : "✔ 取料暫停光閘已取消，恢復正常光閘防護";
    }

    private async void OnSlcReset()
    {
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
}
