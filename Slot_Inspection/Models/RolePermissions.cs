namespace Slot_Inspection.Models;

/// <summary>
/// 可由Admin在「角色權限設定」視窗調整的功能權限項目。
/// Admin永遠擁有全部權限，不受這個表影響；這裡只決定 Operator / Engineer 各自能不能用。
/// </summary>
public enum Permission
{
    // ── 基本機台操作 ──
    Run,
    Stop,
    Home,
    InitDevices,
    ReadBarcode,
    MoveToPickPosition,
    OpenResultFolder,
    SlcReset,
    ToggleLightGateBypass,

    // ── 進階設定 ──
    PositionSettings,
    LightSettings,
    CropSettings,
    SimulationMode,
    AxisSettleDelay,
    QcRecipe,
}

/// <summary>Operator / Engineer 各自的權限表，存放於 Config/RolePermissions.json。</summary>
public sealed class RolePermissionMatrix
{
    public Dictionary<string, bool> Operator { get; set; } = new();
    public Dictionary<string, bool> Engineer { get; set; } = new();
}
