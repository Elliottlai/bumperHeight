using System.Windows;

namespace WebApiSever
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            CIMapiController.OnRequestReceived += AppendLog;
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText(message + Environment.NewLine);
                Scroller.ScrollToEnd();
            });
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
        }

        protected override void OnClosed(EventArgs e)
        {
            CIMapiController.OnRequestReceived -= AppendLog;
            base.OnClosed(e);
        }
    }
}