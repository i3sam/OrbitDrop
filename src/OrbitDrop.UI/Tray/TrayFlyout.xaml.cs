using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using OrbitDrop.Windows.Interop;
using OrbitDrop.Windows.Shell;

namespace OrbitDrop.UI.Tray;

public interface ITrayCommands
{
    bool Paused { get; set; }
    string TriggerKeyName { get; }
    void OpenSettings(string? page = null);
    void ShowOnboarding();
    void Quit();
    /// <summary>"Check for updates", or "Install OrbitDrop x.y.z" once a newer release is known.</summary>
    string UpdateMenuText => "Check for updates";
    bool UpdateAvailable => false;
    void UpdateMenuClicked() { }
    /// <summary>Opens the help form on the website (see the app's OpenSupport).</summary>
    void OpenSupport(string topic, string from, string? details = null) { }
}

/// <summary>Notification-area menu in the OrbitDrop design language. Closes when it loses focus or on Esc.</summary>
public partial class TrayFlyout : Window
{
    private readonly ITrayCommands _commands;
    private bool _closing;

    public TrayFlyout(ITrayCommands commands)
    {
        InitializeComponent();
        _commands = commands;
        Shadow.Opacity = ThemeManager.Number("ToastShadowOpacity");
        Refresh();
        Deactivated += (_, _) => SafeClose();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) SafeClose(); };
    }

    /// <summary>The content root, for offscreen design-review renders.</summary>
    public FrameworkElement ContentRoot => Root;

    private void Refresh()
    {
        bool paused = _commands.Paused;
        StatusText.Text = paused ? "Paused" : $"Ready · drag files, tap {_commands.TriggerKeyName}";
        StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, paused ? "Orbit.Warning" : "Orbit.Success");
        PauseGlyph.Text = paused ? "" : "";
        PauseText.Text = paused ? "Resume" : "Pause";
        UpdateText.Text = _commands.UpdateMenuText;
        UpdateGlyph.Text = _commands.UpdateAvailable ? "" : ""; // Download : Sync
        UpdateDot.Visibility = _commands.UpdateAvailable ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Opens above (or below) the tray anchor point, right-aligned to it, inside the work area.</summary>
    public void ShowAt(Win32.POINT anchor)
    {
        Opacity = 0;
        Show();
        UpdateLayout();
        var monitor = ScreenInfo.MonitorAt(anchor);
        double scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? monitor.Scale;
        int w = (int)Math.Round(ActualWidth * scale), h = (int)Math.Round(ActualHeight * scale);
        var work = monitor.WorkArea;
        bool above = anchor.Y > (work.Top + work.Bottom) / 2;
        int x = Math.Clamp(anchor.X - w + (int)(16 * scale), work.Left, work.Right - w);
        int y = above ? Math.Max(work.Top, Math.Min(anchor.Y, work.Bottom) - h + (int)(12 * scale)) : Math.Min(work.Bottom - h, anchor.Y);
        Win32.SetWindowPos(new WindowInteropHelper(this).Handle, Win32.HWND_TOPMOST, x, y, 0, 0, Win32.SWP_NOSIZE);
        Activate();
        Win32.SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Opacity = 1;
        if (!ThemeManager.Current.ReduceMotion)
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
    }

    private void SafeClose()
    {
        if (_closing) return;
        _closing = true;
        Close();
    }

    private void Run(Action action)
    {
        SafeClose();
        Dispatcher.BeginInvoke(action);
    }

    private void OnPause(object sender, RoutedEventArgs e) => Run(() => _commands.Paused = !_commands.Paused);
    private void OnSettings(object sender, RoutedEventArgs e) => Run(() => _commands.OpenSettings());
    private void OnHelp(object sender, RoutedEventArgs e) => Run(_commands.ShowOnboarding);
    private void OnSupport(object sender, RoutedEventArgs e) => Run(() => _commands.OpenSupport("help", "tray"));
    private void OnUpdate(object sender, RoutedEventArgs e) => Run(_commands.UpdateMenuClicked);
    private void OnAbout(object sender, RoutedEventArgs e) => Run(() => _commands.OpenSettings("About"));
    private void OnExit(object sender, RoutedEventArgs e) => Run(_commands.Quit);
}
