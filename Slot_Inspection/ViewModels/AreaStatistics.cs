using System.Collections.ObjectModel;
using Slot_Inspection.Models;

namespace Slot_Inspection.ViewModels;

public sealed class AreaStatistics : ObservableObject
{
    private string _result = "-";
    private double _avg;
    private double _min;
    private double _max;

    public string Result
    {
        get => _result;
        set => SetProperty(ref _result, value);
    }

    public double Avg
    {
        get => _avg;
        set => SetProperty(ref _avg, value);
    }

    public double Min
    {
        get => _min;
        set => SetProperty(ref _min, value);
    }

    public double Max
    {
        get => _max;
        set => SetProperty(ref _max, value);
    }

    public void Calculate(ObservableCollection<SlotItem> row1, ObservableCollection<SlotItem> row2)
    {
        var allValues = row1.Concat(row2).Select(s => s.Value).ToList();
        if (allValues.Count == 0) return;

        Avg = Math.Round(allValues.Average(), 2);
        Min = allValues.Min();
        Max = allValues.Max();
        Result = row1.Concat(row2).Any(s => s.IsNg) ? "NG" : "OK";
    }
}
