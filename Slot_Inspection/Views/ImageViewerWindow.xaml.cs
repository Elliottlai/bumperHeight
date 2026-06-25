using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Slot_Inspection.Models;

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

    // ── 即時更新：持有活的 Slot 來源 ──
    private readonly IReadOnlyList<ObservableCollection<SlotItem>> _sources;
    private SlotItem? _currentSlot;
    private List<SlotItem> _snapshot = [];
    private int _currentIndex;

    /// <summary>
    /// 即時模式：傳入活的 ObservableCollection 清單與起始 Slot 物件。
    /// RUN 過程中新圖拍完會自動更新顯示。
    /// </summary>
    public ImageViewerWindow(
        IReadOnlyList<ObservableCollection<SlotItem>> sources,
        SlotItem startSlot)
    {
        InitializeComponent();
        _sources     = sources;
        _currentSlot = startSlot;

        foreach (var col in sources)
        {
            col.CollectionChanged += OnCollectionChanged;
            foreach (var s in col)
                s.PropertyChanged += OnSlotPropertyChanged;
        }

        Closed += (_, _) =>
        {
            foreach (var col in _sources)
            {
                col.CollectionChanged -= OnCollectionChanged;
                foreach (var s in col)
                    s.PropertyChanged -= OnSlotPropertyChanged;
            }
        };

        Loaded += (_, _) =>
        {
            RefreshSnapshot();
            int idx = _snapshot.IndexOf(startSlot);
            NavigateTo(idx < 0 ? 0 : idx);
        };
    }

    // ── Collection / Property 變動回調 ──

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
            foreach (SlotItem s in e.NewItems)
                s.PropertyChanged += OnSlotPropertyChanged;
        if (e.OldItems != null)
            foreach (SlotItem s in e.OldItems)
                s.PropertyChanged -= OnSlotPropertyChanged;
        Dispatcher.InvokeAsync(OnSourceUpdated);
    }

    private void OnSlotPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SlotItem.ImageSource)) return;
        Dispatcher.InvokeAsync(OnSourceUpdated);
    }

    private void OnSourceUpdated()
    {
        int prevCount = _snapshot.Count;
        RefreshSnapshot();

        // 目前正在看的 Slot 圖片有更新 → 重新顯示
        if (_currentSlot != null)
        {
            int idx = _snapshot.IndexOf(_currentSlot);
            if (idx >= 0)
            {
                _currentIndex = idx;
                ShowCurrentSlot();
                UpdateNavButtons();
                return;
            }
        }

        // 有新圖拍入 → 自動跳到最新那張
        if (_snapshot.Count > prevCount)
            NavigateTo(_snapshot.Count - 1);
        else
            UpdateNavButtons();
    }

    private void RefreshSnapshot()
    {
        _snapshot = _sources
            .SelectMany(c => c)
            .Where(s => s.ImageSource is BitmapSource)
            .ToList();
    }

    private void NavigateTo(int index)
    {
        if (_snapshot.Count == 0) return;
        _currentIndex = Math.Clamp(index, 0, _snapshot.Count - 1);
        _currentSlot  = _snapshot[_currentIndex];
        ShowCurrentSlot();
        UpdateNavButtons();
    }

    private void ShowCurrentSlot()
    {
        if (_snapshot.Count == 0 || _currentSlot == null) return;
        PreviewImage.Source = (BitmapSource)_currentSlot.ImageSource!;
        DefectBoxesOverlay.ItemsSource = _currentSlot.DefectBoxes;
        TitleText.Text      = _currentSlot.Name;
        IndexText.Text      = _snapshot.Count > 1 ? $"{_currentIndex + 1} / {_snapshot.Count}" : "";
        FitToWindow();
    }

    private void UpdateNavButtons()
    {
        bool multi = _snapshot.Count > 1;
        PrevButton.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = multi ? Visibility.Visible : Visibility.Collapsed;
        IndexText.Visibility  = multi ? Visibility.Visible : Visibility.Collapsed;
        PrevButton.IsEnabled  = _currentIndex > 0;
        NextButton.IsEnabled  = _currentIndex < _snapshot.Count - 1;
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

        // 缺陷外框疊圖層跟著圖片同步縮放/平移，避免脫節
        BoxScaleT.ScaleX = _zoom;
        BoxScaleT.ScaleY = _zoom;
        BoxTranslateT.X = _translateX;
        BoxTranslateT.Y = _translateY;

        ZoomText.Text = $"{_zoom * 100:F0}%";
    }

    // ── 導覽按鈕 ──
    private void Prev_Click(object sender, RoutedEventArgs e) => NavigateTo(_currentIndex - 1);
    private void Next_Click(object sender, RoutedEventArgs e) => NavigateTo(_currentIndex + 1);

    // ── 鍵盤左/右鍵切換 ──
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Left)  { NavigateTo(_currentIndex - 1); e.Handled = true; }
        if (e.Key == Key.Right) { NavigateTo(_currentIndex + 1); e.Handled = true; }
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
