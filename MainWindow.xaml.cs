using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PrintFreeTool;

public partial class MainWindow : Window
{
    public MainWindow(HotkeyShortcut shortcut)
    {
        InitializeComponent();
        Icon = AppIcon.CreateImageSource();
        UpdateShortcut(shortcut);
    }

    public bool AllowClose { get; set; }
    public event Action? CaptureRequested;
    public event Action? OpenFolderRequested;
    public event Action? SettingsRequested;

    public void UpdateShortcut(HotkeyShortcut shortcut)
    {
        // The shortcut is shown in Settings; the compact home keeps only its two actions.
    }

    private void OnHeaderMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is System.Windows.Controls.Button)
            {
                return true;
            }

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e) => Hide();

    private void OnCaptureClick(object sender, RoutedEventArgs e) => CaptureRequested?.Invoke();

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke();

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }
}
