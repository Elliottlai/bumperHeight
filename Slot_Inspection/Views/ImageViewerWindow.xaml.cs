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

    // ── 多張導覽 ──
    private readonly IReadOnlyList<(BitmapSource Image, string Name)> _images;
    private int _currentIndex;

    /// <summary>單張模式（不顯示上一張/下一張）</summary>
    public ImageViewerWindow(BitmapSource image, string slotName)
        : this([(image, slotName)], 0) { }

    /// <summary>多張模式，傳入完整清單與起始索引</summary>
    public ImageViewerWindow(
        IReadOnlyList<(BitmapSource Image, string Name)> images,
        int startIndex)
    {
        InitializeComponent();
        _images = images;
        _currentIndex = Math.Clamp(startIndex, 0, Math.Max(0, images.Count - 1));

        Loaded += (_, _) =>
        {
            ShowImage(_currentIndex);
            UpdateNavButtons();
        };
    }

    private void ShowImage(int index)
    {
        if (_images.Count == 0) return;
        _currentIndex = Math.Clamp(index, 0, _images.Count - 1);

        var (bmp, name) = _images[_currentIndex];
        PreviewImage.Source = bmp;
        TitleText.Text = name;
        IndexText.Text = _images.Count > 1 ? $"{_currentIndex + 1} / {_images.Count}" : "";

        FitToWindow();
        UpdateNavButtons();
    }

    private void UpdateNavButtons()
    {
        bool multi = _images.Count > 1;
        PrevButton.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        IndexText.Visibility  = multi ? Visibility.Visible : Visibility.Collapsed;

        PrevButton.IsEnabled = _currentIndex > 0;
        NextButton.IsEnabled = _currentIndex < _images.Count - 1;
    }

    private void FitToWindow()
    {
        if (PreviewImage.Source is not BitmapSource bmp) return;

        double availW = ImageCanvas.ActualWidth > 0 ? ImageCanvas.ActualWidth : ActualWidth;
        double availH = ImageCanvas.ActualHeight > 0 ? ImageCanvas.ActualHeight : ActualHeight - 32;
        double scaleX = availW / bmp.PixelWidth;
        double scaleY = availH / bmp.PixelHeight;
        _zoom = Math.Min(scaleX, scaleY) * 0.95;

        // 讓圖像置中：translate = (availSize - imageSize * zoom) / 2
        _translateX = (availW - bmp.PixelWidth  * _zoom) / 2.0;
        _translateY = (availH - bmp.PixelHeight * _zoom) / 2.0;
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

    // ── 導覽按鈕 ──
    private void Prev_Click(object sender, RoutedEventArgs e) => ShowImage(_currentIndex - 1);
    private void Next_Click(object sender, RoutedEventArgs e) => ShowImage(_currentIndex + 1);

    // ── 鍵盤左/右鍵切換 ──
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left)  { ShowImage(_currentIndex - 1); e.Handled = true; }
        if (e.Key == Key.Right) { ShowImage(_currentIndex + 1); e.Handled = true; }
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

    // ── 縮放按鈕 ──
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
