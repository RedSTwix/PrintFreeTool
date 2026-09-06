using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PrintFreeTool;

public partial class SettingsWindow : Window
{
    private bool _isRecording;

    public SettingsWindow(HotkeyShortcut currentShortcut, PrintScreenSettings printScreenSettings)
    {
        InitializeComponent();
        Icon = AppIcon.CreateImageSource();
        SelectedShortcut = currentShortcut;
        SelectedPrintScreenSettings = printScreenSettings;
        HotkeyText.Text = currentShortcut.DisplayText;
        UpdatePrintScreenSummary();
    }

    public HotkeyShortcut SelectedShortcut { get; private set; }
    public PrintScreenSettings SelectedPrintScreenSettings { get; private set; }

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        _isRecording = true;
        HotkeyText.Text = "Pressione as teclas...";
        HintText.Text = "Segure um modificador e pressione outra tecla.";
        HintText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(117, 232, 200));
        RecorderButton.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(99, 102, 241));
        Keyboard.Focus(this);
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isRecording)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
            }
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            StopRecording(SelectedShortcut, "Alteração cancelada. O atalho anterior foi mantido.");
            e.Handled = true;
            return;
        }

        if (HotkeyShortcut.IsModifierKey(key))
        {
            HintText.Text = "Agora pressione uma tecla junto com o modificador.";
            e.Handled = true;
            return;
        }

        HotkeyShortcut? shortcut = HotkeyShortcut.FromPressedKey(key);
        if (shortcut is null)
        {
            HintText.Text = "Inclua Ctrl, Alt, Shift ou Win na combinação.";
            HintText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 139, 148));
            e.Handled = true;
            return;
        }

        if (shortcut == HotkeyShortcut.EditorDefault || shortcut == HotkeyShortcut.ActiveWindowDefault)
        {
            HintText.Text = shortcut == HotkeyShortcut.EditorDefault
                ? "Win + Shift + E está reservado para a captura com editor."
                : "Win + Shift + A está reservado para capturar a janela ativa.";
            HintText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 139, 148));
            e.Handled = true;
            return;
        }

        SelectedShortcut = shortcut;
        StopRecording(shortcut, "Novo atalho reconhecido. Clique em Salvar atalho.");
        e.Handled = true;
    }

    private void StopRecording(HotkeyShortcut shortcut, string hint)
    {
        _isRecording = false;
        HotkeyText.Text = shortcut.DisplayText;
        HintText.Text = hint;
        HintText.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(125, 131, 152));
        RecorderButton.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 57, 77));
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnConfigurePrintScreenClick(object sender, RoutedEventArgs e)
    {
        var window = new PrintScreenSettingsWindow(SelectedPrintScreenSettings)
        {
            Owner = this
        };

        if (window.ShowDialog() == true)
        {
            SelectedPrintScreenSettings = window.SelectedSettings;
            UpdatePrintScreenSummary();
        }
    }

    private void UpdatePrintScreenSummary()
    {
        int availableCount = System.Windows.Forms.Screen.AllScreens.Length;
        if (SelectedPrintScreenSettings.Mode == PrintScreenCaptureMode.AllScreens)
        {
            PrintScreenSummaryText.Text = availableCount == 1 ? "Todas as telas (1)" : $"Todas as telas ({availableCount})";
            return;
        }

        if (SelectedPrintScreenSettings.Mode == PrintScreenCaptureMode.EachScreenSeparately)
        {
            PrintScreenSummaryText.Text = availableCount == 1
                ? "Uma tela em arquivo separado"
                : $"Cada tela em arquivo separado ({availableCount})";
            return;
        }

        int selectedCount = System.Windows.Forms.Screen.AllScreens.Count(screen =>
            SelectedPrintScreenSettings.SelectedMonitorIds.Contains(screen.DeviceName, StringComparer.OrdinalIgnoreCase));
        PrintScreenSummaryText.Text = selectedCount switch
        {
            0 => "Tela principal",
            1 => "Uma tela selecionada",
            _ => $"{selectedCount} telas selecionadas"
        };
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source))
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
}
