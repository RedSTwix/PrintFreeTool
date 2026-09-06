using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace PrintFreeTool;

internal sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Drawing.Icon _icon;
    private readonly Forms.ToolStripMenuItem _captureMenuItem;

    public TrayController(HotkeyShortcut shortcut)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir PrintFreeTool", null, (_, _) => OpenRequested?.Invoke());
        _captureMenuItem = new Forms.ToolStripMenuItem(string.Empty, null, (_, _) => CaptureRequested?.Invoke());
        menu.Items.Add(_captureMenuItem);
        menu.Items.Add("Captura com editor   Win + Shift + E", null, (_, _) => EditorCaptureRequested?.Invoke());
        menu.Items.Add("Capturar janela ativa   Win + Shift + A", null, (_, _) => ActiveWindowCaptureRequested?.Invoke());
        menu.Items.Add("Abrir pasta de capturas", null, (_, _) => CaptureService.OpenCaptureFolder());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitRequested?.Invoke());

        _icon = AppIcon.Create();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "PrintFreeTool",
            ContextMenuStrip = menu,
            Visible = true
        };

        _notifyIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == Forms.MouseButtons.Left)
            {
                OpenRequested?.Invoke();
            }
        };
        UpdateShortcut(shortcut);
    }

    public event Action? CaptureRequested;
    public event Action? EditorCaptureRequested;
    public event Action? ActiveWindowCaptureRequested;
    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public void UpdateShortcut(HotkeyShortcut shortcut)
    {
        _captureMenuItem.Text = $"Nova captura   {shortcut.DisplayText}";
        string tooltip = $"PrintFreeTool — {shortcut.DisplayText}";
        _notifyIcon.Text = tooltip.Length <= 63 ? tooltip : "PrintFreeTool";
    }

    public void ShowMessage(string title, string message, bool isError = false)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = isError ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
