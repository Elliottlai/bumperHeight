using System.Text;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FoupInspecMachine.Manager;
using Machine.Core;
using Machine.Core.Interfaces;
using NLog;
using HalconDotNet;
using CameraLightTest.LightControllers;
using Brushes = System.Windows.Media.Brushes;

namespace CameraLightTest
{
    public partial class MainWindow : Window
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private HighBright_Controller? _light;
        private ICamera? _camera;
        private bool _initialized;

        // ── 防抖 Timer（300ms 內無新動作才送出指令）────────
        private readonly DispatcherTimer _sliderDebounce;

        public MainWindow()
        {
            InitializeComponent();

            SliderIntensity.ValueChanged += (s, e) =>
                TxtIntensityValue.Text = $"{(int)SliderIntensity.Value}%";

            // 初始化防抖 Timer
            _sliderDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _sliderDebounce.Tick += SliderDebounce_Tick;
        }

        // ═══════════════════════════════════════
        //  Slider 即時控光（防抖）
        // ═══════════════════════════════════════

        private void SliderIntensity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_initialized || _light == null) return;

            // 每次有新值進來就重置計時器
            _sliderDebounce.Stop();
            _sliderDebounce.Start();
        }

        private void SliderDebounce_Tick(object? sender, EventArgs e)
        {
            _sliderDebounce.Stop(); // 單次觸發

            if (_light == null || !_light.IsOpen) return;

            int ch        = int.TryParse(TxtLightChannel.Text, out int c) ? c : 1;
            int intensity = (int)SliderIntensity.Value;

            bool ok = _light.SetValue(ch, intensity);
            Log($"[Slider] CH{ch} = {intensity}%{(ok ? "" : " [失敗]")}");
        }

        // ═══════════════════════════════════════
        //  初始化
        // ═══════════════════════════════════════

        private void BtnInit_Click(object sender, RoutedEventArgs e)
        {
            BtnInit.IsEnabled = false;
            TxtInitStatus.Text = "初始化中...";
            TxtInitStatus.Foreground = Brushes.Orange;

            try
            {
                // 1. Machine.Core
                Log("初始化 Machine.Core...");
                cMachineManager.Init();
                Log("  → OK");

                // 填入可用相機列表
                CmbCamera.Items.Clear();
                foreach (var key in cMachineManager.Cameras.Keys)
                    CmbCamera.Items.Add(key);
                if (CmbCamera.Items.Count > 0)
                    CmbCamera.SelectedIndex = 0;

                // 2. 光源
                string comPort = TxtLightCom.Text.Trim();
                Log($"連接光源 ({comPort})...");
                _light = new HighBright_Controller(comPort);
                _light.Connect();
                if (!_light.IsOpen)
                    throw new Exception($"光源開啟失敗 ({comPort})");
                Log("  → OK");

                // 3. 相機
                string camName = CmbCamera.SelectedItem?.ToString() ?? "";
                if (string.IsNullOrEmpty(camName) || !cMachineManager.Cameras.TryGetValue(camName, out _camera))
                    throw new Exception($"找不到相機: {camName}");

                Log($"初始化相機 ({camName})...");
                if (!_camera.Init())
                    throw new Exception("相機 Init() 回傳 false");
                Log($"  → OK (W={_camera.FrameWidth}, H={_camera.BufHeight})");

                // 完成
                _initialized = true;
                TxtInitStatus.Text = "✓ 初始化完成";
                TxtInitStatus.Foreground = Brushes.LimeGreen;
                BtnLightOn.IsEnabled  = true;
                BtnLightOff.IsEnabled = true;
                BtnCapture.IsEnabled  = true;
                BtnSetExposure.IsEnabled = true;
                Log("===== 初始化完成，可開始測試 =====");
            }
            catch (Exception ex)
            {
                TxtInitStatus.Text = $"✗ 失敗: {ex.Message}";
                TxtInitStatus.Foreground = Brushes.Red;
                Log($"[ERROR] {ex.Message}");
                BtnInit.IsEnabled = true;
            }
        }

        // ═══════════════════════════════════════
        //  光源控制
        // ═══════════════════════════════════════

        private void BtnLightOn_Click(object sender, RoutedEventArgs e)
        {
            if (_light == null) return;
            int ch        = int.TryParse(TxtLightChannel.Text, out int c) ? c : 1;
            int intensity = (int)SliderIntensity.Value;
            bool ok = _light.SetValue(ch, intensity);
            Log($"光源 ON → CH{ch} = {intensity}%{(ok ? "" : " [失敗]")}");
        }

        private void BtnLightOff_Click(object sender, RoutedEventArgs e)
        {
            if (_light == null) return;
            int ch  = int.TryParse(TxtLightChannel.Text, out int c) ? c : 1;
            bool ok = _light.SetValue(ch, 0);
            Log($"光源 OFF → CH{ch}{(ok ? "" : " [失敗]")}");
        }

        // ═══════════════════════════════════════
        //  相機控制
        // ═══════════════════════════════════════

        private void BtnSetExposure_Click(object sender, RoutedEventArgs e)
        {
            if (_camera == null) return;
            if (!double.TryParse(TxtExposure.Text, out double exposure)) return;

            _camera.Stop();
            _camera.SetFeatureValue(GMExpandParamter.ExposureTime, exposure);
            _camera.Start();
            Log($"曝光時間已設為 {exposure} μs");
        }

        private async void BtnCapture_Click(object sender, RoutedEventArgs e)
        {
            if (_camera == null || _light == null) return;
            BtnCapture.IsEnabled = false;

            try
            {
                int ch        = int.TryParse(TxtLightChannel.Text, out int c) ? c : 1;
                int intensity = (int)SliderIntensity.Value;

                // 1. 開光
                bool ok = _light.SetValue(ch, intensity);
                Log($"[1/4] 光源 ON (CH{ch}={intensity}%){(ok ? "" : " [失敗]")}");
                await Task.Delay(100);

                // 2. 觸發相機
                _camera.Start();
                await Task.Delay(200);
                Log("[2/4] 相機觸發完成");

                // 3. 取像存檔
                string dir = TxtSavePath.Text.Trim();
                Directory.CreateDirectory(dir);
                string filename = Path.Combine(dir,
                    $"Capture_{DateTime.Now:yyyyMMdd_HHmmss_fff}.tif");

                IntPtr[] bufAddr = _camera.GetBufAddress(0);
                if (bufAddr != null && bufAddr.Length > 0 && bufAddr[0] != IntPtr.Zero)
                {
                    int w = _camera.FrameWidth;
                    int h = _camera.BufHeight;

                    var hImg = new HImage();
                    hImg.GenImage1Extern("byte", w, h, bufAddr[0], IntPtr.Zero);
                    hImg.WriteImage("tiff", 0, filename);
                    hImg.Dispose();

                    Log($"[3/4] 已存檔: {filename}");
                    ShowPreview(bufAddr[0], w, h);
                }
                else
                {
                    Log("[3/4] 警告: 無法取得影像 Buffer");
                }

                // 4. 關光
                _light.SetValue(ch, 0);
                Log("[4/4] 光源 OFF");
                Log("✓ 拍照完成\n");
            }
            catch (Exception ex)
            {
                Log($"[ERROR] {ex.Message}");
                try { _light?.SetValue(1, 0); } catch { }
            }
            finally
            {
                BtnCapture.IsEnabled = true;
            }
        }

        // ═══════════════════════════════════════
        //  影像預覽
        // ═══════════════════════════════════════

        private unsafe void ShowPreview(IntPtr bufPtr, int width, int height)
        {
            try
            {
                int stride = width;
                var bmp = BitmapSource.Create(
                    width, height, 96, 96,
                    PixelFormats.Gray8, null,
                    bufPtr, height * stride, stride);
                bmp.Freeze();
                ImgPreview.Source = bmp;
            }
            catch (Exception ex)
            {
                Log($"[WARN] 預覽顯示失敗: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════
        //  其他
        // ═══════════════════════════════════════

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                SelectedPath = TxtSavePath.Text
            };
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                TxtSavePath.Text = dlg.SelectedPath;
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            try
            {
                _sliderDebounce.Stop();
                _light?.TurnOffAll();
                _light?.Dispose();
                _camera?.Stop();
            }
            catch { }
        }

        private void Log(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}\r\n";
            TxtLog.AppendText(line);
            TxtLog.ScrollToEnd();
            _logger.Info(msg);
        }
    }
}
