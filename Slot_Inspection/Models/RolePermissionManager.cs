using System.IO;
using System.Linq;
using System.Text.Json;

namespace Slot_Inspection.Models;

/// <summary>
/// Operator / Engineer 權限表的存讀管理，存放於 Config/RolePermissions.json。
/// Admin不受這個表約束，永遠擁有全部權限。
/// </summary>
public static class RolePermissionManager
{
    private static readonly string FilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Config", "RolePermissions.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // 跟原本寫死在程式裡的權限配置完全對齊：基本機台操作 Operator/Engineer 都能做，
    // 進階設定類只有 Engineer 以上能做。檔案缺項或第一次啟動時都以此為準。
    private static readonly Permission[] BasicOperations =
    [
        Permission.Run, Permission.Stop, Permission.Home, Permission.InitDevices,
        Permission.ReadBarcode, Permission.MoveToPickPosition, Permission.OpenResultFolder,
        Permission.SlcReset, Permission.ToggleLightGateBypass, Permission.RecipeSwitch,
    ];

    private static RolePermissionMatrix? _cache;

    /// <summary>讀取權限表；檔案不存在或解析失敗時，建立並寫回預設值（對齊原本寫死的權限配置）。</summary>
    public static RolePermissionMatrix Load()
    {
        if (_cache != null) return _cache;

        if (File.Exists(FilePath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<RolePermissionMatrix>(File.ReadAllText(FilePath), JsonOpts);
                if (loaded != null)
                {
                    _cache = loaded;
                    return _cache;
                }
            }
            catch
            {
                // 設定檔損毀時改用預設值，不讓整支程式因此開不起來
            }
        }

        _cache = BuildDefault();
        Save(_cache);
        return _cache;
    }

    private static RolePermissionMatrix BuildDefault()
    {
        var matrix = new RolePermissionMatrix();
        foreach (var p in System.Enum.GetValues<Permission>())
        {
            bool basic = BasicOperations.Contains(p);
            matrix.Operator[p.ToString()] = basic;
            matrix.Engineer[p.ToString()] = true; // Engineer 以上原本就擁有全部這些功能
        }
        return matrix;
    }

    public static void Save(RolePermissionMatrix matrix)
    {
        _cache = matrix;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(matrix, JsonOpts));
    }

    /// <summary>判斷某角色是否有某項權限。Admin永遠true；設定檔缺項時比照原本寫死的權限配置。</summary>
    public static bool IsAllowed(UserRole role, Permission permission)
    {
        if (role == UserRole.Admin) return true;
        if (role != UserRole.Operator && role != UserRole.Engineer) return false;

        var matrix = Load();
        var dict = role == UserRole.Operator ? matrix.Operator : matrix.Engineer;
        if (dict.TryGetValue(permission.ToString(), out bool allowed)) return allowed;

        return role == UserRole.Engineer || BasicOperations.Contains(permission);
    }

    /// <summary>設定某角色某項權限的允許狀態並立即存檔。</summary>
    public static void SetAllowed(UserRole role, Permission permission, bool allowed)
    {
        if (role == UserRole.Admin) return; // Admin不受這個表約束，沒有東西可以設

        var matrix = Load();
        var dict = role == UserRole.Operator ? matrix.Operator : matrix.Engineer;
        dict[permission.ToString()] = allowed;
        Save(matrix);
    }
}
