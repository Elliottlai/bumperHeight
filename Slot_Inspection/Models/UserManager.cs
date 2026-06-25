namespace Slot_Inspection.Models;

using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>
/// 本機帳號管理（Config/Users.json）。密碼用 PBKDF2（Rfc2898DeriveBytes）雜湊+加鹽儲存，
/// 不需額外套件。檔案不存在時自動建立預設 admin/admin（Role=Admin），避免系統無帳號可登入。
/// </summary>
public static class UserManager
{
    private static readonly string FilePath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Config", "Users.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    private static string GenerateSalt() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltSize));

    private static string HashPassword(string password, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, saltBytes, Iterations, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(pbkdf2.GetBytes(HashSize));
    }

    /// <summary>讀取所有帳號；檔案不存在時先建立預設 admin/admin。</summary>
    public static List<UserAccount> LoadAll()
    {
        if (!File.Exists(FilePath))
        {
            string salt = GenerateSalt();
            Save([new UserAccount
            {
                Username = "admin",
                Salt = salt,
                PasswordHash = HashPassword("admin", salt),
                Role = UserRole.Admin
            }]);
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<UserAccount>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static void Save(List<UserAccount> users)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(users, JsonOpts));
    }

    private static UserAccount? Find(List<UserAccount> users, string username) =>
        users.FirstOrDefault(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

    /// <summary>驗證帳號密碼，成功回傳該帳號，失敗回傳 null。</summary>
    public static UserAccount? Authenticate(string username, string password)
    {
        var user = Find(LoadAll(), username);
        if (user == null) return null;
        return HashPassword(password, user.Salt) == user.PasswordHash ? user : null;
    }

    public static bool Exists(string username) => Find(LoadAll(), username) != null;

    /// <summary>新增帳號；帳號名已存在或為空白則回傳 false。</summary>
    public static bool Add(string username, string password, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        var users = LoadAll();
        if (Find(users, username) != null) return false;

        string salt = GenerateSalt();
        users.Add(new UserAccount
        {
            Username = username,
            Salt = salt,
            PasswordHash = HashPassword(password, salt),
            Role = role
        });
        Save(users);
        return true;
    }

    public static bool Delete(string username)
    {
        var users = LoadAll();
        int removed = users.RemoveAll(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) return false;
        Save(users);
        return true;
    }

    public static bool ChangeRole(string username, UserRole role)
    {
        var users = LoadAll();
        var user = Find(users, username);
        if (user == null) return false;
        user.Role = role;
        Save(users);
        return true;
    }

    /// <summary>驗證舊密碼通過後才更換新密碼（自助改密碼用）。</summary>
    public static bool ChangePassword(string username, string oldPassword, string newPassword)
    {
        var users = LoadAll();
        var user = Find(users, username);
        if (user == null) return false;
        if (HashPassword(oldPassword, user.Salt) != user.PasswordHash) return false;

        string newSalt = GenerateSalt();
        user.Salt = newSalt;
        user.PasswordHash = HashPassword(newPassword, newSalt);
        Save(users);
        return true;
    }
}
