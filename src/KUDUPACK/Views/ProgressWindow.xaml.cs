using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Kudupack.Core;

namespace Kudupack.Views
{
    public enum OperationOutcome { Done, Cancelled, Failed }

    public sealed class OperationResult
    {
        public OperationOutcome Outcome { get; set; }
        public Exception Error { get; set; }
        public bool Ok => Outcome == OperationOutcome.Done;
    }

    /// <summary>Runs work on a background thread while showing progress and a Cancel button.</summary>
    public partial class ProgressWindow : Window
    {
        private readonly Action<ProgressReporter> _work;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _finished;

        private ProgressWindow(string title, string status, Action<ProgressReporter> work)
        {
            InitializeComponent();
            ThemeManager.Attach(this);
            _work = work;
            TitleText.Text = title;
            StatusText.Text = status;
            Title = title + " - KUDUPACK";
            Loaded += async (s, e) => await RunAsync();
        }

        public OperationResult Result { get; private set; } = new OperationResult { Outcome = OperationOutcome.Cancelled };

        public static OperationResult Run(Window owner, string title, string status, Action<ProgressReporter> work)
        {
            var window = new ProgressWindow(title, status, work);
            if (owner != null && owner.IsVisible) window.Owner = owner;
            else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowDialog();
            return window.Result;
        }

        private async Task RunAsync()
        {
            var percent = new Progress<int>(p =>
            {
                Bar.IsIndeterminate = false;
                Bar.Value = p;
                PercentText.Text = p + "%";
            });
            var status = new Progress<string>(text => StatusText.Text = text);
            var reporter = new ProgressReporter(percent, status, _cts.Token, AskPassword);

            try
            {
                await Task.Run(() => _work(reporter));
                Result = _cts.IsCancellationRequested
                    ? new OperationResult { Outcome = OperationOutcome.Cancelled }
                    : new OperationResult { Outcome = OperationOutcome.Done };
            }
            catch (OperationCanceledException)
            {
                Result = new OperationResult { Outcome = OperationOutcome.Cancelled };
            }
            catch (Exception ex)
            {
                Result = new OperationResult { Outcome = OperationOutcome.Failed, Error = ex };
            }
            finally
            {
                _finished = true;
                Close();
            }
        }

        /// <summary>Called from the background thread; shows the password box on the UI thread.</summary>
        private string AskPassword(string message)
        {
            return Dispatcher.Invoke(() => PasswordWindow.Ask(this, message));
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _cts.Cancel();
            CancelButton.IsEnabled = false;
            StatusText.Text = "Stopping…";
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_finished)
            {
                // Wait for the work to stop cleanly before closing.
                e.Cancel = true;
                Cancel_Click(this, null);
            }
            base.OnClosing(e);
        }
    }
}
