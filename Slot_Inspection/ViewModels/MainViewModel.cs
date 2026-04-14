using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Slot_Inspection.Models;

namespace Slot_Inspection.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private string _barcode = "ABCDEFGHUK00123456";
    private string _currentDate = DateTime.Now.ToString("yyyy/MM/dd");
    private string _currentTime = DateTime.Now.ToString("HH:mm:ss");
    private bool _isRunning;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };

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

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

    public MainViewModel()
    {
        StartCommand = new RelayCommand(OnStart, () => !IsRunning);
        StopCommand = new RelayCommand(OnStop, () => IsRunning);

        FillSlots(AreaA_Row1, 1, 13, ngSlotNumber: 5);
        FillSlots(AreaA_Row2, 14, 12, ngSlotNumber: -1);
        FillSlots(AreaB_Row1, 1, 13, ngSlotNumber: 8);
        FillSlots(AreaB_Row2, 14, 12, ngSlotNumber: -1);

        AreaA_Stats.Calculate(AreaA_Row1, AreaA_Row2);
        AreaB_Stats.Calculate(AreaB_Row1, AreaB_Row2);

        _timer.Tick += (_, _) =>
        {
            CurrentDate = DateTime.Now.ToString("yyyy/MM/dd");
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
        };
        _timer.Start();
    }

    private void OnStart()
    {
        IsRunning = true;
        // TODO: 啟動檢測流程 / 連接設備
    }

    private void OnStop()
    {
        IsRunning = false;
        // TODO: 停止檢測流程
    }

    private static void FillSlots(ObservableCollection<SlotItem> target, int startNumber, int count, int ngSlotNumber)
    {
        Random random = new();

        for (int i = 0; i < count; i++)
        {
            int slotNo = startNumber + i;
            target.Add(new SlotItem
            {
                Name = $"SLOT#{slotNo}",
                Value = Math.Round(0.45 + random.NextDouble() * 0.1, 2),
                IsNg = slotNo == ngSlotNumber,
            });
        }
    }
}
