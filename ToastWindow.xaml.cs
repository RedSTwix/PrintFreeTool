using System.Media;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PrintFreeTool;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _closeTimer;

    public ToastWindow(string title, string message, bool isError)
    {
        InitializeComponent();
        Icon = AppIcon.CreateImageSource();
        TitleText.Text = title;
        MessageText.Text = message;

        if (isError)
        {
            StatusIcon.Text = "!";
            StatusIconBackground.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68));
            SystemSounds.Exclamation.Play();
        }

        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(isError ? 6 : 4) };
        _closeTimer.Tick += (_, _) => BeginClose();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 18;
        Top = workArea.Bottom - ActualHeight - 18;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170));
        BeginAnimation(OpacityProperty, fadeIn);
        _closeTimer.Start();
    }

    private void BeginClose()
    {
        _closeTimer.Stop();
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(160));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => BeginClose();

    protected override void OnClosed(EventArgs e)
    {
        _closeTimer.Stop();
        base.OnClosed(e);
    }
}
