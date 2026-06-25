namespace Slot_Inspection.Models;

/// <summary>使用者角色，由低到高：Operator → Engineer → Admin。</summary>
public enum UserRole { Operator, Engineer, Admin }

/// <summary>本機帳號資料，存於 Config/Users.json。</summary>
public sealed class UserAccount
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Salt { get; set; } = "";
    public UserRole Role { get; set; }
}
