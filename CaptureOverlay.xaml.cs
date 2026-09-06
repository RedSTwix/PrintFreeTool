using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace PrintFreeTool;

public partial class CaptureOverlay : Window
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    private System.Windows.Point _dragStart;
    private bool _isDragging;
    private bool _isFinishing;
    private readonly bool _openEditor;

    public CaptureOverlay(bool openEditor)
    {
        InitializeComponent();
        _openEditor = openEditor;
    }

    public event Action<System.Windows.Media.Imaging.BitmapSource>? CaptureReady;
    public event Action<string>? CaptureSaved;
    public event Action? CaptureCanceled;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        int left = GetSystemMetrics(SmXVirtualScreen);
        int top = GetSystemMetrics(SmYVirtualScreen);
        int width = GetSystemMetrics(SmCxVirtualScreen);
        int height = GetSystemMetrics(SmCyVirtualScreen);

        SetWindowPos(handle, new IntPtr(-1), left, top, width, height, SwpNoActivate | SwpShowWindow);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        Focus();
        Keyboard.Focus(this);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isFinishing)
        {
            return;
        }

        _isDragging = true;
        _dragStart = e.GetPosition(SelectionCanvas);
        SelectionCanvas.CaptureMouse();

        Canvas.SetLeft(SelectionBorder, _dragStart.X);
        Canvas.SetTop(SelectionBorder, _dragStart.Y);
        SelectionBorder.Width = 0;
        SelectionBorder.Height = 0;
        SelectionBorder.Visibility = Visibility.Visible;
        DimensionBadge.Visibility = Visibility.Visible;
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        System.Windows.Point current = e.GetPosition(SelectionCanvas);
        double left = Math.Min(_dragStart.X, current.X);
        double top = Math.Min(_dragStart.Y, current.Y);

        Canvas.SetLeft(SelectionBorder, left);
        Canvas.SetTop(SelectionBorder, top);
        SelectionBorder.Width = Math.Abs(current.X - _dragStart.X);
        SelectionBorder.Height = Math.Abs(current.Y - _dragStart.Y);

        System.Windows.Point screenStart = PointToScreen(_dragStart);
        System.Windows.Point screenCurrent = PointToScreen(current);
        int pixelWidth = (int)Math.Abs(screenCurrent.X - screenStart.X);
        int pixelHeight = (int)Math.Abs(screenCurrent.Y - screenStart.Y);
        DimensionText.Text = $"{pixelWidth} × {pixelHeight} px";

        Canvas.SetLeft(DimensionBadge, Math.Min(current.X + 14, Math.Max(0, SelectionCanvas.ActualWidth - 120)));
        Canvas.SetTop(DimensionBadge, Math.Min(current.Y + 14, Math.Max(0, SelectionCanvas.ActualHeight - 42)));
    }

    private async void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging || _isFinishing)
        {
            return;
        }

        _isDragging = false;
        _isFinishing = true;
        SelectionCanvas.ReleaseMouseCapture();

        System.Windows.Point dragEnd = e.GetPosition(SelectionCanvas);
        System.Windows.Point screenStart = PointToScreen(_dragStart);
        System.Windows.Point screenEnd = PointToScreen(dragEnd);

        int left = (int)Math.Floor(Math.Min(screenStart.X, screenEnd.X));
        int top = (int)Math.Floor(Math.Min(screenStart.Y, screenEnd.Y));
        int right = (int)Math.Ceiling(Math.Max(screenStart.X, screenEnd.X));
        int bottom = (int)Math.Ceiling(Math.Max(screenStart.Y, screenEnd.Y));

        if (right - left < 3 || bottom - top < 3)
        {
            _isFinishing = false;
            SelectionBorder.Visibility = Visibility.Collapsed;
            DimensionBadge.Visibility = Visibility.Collapsed;
            return;
        }

        Hide();

        try
        {
            // Give the desktop compositor time to remove the dark overlay.
            await Task.Delay(90);
            System.Windows.Media.Imaging.BitmapSource image = CaptureService.CaptureRegion(left, top, right - left, bottom - top);
            if (_openEditor)
            {
                CaptureReady?.Invoke(image);
            }
            else
            {
                string savedPath = await CaptureService.SaveAndCopyAsync(image);
                CaptureSaved?.Invoke(savedPath);
            }
            Close();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Não foi possível concluir a captura.\n\n{exception.Message}",
                "PrintFreeTool",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            CaptureCanceled?.Invoke();
            Close();
        }
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelCapture();
        }
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        CancelCapture();
    }

    private void CancelCapture()
    {
        if (_isFinishing)
        {
            return;
        }

        CaptureCanceled?.Invoke();
        Close();
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
