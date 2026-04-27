using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Slot_Inspection.Models;
using Slot_Inspection.Services;

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

    public MainViewModel()
    {
        // START 只需：初始化完成 + 尚未運行（條碼由 START 後自動讀取）
        StartCommand = new RelayCommand(OnStart, () => IsInitialized && !IsRunning);
        StopCommand = new RelayCommand(OnStop, () => IsRunning);

        // 先填入空白 Slot 佔位（UI 不會是空的）
        FillSlots(AreaA_Row1, 1, 13);
        FillSlots(AreaA_Row2, 14, 12);
        FillSlots(AreaB_Row1, 1, 13);
        FillSlots(AreaB_Row2, 14, 12);

        _timer.Tick += (_, _) =>
        {
            CurrentDate = DateTime.Now.ToString("yyyy/MM/dd");
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
        };
        _timer.Start();
    }

    /// <summary>
    /// S00：開機自動初始化（由 MainWindow.Loaded 呼叫）
    /// </summary>
    public async Task InitializeAsync()
    {
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
    //  S01 感測器檢查 → 移動讀碼 → S02 放料 → S03 檢測 → S04 結果
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

            // ── S02：等待放料就位 ──
            StatusMessage = "請放料...（等待感測器）";
            bool placed = await Task.Run(
                () => _machine.WaitForMaterialAsync(statusProgress, _cts.Token));

            if (!placed)
            {
                StatusMessage = "放料超時，請重新操作";
                return;
            }

            // ── S03：開始檢測 ──
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

    private void OnStop()
    {
        IsRunning = false;
        _cts?.Cancel();
        StatusMessage = "已停止";
        // TODO: 接入停止流程
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
                Name = $"SLOT#{startNumber + i}",
                Value = 0,
                IsNg = false,
            });
        }
    }
}
