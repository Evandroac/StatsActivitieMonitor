using System.Windows;
using System.Windows.Media.Animation;
using StatsMonitor.Helpers;

namespace StatsMonitor.Views;

public partial class SplashWindow : Window
{
    private double _target;

    public SplashWindow()
    {
        InitializeComponent();
        SubtitleText.Text = L("Monitor de hardware e desempenho", "Hardware and performance monitor");
        SetStatus(L("Iniciando…", "Starting…"), 0);
    }

    private static string L(string pt, string en) => Loc.Lang == "en" ? en : pt;

    public static string Text(string pt, string en) => L(pt, en);

    /// <summary>Appends a timestamped log line and updates the status bar.</summary>
    public void Log(string message, double percent)
    {
        LogText.Text += (LogText.Text.Length > 0 ? "\n" : "") + $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
        LogScroll.ScrollToEnd();
        SetStatus(message, percent);
    }

    private void SetStatus(string status, double percent)
    {
        StatusText.Text = status;
        _target = Math.Clamp(percent, 0, 100);
        PercentText.Text = $"{_target:0}%";
        var width = Math.Max(0, ((FrameworkElement)Fill.Parent).ActualWidth > 0 ? ((FrameworkElement)Fill.Parent).ActualWidth : Width - 14 * 2 - 56);
        var anim = new DoubleAnimation(width * _target / 100, TimeSpan.FromMilliseconds(250)) { EasingFunction = new QuadraticEase() };
        Fill.BeginAnimation(WidthProperty, anim);
    }

    public async Task FadeOutAsync()
    {
        var done = new TaskCompletionSource();
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        anim.Completed += (_, _) => done.TrySetResult();
        BeginAnimation(OpacityProperty, anim);
        await done.Task;
    }
}
