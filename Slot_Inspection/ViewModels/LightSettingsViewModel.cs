using Slot_Inspection.Models;

namespace Slot_Inspection.ViewModels;

/// <summary>
/// 亮度設定視窗的 ViewModel。
/// 持有左/右相機亮度及 Value 上下極限的可編輯副本，按 OK 後呼叫端再寫回 config。
/// </summary>
public sealed class LightSettingsViewModel : ObservableObject
{
    private int _lightIntensityLeft;
    private int _lightIntensityRight;
    private double _valueLimitLow;
    private double _valueLimitHigh;

    public int LightIntensityLeft
    {
        get => _lightIntensityLeft;
        set => SetProperty(ref _lightIntensityLeft, Math.Clamp(value, 0, 100));
    }

    public int LightIntensityRight
    {
        get => _lightIntensityRight;
        set => SetProperty(ref _lightIntensityRight, Math.Clamp(value, 0, 100));
    }

    public double ValueLimitLow
    {
        get => _valueLimitLow;
        set => SetProperty(ref _valueLimitLow, value);
    }

    public double ValueLimitHigh
    {
        get => _valueLimitHigh;
        set => SetProperty(ref _valueLimitHigh, value);
    }

    public LightSettingsViewModel(InspectionConfig config)
    {
        _lightIntensityLeft  = config.LightIntensityLeft;
        _lightIntensityRight = config.LightIntensityRight;
        _valueLimitLow       = config.ValueLimitLow;
        _valueLimitHigh      = config.ValueLimitHigh;
    }

    /// <summary>將目前值寫回 InspectionConfig 並呼叫 Save()。</summary>
    public void ApplyTo(InspectionConfig config)
    {
        config.LightIntensityLeft  = LightIntensityLeft;
        config.LightIntensityRight = LightIntensityRight;
        config.ValueLimitLow       = ValueLimitLow;
        config.ValueLimitHigh      = ValueLimitHigh;
        config.Save();
    }
}
