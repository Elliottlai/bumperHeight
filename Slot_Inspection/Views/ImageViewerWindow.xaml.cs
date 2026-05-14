using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Slot_Inspection.Views;

public partial class ImageViewerWindow : Window
{
    private double _zoom = 1.0;
    private const double ZoomStep = 1.2;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 20.0;

    private Point _dragStart;
    private bool _isDragging;
    private double _translateX;
    private double _translateY;

    public ImageViewerWindow(BitmapSource image, string slotName)
    {
        InitializeComponent();
        TitleText.Text = slotName;
        PreviewImage.Source = image;

        // 初始縮放：適合視窗
        Loaded += (_, _) => FitToWindow();
    }

    private void FitToWindow()
    {
        if (PreviewImage.Source is not BitmapSource bmp) return;

        double availW = ImageCanvas.ActualWidth > 0 ? ImageCanvas.ActualWidth : ActualWidth;
        double availH = ImageCanvas.ActualHeight > 0 ? ImageCanvas.ActualHeight : ActualHeight - 32;
        double scaleX = availW / bmp.PixelWidth;
        double scaleY = availH / bmp.PixelHeight;
        _zoom = Math.Min(scaleX, scaleY) * 0.95;

        _translateX = 0;
        _translateY = 0;
        ApplyTransform();
    }

    private void ApplyTransform()
    {
        _zoom = Math.Clamp(_zoom, MinZoom, MaxZoom);
        ScaleT.ScaleX = _zoom;
        ScaleT.ScaleY = _zoom;
        TranslateT.X = _translateX;
        TranslateT.Y = _translateY;
        ZoomText.Text = $"{_zoom * 100:F0}%";
    }

    // ── 滾輪縮放（以滑鼠位置為中心） ──
    private void ImageBorder_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var border = (UIElement)sender;
        Point mousePos = e.GetPosition(border);

        double oldZoom = _zoom;
        _zoom *= e.Delta > 0 ? ZoomStep : 1.0 / ZoomStep;
        _zoom = Math.Clamp(_zoom, MinZoom, MaxZoom);

        double scale = _zoom / oldZoom;
        _translateX = mousePos.X + scale * (_translateX - mousePos.X);
        _translateY = mousePos.Y + scale * (_translateY - mousePos.Y);

        ApplyTransform();
        e.Handled = true;
    }

    // ── 拖曳平移 ──
    private void ImageBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragStart = e.GetPosition(sender as UIElement);
        ((UIElement)sender).CaptureMouse();
    }

    private void ImageBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void ImageBorder_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        Point current = e.GetPosition(sender as UIElement);
        _translateX += current.X - _dragStart.X;
        _translateY += current.Y - _dragStart.Y;
        _dragStart = current;
        ApplyTransform();
    }

    // ── 按鈕 ──
    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        _zoom *= ZoomStep;
        ApplyTransform();
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        _zoom /= ZoomStep;
        ApplyTransform();
    }

    private void Reset_Click(object sender, RoutedEventArgs e) => FitToWindow();
}
