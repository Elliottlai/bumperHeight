using System.Windows;

namespace BarcodeReader.Interfaces;

/// <summary>
/// 條碼結果解析介面
/// </summary>
public interface IBarcodeResultParser
{
    IReadOnlyList<BarcodeResult> Parse(nint pFrameInfo);
}

public class BarcodeResult
{
    public string Code { get; set; } = string.Empty;
    public string BarType { get; set; } = string.Empty;
    public int TotalProcessCost { get; set; }
    public string AlgoCost { get; set; } = string.Empty;
    public string PPM { get; set; } = string.Empty;
    public int OverQuality { get; set; }
    public int IDRScore { get; set; }
    public Point[] Points { get; set; } = [];
}