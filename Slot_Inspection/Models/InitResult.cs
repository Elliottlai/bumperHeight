namespace Slot_Inspection.Models;

/// <summary>
/// 單一設備的初始化結果
/// </summary>
public sealed class DeviceInitResult
{
    public string DeviceName { get; init; } = "";
    public bool Success { get; init; }
    public string Message { get; init; } = "";
    public Exception? Exception { get; init; }

    public static DeviceInitResult Ok(string name)
        => new() { DeviceName = name, Success = true, Message = "OK" };

    public static DeviceInitResult Fail(string name, string message, Exception? ex = null)
        => new() { DeviceName = name, Success = false, Message = message, Exception = ex };
}

/// <summary>
/// 整機初始化結果
/// </summary>
public sealed class MachineInitResult
{
    public List<DeviceInitResult> Items { get; } = [];

    public bool AllPassed => Items.Count > 0 && Items.All(x => x.Success);

    public void Add(DeviceInitResult item) => Items.Add(item);

    public string GetSummary()
    {
        var lines = Items.Select(x => $"[{(x.Success ? "PASS" : "FAIL")}] {x.DeviceName}: {x.Message}");
        return string.Join(Environment.NewLine, lines);
    }
}
