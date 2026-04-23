using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BarcodeReader.Interfaces;

namespace BarcodeReader.Services;

/// <summary>
/// WPF 實作的 IImageRenderer，繪製到 Image 控件上
/// </summary>
public sealed class WpfImageRenderer : IImageRenderer
{
    private readonly Image _imageControl;
    private DrawingGroup _overlayGroup = new();
    private WriteableBitmap? _bitmap;

    public WpfImageRenderer(Image imageControl)
    {
        _imageControl = imageControl;
    }

    public void RenderImage(byte[] imageData, int width, int height, ImagePixelFormat format)
    {
        _overlayGroup = new DrawingGroup();

        if (format == ImagePixelFormat.Mono8)
        {
            _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Gray8, null);
            _bitmap.WritePixels(new Int32Rect(0, 0, width, height), imageData, width, 0);
            AddImageDrawing(_bitmap, width, height);
        }
        else if (format == ImagePixelFormat.Jpeg)
        {
            using var ms = new MemoryStream(imageData);
            var decoder = new JpegBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var source = decoder.Frames[0];
            AddImageDrawing(source, source.PixelWidth, source.PixelHeight);
        }
    }

    public void DrawBarcodeRegion(Point[] points)
    {
        if (points.Length < 4) return;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], false, true);
            ctx.PolyLineTo(points[1..], true, false);
        }
        geometry.Freeze();

        var drawing = new GeometryDrawing
        {
            Geometry = geometry,
            Pen = new Pen(Brushes.Blue, 3)
        };
        _overlayGroup.Children.Add(drawing);
    }

    public void DrawWaybillRegion(float centerX, float centerY, float width, float height, float angle)
    {
        DrawRotatedRect(centerX, centerY, width, height, angle, Brushes.Red);
    }

    public void DrawOcrRegion(float centerX, float centerY, float width, float height, float angle)
    {
        DrawRotatedRect(centerX, centerY, width, height, angle, Brushes.Yellow);
    }

    public void Refresh()
    {
        _overlayGroup.Freeze();
        _imageControl.Source = new DrawingImage(_overlayGroup);
    }

    // ── 私有方法 ──

    private void AddImageDrawing(BitmapSource source, int width, int height)
    {
        var imageDrawing = new ImageDrawing(source, new Rect(0, 0, width, height));
        _overlayGroup.Children.Add(imageDrawing);
    }

    private void DrawRotatedRect(float cx, float cy, float w, float h, float angle, Brush stroke)
    {
        var rect = new Rect(cx - w / 2, cy - h / 2, w, h);
        var geometry = new RectangleGeometry(rect);

        if (angle != 0f)
        {
            geometry.Transform = new RotateTransform(angle, cx, cy);
        }
        geometry.Freeze();

        var drawing = new GeometryDrawing
        {
            Geometry = geometry,
            Pen = new Pen(stroke, 3)
        };
        _overlayGroup.Children.Add(drawing);
    }
}