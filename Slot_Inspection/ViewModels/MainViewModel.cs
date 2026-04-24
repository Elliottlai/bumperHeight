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
    private bool _isBarcodeConfirmed;

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

    /// <summary>條碼已通過驗證，START 按鈕才會亮</summary>
    public bool IsBarcodeConfirmed
    {
        get => _isBarcodeConfirmed;
        set => SetProperty(ref _isBarcodeConfirmed, value);
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

    /// <summary>
    /// S01：確認條碼 — 由 TextBox Enter 鍵或讀碼器觸發
    /// </summary>
    public ICommand ConfirmBarcodeCommand { get; }

    public MainViewModel()
    {
        // START 必須：初始化完成 + 條碼已確認 + 尚未運行
        StartCommand = new RelayCommand(OnStart, () => IsInitialized && IsBarcodeConfirmed && !IsRunning);
        StopCommand = new RelayCommand(OnStop, () => IsRunning);
        ConfirmBarcodeCommand = new RelayCommand(OnConfirmBarcode, () => IsInitialized && !IsRunning);

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
                // S00 完成 → 進入 S01 等待條碼
                StatusMessage = "請掃描或輸入條碼（Enter 確認）";
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
    //  S01：條碼確認
    // ═══════════════════════════════════════

    private void OnConfirmBarcode()
    {
        var input = Barcode?.Trim() ?? "";

        // 重複掃碼：與上次相同 → 視為一次，靜默接受
        if (BarcodeValidator.IsDuplicate(input, _confirmedBarcode))
        {
            StatusMessage = $"條碼已確認：{input}（重複掃描，略過）";
            System.Diagnostics.Debug.WriteLine($"[S01] 重複條碼，略過: {input}");
            return;
        }

        // 驗證格式
        var validation = BarcodeValidator.Validate(input);
        if (!validation.IsValid)
        {
            StatusMessage = $"條碼錯誤：{validation.Message}";
            IsBarcodeConfirmed = false;
            System.Diagnostics.Debug.WriteLine($"[S01] 條碼驗證失敗: {validation.Message}");
            CommandManager.InvalidateRequerySuggested();
            return;
        }

        // 驗證通過
        _confirmedBarcode = input;
        IsBarcodeConfirmed = true;
        StatusMessage = $"條碼確認：{input}，請按 START";
        System.Diagnostics.Debug.WriteLine($"[S01] 條碼確認: {input}");
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// 供讀碼器在背景收到條碼後呼叫（自動切回 UI 執行緒）
    /// </summary>
    public void ReceiveBarcodeFromReader(string barcode)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Barcode = barcode;
            OnConfirmBarcode();
        });
    }

    // ═══════════════════════════════════════
    //  S03：START / STOP
    // ═══════════════════════════════════════

    private async void OnStart()
    {
        IsRunning = true;
        _cts = new CancellationTokenSource();

        // 清空上一次的 Slot 資料
        ResetAllSlots();

        var statusProgress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            // ── S02：等待放料就位 ──
            StatusMessage = "請放料...（等待感測器）";
            bool placed = await Task.Run(
                () => _machine.WaitForMaterialAsync(statusProgress, _cts.Token));

            if (!placed)
            {
                StatusMessage = "放料超時，請重新操作";
                return; // 不進入 S03
            }

            // ── S03：開始檢測 ──
            StatusMessage = $"檢測中：{_confirmedBarcode}";

            // 每個 Slot 完成就回報一次，自動回到 UI 執行緒
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

            // 完成後重置條碼，等待下一件料
            IsBarcodeConfirmed = false;
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
