using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace NJPlayer.App.Views;

/// <summary>
/// On-screen notification toast positioned bottom-center with 2.0s auto-hide per DESIGN.md section 7.
/// </summary>
public partial class ToastOverlay : UserControl
{
    private readonly DispatcherTimer _timer;
    private readonly DoubleAnimation _fadeIn;
    private readonly DoubleAnimation _fadeOut;

    public ToastOverlay()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.0)
        };
        _timer.Tick += Timer_Tick;

        _fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120));
        _fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180));
    }

    public void ShowToast(string message, string icon = "⚡", bool isError = false)
    {
        _timer.Stop();

        ToastText.Text = message;
        ToastIcon.Text = icon;

        if (isError)
        {
            ToastIcon.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            ToastBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("DangerBrush");
        }
        else
        {
            ToastIcon.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
            ToastBorder.BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrush");
        }

        BeginAnimation(OpacityProperty, _fadeIn);
        _timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _timer.Stop();
        BeginAnimation(OpacityProperty, _fadeOut);
    }
}
