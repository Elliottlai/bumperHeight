namespace Slot_Inspection.Models;

/// <summary>
/// 條碼驗證結果
/// </summary>
public sealed class BarcodeValidationResult
{
    public bool IsValid { get; init; }
    public string Message { get; init; } = "";

    public static BarcodeValidationResult Ok()
        => new() { IsValid = true, Message = "OK" };

    public static BarcodeValidationResult Fail(string message)
        => new() { IsValid = false, Message = message };
}

/// <summary>
/// S01 條碼驗證邏輯（純規則判斷，不碰 UI 也不碰硬體）
/// </summary>
public static class BarcodeValidator
{
    // ── 條碼規則（待確認：依實際格式調整）──
    public const int RequiredLength = 18; // 固定長度，要改只改這裡

    /// <summary>
    /// 驗證條碼格式是否合法
    /// </summary>
    public static BarcodeValidationResult Validate(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return BarcodeValidationResult.Fail("條碼不可為空");

        if (barcode.Length != RequiredLength)
            return BarcodeValidationResult.Fail(
                $"條碼長度錯誤（應為 {RequiredLength} 碼，實際 {barcode.Length} 碼）");

        return BarcodeValidationResult.Ok();
    }

    /// <summary>
    /// 判斷是否為重複掃碼（新舊條碼相同視為同一件料）
    /// </summary>
    public static bool IsDuplicate(string? newBarcode, string? lastBarcode)
        => !string.IsNullOrEmpty(newBarcode)
        && string.Equals(newBarcode, lastBarcode, StringComparison.Ordinal);
}
