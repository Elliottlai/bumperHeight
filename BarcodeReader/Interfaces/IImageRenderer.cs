using System.Windows.Media.Imaging;

namespace BarcodeReader.Interfaces;

/// <summary>
/// 影像繪製介面（含條碼框、運單框、OCR框）
/// </summary>
public interface IImageRenderer
{
    void RenderImage(byte[] imageData, int width, int height, ImagePixelFormat format);
    void DrawBarcodeRegion(System.Windows.Point[] points);
    void DrawWaybillRegion(float centerX, float centerY, float width, float height, float angle);
    void DrawOcrRegion(float centerX, float centerY, float width, float height, float angle);
    void Refresh();
}

public enum ImagePixelFormat
{
    Mono8,
    Jpeg
}