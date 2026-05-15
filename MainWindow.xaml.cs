using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        [DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint esFlags);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private const uint GW_OWNER = 4;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        private const uint ES_CONTINUOUS = 0x80000000;
        private const uint ES_SYSTEM_REQUIRED = 0x00000001;
        private const uint ES_DISPLAY_REQUIRED = 0x00000002;

        private CancellationTokenSource? _cts;
        private NotifyIcon? _trayIcon;
        private DateTime _startTime;
        private TimeSpan _elapsed;
        private DispatcherTimer _uiTimer = null!;
        private bool _isRunning;

        private volatile bool _isPaused;
        private volatile int _countdownSeconds;

        public ObservableCollection<AppEntry> Apps { get; } = new();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SetupTrayIcon();
            _elapsed = TimeSpan.Zero;

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _uiTimer.Tick += UiTimer_Tick;

            RefreshAppList();
            AppListBox.ItemsSource = Apps;
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            StopRunning();
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshAppList();

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = Apps.Where(a => a.IsSelected).ToList();
            if (selected.Count == 0)
            {
                StatusText.Text = "Select at least one app!";
                return;
            }

            _isRunning = true;
            _cts = new CancellationTokenSource();
            _startTime = DateTime.UtcNow;
            _elapsed = TimeSpan.Zero;

            SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED);
            _uiTimer.Start();

            Task.Run(() => NudgeLoop(_cts.Token));

            for (int i = 0; i < selected.Count; i++)
            {
                var processName = selected[i].ProcessName;
                var initialDelay = TimeSpan.FromMinutes(i);
                Task.Run(() => AppMinimizeLoop(processName, initialDelay, _cts.Token));
            }

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            AppListBox.IsEnabled = false;
            StatusText.Text = "Keeping awake...";
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopRunning();
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            AppListBox.IsEnabled = true;
            StatusText.Text = "Stopped";
            TimerText.Text = "00:00:00";
        }

        private void StopRunning()
        {
            _uiTimer.Stop();
            _cts?.Cancel();
            _cts = null;
            _isRunning = false;
            SetThreadExecutionState(ES_CONTINUOUS);
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new NotifyIcon
            {
                Text = "Digitālais virinātājs",
                Icon = SystemIcons.Application,
                Visible = true
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Close));
            _trayIcon.ContextMenuStrip = menu;
        }

        private void RefreshAppList()
        {
            var previouslySelected = Apps.Where(a => a.IsSelected)
                                         .Select(a => a.ProcessName)
                                         .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Apps.Clear();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var proc in Process.GetProcesses())
            {
                if (proc.MainWindowHandle == IntPtr.Zero) continue;
                if (string.IsNullOrWhiteSpace(proc.MainWindowTitle)) continue;
                if (_blockedProcesses.Contains(proc.ProcessName)) continue;
                if (!IsTaskbarWindow(proc.MainWindowHandle)) continue;
                if (!seen.Add(proc.ProcessName)) continue;

                Apps.Add(new AppEntry
                {
                    ProcessName = proc.ProcessName,
                    DisplayName = $"{proc.MainWindowTitle}  ({proc.ProcessName})",
                    IsSelected = previouslySelected.Contains(proc.ProcessName)
                });
            }
        }

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            if (_isPaused)
                StatusText.Text = $"Paused — resuming in {_countdownSeconds}s";
            else if (_isRunning)
                StatusText.Text = "Keeping awake...";

            if (_isRunning)
            {
                var total = _elapsed + (DateTime.UtcNow - _startTime);
                TimerText.Text = total.ToString(@"hh\:mm\:ss");
            }
        }

        private async Task NudgeLoop(CancellationToken ct)
        {
            var rng = new Random();
            GetCursorPos(out var lastPos);

            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(2 + rng.NextDouble() * 2), ct);
                if (ct.IsCancellationRequested) break;

                GetCursorPos(out var currentPos);

                if (currentPos.X != lastPos.X || currentPos.Y != lastPos.Y)
                {
                    _isPaused = true;
                    for (int i = 5; i > 0; i--)
                    {
                        _countdownSeconds = i;
                        await Task.Delay(1000, ct);
                    }
                    _isPaused = false;
                    GetCursorPos(out lastPos);
                    continue;
                }

                SetCursorPos(currentPos.X + 1, currentPos.Y);
                await Task.Delay(50, ct);
                SetCursorPos(currentPos.X, currentPos.Y);
                lastPos = currentPos;
            }
        }

        private async Task AppMinimizeLoop(string processName, TimeSpan initialDelay, CancellationToken ct)
        {
            if (initialDelay > TimeSpan.Zero)
                await Task.Delay(initialDelay, ct);

            while (!ct.IsCancellationRequested)
            {
                var handles = GetWindowHandles(processName);
                foreach (var hWnd in handles)
                {
                    ShowWindow(hWnd, SW_RESTORE);
                    SetForegroundWindow(hWnd);
                }

                await Task.Delay(TimeSpan.FromMinutes(5), ct);
                if (ct.IsCancellationRequested) break;

                handles = GetWindowHandles(processName);
                foreach (var hWnd in handles)
                {
                    ShowWindow(hWnd, SW_MINIMIZE);
                }

                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        private static readonly HashSet<string> _blockedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "TextInputHost", "ApplicationFrameHost", "SystemSettings",
            "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost",
            "LockApp", "ScreenClippingHost", "CompactOverlay"
        };

        private static bool IsTaskbarWindow(IntPtr hWnd)
        {
            if (!IsWindowVisible(hWnd)) return false;

            // Exclude windows that are cloaked by DWM (modern/UWP apps may be cloaked when not shown on taskbar)
            const int DWMWA_CLOAKED = 14;
            try
            {
                if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return false;
            }
            catch
            {
                // If DWM call fails, don't block based on cloak state
            }

            // Exclude windows with no visible area
            try
            {
                if (GetWindowRect(hWnd, out var rect))
                {
                    if (rect.Right - rect.Left <= 0 || rect.Bottom - rect.Top <= 0)
                        return false;
                }
            }
            catch { }

            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

            // Explicitly marked as app window -> show it
            if ((exStyle & WS_EX_APPWINDOW) != 0) return true;

            // Tool windows never appear on taskbar
            if ((exStyle & WS_EX_TOOLWINDOW) != 0) return false;

            // No-activate windows are not taskbar windows
            if ((exStyle & WS_EX_NOACTIVATE) != 0) return false;

            // If the window has a visible owner, it's not a taskbar window
            IntPtr owner = GetWindow(hWnd, GW_OWNER);
            if (owner != IntPtr.Zero) return false;

            return true;
        }

        private static List<IntPtr> GetWindowHandles(string processName)
        {
            var handles = new List<IntPtr>();
            foreach (var proc in Process.GetProcessesByName(processName))
            {
                if (proc.MainWindowHandle != IntPtr.Zero)
                    handles.Add(proc.MainWindowHandle);
            }
            return handles;
        }
    }

    public class AppEntry : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string ProcessName { get; set; } = "";
        public string DisplayName { get; set; } = "";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}