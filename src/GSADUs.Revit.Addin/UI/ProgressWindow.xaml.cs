using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace GSADUs.Revit.Addin.UI
{
    public partial class ProgressWindow : Window
    {
        public event EventHandler? CancelRequested;
        private bool _cancelClicked;
        private bool _allowClose; // allow programmatic close without triggering cancel

        public ProgressWindow()
        {
            InitializeComponent();
            this.KeyDown += ProgressWindow_KeyDown;
        }

        private void ProgressWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                RequestCancel();
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            RequestCancel();
        }

        private void RequestCancel()
        {
            if (_cancelClicked) return;
            _cancelClicked = true;
            try
            {
                CancelBtn.IsEnabled = false;
                StatusText.Text = "Cancelling after current step...";
                CancelRequested?.Invoke(this, EventArgs.Empty);
            }
            catch { }
        }

        public void Update(string setName, int index, int total, double overallPercent, TimeSpan elapsed)
        {
            try
            {
                HeaderText.Text = $"Exporting {setName} ({index} of {total})";
                OverallBar.Value = Math.Max(0, Math.Min(100, overallPercent));
                ElapsedText.Text = $"Elapsed: {elapsed:hh\\:mm\\:ss}";
            }
            catch { }
        }

        // Allow programmatic close without triggering cancel
        public void ForceClose()
        {
            _allowClose = true;
            try { Close(); } catch { }
        }

        // Pump pending UI messages so the window repaints and button clicks are handled
        public static void DoEvents()
        {
            try
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(ExitFrame), frame);
                Dispatcher.PushFrame(frame);
            }
            catch { }
        }

        private static object? ExitFrame(object arg)
        {
            try { ((DispatcherFrame)arg).Continue = false; } catch { }
            return null;
        }

        // Treat user-initiated window close as soft cancel
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_allowClose && !_cancelClicked)
            {
                e.Cancel = true; // keep window open, trigger soft cancel instead
                RequestCancel();
                return;
            }
            base.OnClosing(e);
        }
    }
}
