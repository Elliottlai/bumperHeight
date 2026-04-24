using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Slot_Inspection.Services;

/// <summary>
/// 模擬模式下動態產生測試圖片（彩色方塊 + 量測值文字）。
/// 作為 SimImageLoader 找不到圖檔時的備用方案，不需要任何圖檔。
/// </summary>
public static class SimImageGenerator
{
    private static readonly Random _rng = new();

    /// <summary>
    /// 產生一張模擬圖片，已 Freeze 可跨執行緒傳到 UI。
    /// OK：綠色系；NG：紅色系 + 右下角 NG 標記。
    /// </summary>
    public static BitmapSource Generate(
        string slotName, double value, bool isNg,
        int width = 120, int height = 90)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            // ── 背景 ──
            var bg = isNg
                ? Color.FromRgb(
                    (byte)_rng.Next(160, 210),
                    (byte)_rng.Next(30, 70),
                    (byte)_rng.Next(30, 70))
                : Color.FromRgb(
                    (byte)_rng.Next(30, 70),
                    (byte)_rng.Next(110, 170),
                    (byte)_rng.Next(30, 70));
            dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, width, height));

            // ── 雜訊方塊 ──
            for (int i = 0; i < 12; i++)
            {
                dc.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb(70,
                        (byte)_rng.Next(255),
                        (byte)_rng.Next(255),
                        (byte)_rng.Next(255))),
                    null,
                    new Rect(
                        _rng.Next(0, width - 12),
                        _rng.Next(0, height - 12),
                        _rng.Next(4, 14),
                        _rng.Next(4, 14)));
            }

            // ── Slot 名稱（左上角）──
            dc.DrawText(
                new FormattedText(
                    slotName,
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Consolas"), 9, Brushes.White, 96),
                new Point(3, 3));

            // ── 量測值（置中）──
            var vt = new FormattedText(
                value.ToString("F2"),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Consolas"), 18, Brushes.Yellow, 96);
            dc.DrawText(vt, new Point((width - vt.Width) / 2, (height - vt.Height) / 2));

            // ── NG 標記（右下角）──
            if (isNg)
            {
                var ng = new FormattedText(
                    "NG",
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        new FontFamily("Arial"),
                        FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                    20, Brushes.Red, 96);
                dc.DrawText(ng, new Point(width - ng.Width - 4, height - ng.Height - 4));
            }
        }

        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze(); // ← 必須 Freeze，才能從背景執行緒傳到 UI
        return bmp;
    }
}
