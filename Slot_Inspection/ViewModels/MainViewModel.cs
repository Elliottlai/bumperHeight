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
    private string _confirmedBarcode = ""; // 上一次已確認的條碼（用於重複判斷）
    private string _currentDate = DateTime.Now.ToString("yyyy/MM/dd");
    private string _currentTime = DateTime.Now.ToString("HH:mm:ss");
    private bool _isRunning;
    private string _statusMessage = "正在啟動...";
    private bool _isInitialized;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _plcTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly System.Diagnostics.Stopwatch _x1x2Stopwatch = new();
    private static readonly TimeSpan DryRunTriggerDuration = TimeSpan.FromMilliseconds(500);
    private bool _prevX10 = false; // X10 上一次狀態（預設 0）
    private readonly MachineController _machine = new();
    private CancellationTokenSource? _cts;

    public ObservableCollection<SlotItem> AreaA_Row1 { get; } = [];
    public ObservableCollection<SlotItem> AreaA_Row2 { get; } = [];
    public ObservableCollection<SlotItem> AreaB_Row1 { get; } = [];
    public ObservableCollection<SlotItem> AreaB_Row2 { get; } = [];

    public AreaStatistics AreaA_Stats { get; } = new();
    public AreaStatistics AreaB_Stats { get; } = new();

    public string Barcode
    {
        get => _barcode;
        set => SetProperty(ref _barcode, value);
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

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand InitAxesCommand { get; }
    public ICommand HomeCommand { get; }
    public ICommand ReadBarcodeCommand { get; }
    public ICommand MoveToPickPositionCommand { get; }
    public ICommand PositionSettingsCommand { get; }
    public ICommand CropSettingsCommand { get; }

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

    public MainViewModel()
    {
        // START 只需：初始化完成（含原點賦歸） + 尚未運行
        StartCommand = new RelayCommand(OnStart, () => IsInitialized && !IsRunning && !IsDryRunning);
        StopCommand = new RelayCommand(OnStop);
        // 主流程：逐 Slot 移動、取像、存圖
        RunCommand = new RelayCommand(OnRunInspection, () => IsDeviceReady && !IsRunning && !IsDryRunning);
        // 初始化：Config + Servo + PLC（不含原點賦歸）
        InitAxesCommand = new RelayCommand(OnInitDevices, () => !IsRunning && !IsDryRunning && !IsDeviceReady);
        // 原點賦歸：裝置就緒後隨時可按（不鎖定）
        HomeCommand = new RelayCommand(OnHome, () => IsDeviceReady && !IsRunning && !IsDryRunning);
        // 讀取條碼：Y→491 → 偵測 X12/X13 → 移動 X 軸
        ReadBarcodeCommand = new RelayCommand(OnReadBarcode, () => IsDeviceReady && !IsRunning && !IsDryRunning);
        // 移至取料位：Y 軸移動到取料位座標
        MoveToPickPositionCommand = new RelayCommand(OnMoveToPickPosition, () => IsDeviceReady && !IsRunning && !IsDryRunning);
        // 座標設定：任何時候都可開啟
        PositionSettingsCommand = new RelayCommand(OnPositionSettings);
        // 裁切設定：任何時候都可開啟
        CropSettingsCommand = new RelayCommand(OnCropSettings);

        // 先填入空白 Slot 佔位（UI 不會是空的）
        FillSlots(AreaA_Row1, 25, 13);
        FillSlots(AreaA_Row2, 12, 12);
        FillSlots(AreaB_Row1, 25, 13);
        FillSlots(AreaB_Row2, 12, 12);

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
        // X7/X10 光閘暫停/繼續（暫時停用）
        //if (IsDryRunning)
        //{
        //    bool x7 = _machine.GetPlcXOctal(7) ?? true;
        //    if (!x7)
        //        _machine.PauseDryRun();
        //    if (_machine.IsDryRunPaused)
        //    {
        //        bool x10 = _machine.GetPlcXOctal(10) ?? false;
        //        if (!_prevX10 && x10 && x7)
        //            _machine.ResumeDryRun();
        //        _prevX10 = x10;
        //    }
        //    else
        //    {
        //        _prevX10 = false;
        //    }
        //}

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

        StatusMessage = "正在初始化...";
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
                StatusMessage = "初始化完成，請按 START 開始";
            }
            else
            {
                StatusMessage = "初始化失敗";
            }

            // 通知 WPF 重新判斷所有按鈕的 CanExecute
            CommandManager.InvalidateRequerySuggested();

            System.Diagnostics.Debug.WriteLine(result.GetSummary());
        }
        catch (Exception ex)
        {
            StatusMessage = $"初始化例外: {ex.Message}";
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
            StatusMessage = "檢查載台在席感測器...";
            var carrierPos = await Task.Run(() => _machine.DetectCarrierPosition());

            if (carrierPos == MachineController.CarrierPosition.None)
            {
                StatusMessage = "未偵測到載台，請確認載台位置後重新 START";
                return;
            }

            StatusMessage = $"偵測到載台位置：{carrierPos}";

            // ── S01b：移動到讀碼位置 → 自動讀碼 ──
            string? code = await Task.Run(
                () => _machine.MoveAndReadBarcodeAsync(carrierPos, statusProgress, _cts.Token));

            if (string.IsNullOrEmpty(code))
            {
                StatusMessage = "讀碼失敗（NoRead），條碼軸回歸原點...";
                await Task.Run(() => _machine.MoveBarcodeAxisHomeAsync(statusProgress, _cts.Token));
                StatusMessage = "讀碼失敗，條碼軸已回原點，請按 START 重新開始";
                return;
            }

            // 驗證條碼格式
            var validation = BarcodeValidator.Validate(code);
            if (!validation.IsValid)
            {
                StatusMessage = $"條碼格式錯誤：{validation.Message}";
                return;
            }

            _confirmedBarcode = code;
            Barcode = code;
            StatusMessage = $"條碼確認：{code}";
            System.Diagnostics.Debug.WriteLine($"[S01] 自動讀碼成功: {code}");

            // ── S01c：讀碼軸回歸原點 ──
            await Task.Run(
                () => _machine.MoveBarcodeAxisHomeAsync(statusProgress, _cts.Token));

            // ── S03：開始檢測（載台在席已確認有料，直接進入檢測）──
            StatusMessage = $"檢測中：{_confirmedBarcode}";

            var slotProgress = new Progress<SlotInspectionProgress>(report =>
            {
                var collection = report.Target switch
                {
                    SlotInspectionProgress.TargetCollection.AreaA_Row1 => AreaA_Row1,
                    SlotInspectionProgress.TargetCollection.AreaA_Row2 => AreaA_Row2,
                    SlotInspectionProgress.TargetCollection.AreaB_Row1 => AreaB_Row1,
                    SlotInspectionProgress.TargetCollection.AreaB_Row2 => AreaB_Row2,
                    _ => null
                };

                if (collection != null && report.SlotIndex < collection.Count)
                {
                    collection[report.SlotIndex].Value       = report.Value;
                    collection[report.SlotIndex].IsNg        = report.IsNg;
                    collection[report.SlotIndex].ImageSource = report.Image;
                }

                StatusMessage = report.StatusText;
            });

            await Task.Run(
                () => _machine.RunInspectionAsync(_confirmedBarcode, slotProgress, _cts.Token));

            // 全部完成 → 計算統計
            AreaA_Stats.Calculate(AreaA_Row1, AreaA_Row2);
            AreaB_Stats.Calculate(AreaB_Row1, AreaB_Row2);

            bool pass = AreaA_Stats.Result == "OK" && AreaB_Stats.Result == "OK";

            // S04：寫彙總結果
            InspectionResultWriter.WriteSummary(_confirmedBarcode, pass);

            StatusMessage = $"{_confirmedBarcode} — {(pass ? "? PASS" : "? FAIL")}";
            System.Diagnostics.Debug.WriteLine(
                $"[S04] 完成: {_confirmedBarcode} = {(pass ? "PASS" : "FAIL")}");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "已停止";
        }
        catch (Exception ex)
        {
            StatusMessage = $"檢測例外: {ex.Message}";
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

        var statusProgress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            StatusMessage = "主流程啟動...逐 Slot 取像中";

            var dryRunSlotProgress = new Progress<SlotInspectionProgress>(report =>
            {
                var collection = report.Target switch
                {
                    SlotInspectionProgress.TargetCollection.AreaA_Row1 => AreaA_Row1,
                    SlotInspectionProgress.TargetCollection.AreaA_Row2 => AreaA_Row2,
                    SlotInspectionProgress.TargetCollection.AreaB_Row1 => AreaB_Row1,
                    SlotInspectionProgress.TargetCollection.AreaB_Row2 => AreaB_Row2,
                    _ => null
                };

                if (collection != null && report.SlotIndex < collection.Count)
                {
                    if (report.Image != null)
                        collection[report.SlotIndex].ImageSource = report.Image;
                    if (report.Value != 0)
                        collection[report.SlotIndex].Value = report.Value;
                }

                if (!string.IsNullOrEmpty(report.StatusText))
                    StatusMessage = report.StatusText;
            });

            await Task.Run(() => _machine.DryRunAsync(statusProgress, _cts.Token, _confirmedBarcode, dryRunSlotProgress));
            StatusMessage = "主流程完成 ?";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "主流程已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"主流程異常: {ex.Message}";
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
            StatusMessage = $"Y 軸移動至 {_machine.Config.BarcodePositionY} mm...";

            // STEP 1: Y 軸移動到設定位置
            _machine.Axes["AxisY"].MotMoveAbs(_machine.Config.BarcodePositionY);
            bool yArrived = await MachineController.WaitUntilAsync(
                () => _machine.Axes["AxisY"].Wait(),
                TimeSpan.FromSeconds(30), ct);
            if (!yArrived)
            {
                StatusMessage = "Y 軸移動逾時";
                return;
            }
            StatusMessage = $"Y 軸已到位 ({_machine.Config.BarcodePositionY} mm)";

            // STEP 2: 偵測 X12 / X13 IO 訊號
            bool x12 = _machine.GetPlcXOctal(12) ?? false;
            bool x13 = _machine.GetPlcXOctal(13) ?? false;

            if (!x12 && !x13)
            {
                StatusMessage = "未偵測到載台 (X12/X13 皆無訊號)";
                return;
            }

            // STEP 3: 依據訊號移動 X 軸
            double targetX;
            if (x12)
            {
                targetX = _machine.Config.BarcodePositionLeftX;
                StatusMessage = $"偵測到 X12=ON → X 軸移動至 {targetX} mm...";
            }
            else
            {
                targetX = _machine.Config.BarcodePositionRightX;
                StatusMessage = $"偵測到 X13=ON → X 軸移動至 {targetX} mm...";
            }

            _machine.Axes["AxisX"].MotMoveAbs(targetX);
            bool xArrived = await MachineController.WaitUntilAsync(
                () => _machine.Axes["AxisX"].Wait(),
                TimeSpan.FromSeconds(30), ct);
            if (!xArrived)
            {
                StatusMessage = "X 軸移動逾時";
                return;
            }

            StatusMessage = $"X 軸已到位 ({targetX} mm)，準備讀碼...";

            // TODO: 觸發讀碼器讀取條碼
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "讀碼流程已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"讀碼流程異常: {ex.Message}";
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
        StatusMessage = "已停止";
        // TODO: 接入停止流程
    }

    private async void OnMoveToPickPosition()
    {
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            double targetY = _machine.Config.BarcodePositionY;
            StatusMessage = $"Y 軸移動至取料位置 {targetY} mm...";

            _machine.Axes["AxisY"].MotMoveAbs(targetY);
            bool arrived = await MachineController.WaitUntilAsync(
                () => _machine.Axes["AxisY"].Wait(),
                TimeSpan.FromSeconds(30), ct);

            StatusMessage = arrived
                ? $"Y 軸已到達取料位置 ({targetY} mm) ?"
                : "Y 軸移動逾時";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "移至取料位已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"移至取料位異常: {ex.Message}";
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
            StatusMessage = "座標設定已更新（已儲存）";
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
            StatusMessage = "裁切設定已更新（已儲存）";
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
            StatusMessage = "初始化中（Config + Servo + PLC）...";
            var result = await Task.Run(
                () => _machine.InitializeDevicesAsync(progress, _cts.Token));

            IsDeviceReady = result.AllPassed;

            if (result.AllPassed)
            {
                StatusMessage = "初始化完成 ? 請按【原點賦歸】";
            }
            else
            {
                var failed = result.Items.FirstOrDefault(x => !x.Success);
                StatusMessage = $"初始化失敗: {failed?.DeviceName} - {failed?.Message}";
            }

            System.Diagnostics.Debug.WriteLine(result.GetSummary());
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "初始化已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"初始化異常: {ex.Message}";
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
            StatusMessage = "原點賦歸中...";
            var result = await Task.Run(
                () => _machine.HomeAllAxesPublicAsync(progress, _cts.Token));

            if (result.AllPassed)
            {
                IsInitialized = true;
                StatusMessage = "原點賦歸完成 ? 可執行空跑測試或 START";
            }
            else
            {
                var failed = result.Items.FirstOrDefault(x => !x.Success);
                StatusMessage = $"原點賦歸失敗: {failed?.DeviceName} - {failed?.Message}";
            }

            System.Diagnostics.Debug.WriteLine(result.GetSummary());
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "原點賦歸已取消";
        }
        catch (Exception ex)
        {
            StatusMessage = $"原點賦歸異常: {ex.Message}";
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
        foreach (var slot in AreaA_Row1.Concat(AreaA_Row2)
                                       .Concat(AreaB_Row1)
                                       .Concat(AreaB_Row2))
        {
            slot.Value = 0;
            slot.IsNg  = false;
        }

        AreaA_Stats.Calculate(AreaA_Row1, AreaA_Row2);
        AreaB_Stats.Calculate(AreaB_Row1, AreaB_Row2);
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
}
