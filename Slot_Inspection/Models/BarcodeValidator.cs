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
    /// <summary>
    /// 驗證條碼格式是否合法（不限制長度）
    /// </summary>
    public static BarcodeValidationResult Validate(string? barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return BarcodeValidationResult.Fail("條碼不可為空");

        return BarcodeValidationResult.Ok();
    }

    /// <summary>
    /// 判斷是否為重複掃碼（新舊條碼相同視為同一件料）
    /// </summary>
    public static bool IsDuplicate(string? newBarcode, string? lastBarcode)
        => !string.IsNullOrEmpty(newBarcode)
        && string.Equals(newBarcode, lastBarcode, StringComparison.Ordinal);
}
