using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FormsScreen = System.Windows.Forms.Screen;

namespace PrintFreeTool;

public partial class PrintScreenSettingsWindow : Window
{
    private readonly List<System.Windows.Controls.CheckBox> _monitorChecks = [];

    public PrintScreenSettingsWindow(PrintScreenSettings current)
    {
        InitializeComponent();
        Icon = AppIcon.CreateImageSource();
        SelectedSettings = new PrintScreenSettings(current.Mode, current.SelectedMonitorIds.ToList());
        PopulateMonitors(current);

        if (current.Mode == PrintScreenCaptureMode.AllScreens)
        {
            AllScreensRadio.IsChecked = true;
        }
        else if (current.Mode == PrintScreenCaptureMode.EachScreenSeparately)
        {
            SeparateScreensRadio.IsChecked = true;
        }
        else
        {
            SelectedScreensRadio.IsChecked = true;
        }

        UpdateMonitorAvailability();
    }

    public PrintScreenSettings SelectedSettings { get; private set; }

    private void PopulateMonitors(PrintScreenSettings current)
    {
        FormsScreen[] screens = FormsScreen.AllScreens;
        AllScreensDescription.Text = screens.Length == 1
            ? "1 monitor detectado pelo Windows"
            : $"{screens.Length} monitores em uma única imagem";

        for (int index = 0; index < screens.Length; index++)
        {
            FormsScreen screen = screens[index];
            var label = new Grid();
            label.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            label.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var number = new Border
            {
                Width = 38,
                Height = 30,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 44, 61)),
                Child = new TextBlock
                {
                    Text = (index + 1).ToString(),
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                }
            };

            var details = new StackPanel { Margin = new Thickness(11, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            details.Children.Add(new TextBlock
            {
                Text = screen.Primary ? $"Tela {index + 1}  •  Principal" : $"Tela {index + 1}",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            });
            details.Children.Add(new TextBlock
            {
                Text = $"{screen.Bounds.Width} × {screen.Bounds.Height}  •  {screen.DeviceName}",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(128, 134, 154)),
                FontSize = 10,
                Margin = new Thickness(0, 4, 0, 0)
            });

            Grid.SetColumn(details, 1);
            label.Children.Add(number);
            label.Children.Add(details);

            bool isSelected = current.SelectedMonitorIds.Contains(screen.DeviceName, StringComparer.OrdinalIgnoreCase);
            if (current.Mode == PrintScreenCaptureMode.AllScreens && current.SelectedMonitorIds.Count == 0)
            {
                isSelected = screen.Primary;
            }

            var check = new System.Windows.Controls.CheckBox
            {
                Style = (Style)FindResource("MonitorCheck"),
                Content = label,
                Tag = screen.DeviceName,
                IsChecked = isSelected
            };
            _monitorChecks.Add(check);
            MonitorListPanel.Children.Add(check);
        }
    }

    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (MonitorListPanel is not null)
        {
            UpdateMonitorAvailability();
        }
    }

    private void UpdateMonitorAvailability()
    {
        MonitorListPanel.IsEnabled = SelectedScreensRadio.IsChecked == true;
        ValidationText.Visibility = Visibility.Collapsed;
    }

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        List<string> selectedIds = _monitorChecks
            .Where(check => check.IsChecked == true)
            .Select(check => (string)check.Tag)
            .ToList();

        if (SelectedScreensRadio.IsChecked == true && selectedIds.Count == 0)
        {
            ValidationText.Text = "Selecione pelo menos uma tela para continuar.";
            ValidationText.Visibility = Visibility.Visible;
            return;
        }

        PrintScreenCaptureMode mode = SeparateScreensRadio.IsChecked == true
            ? PrintScreenCaptureMode.EachScreenSeparately
            : SelectedScreensRadio.IsChecked == true
                ? PrintScreenCaptureMode.SelectedScreens
                : PrintScreenCaptureMode.AllScreens;

        SelectedSettings = new PrintScreenSettings(mode, selectedIds);
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source)) return;
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is System.Windows.Controls.Button) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }
}
