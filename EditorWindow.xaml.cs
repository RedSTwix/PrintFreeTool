using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaColor = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;

namespace PrintFreeTool;

public partial class EditorWindow : Window
{
    private readonly BitmapSource _originalImage;
    private readonly Stack<StrokeChange> _undo = new();
    private readonly Stack<StrokeChange> _redo = new();
    private bool _ignoreStrokeChanges;
    private EditorTool _tool = EditorTool.Pen;
    private MediaColor _selectedColor = MediaColor.FromRgb(244, 63, 94);
    private double _zoom = 1;
    private bool _fitMode = true;

    public EditorWindow(BitmapSource image)
    {
        InitializeComponent();
        Icon = AppIcon.CreateImageSource();
        _originalImage = image;
        CapturedImage.Source = image;
        EditingSurface.Width = image.PixelWidth;
        EditingSurface.Height = image.PixelHeight;
        InkSurface.Width = image.PixelWidth;
        InkSurface.Height = image.PixelHeight;
        ImageInfoText.Text = $"{image.PixelWidth} × {image.PixelHeight} px  •  Edite antes de salvar";
        InkSurface.Strokes.StrokesChanged += OnStrokesChanged;
        ApplyDrawingTool();
        UpdateToolButtons();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            FitToWindow();
            Activate();
            Keyboard.Focus(this);
        });
    }

    public event Action<string>? EditingCompleted;

    private void OnPenClick(object sender, RoutedEventArgs e)
    {
        _tool = EditorTool.Pen;
        ApplyDrawingTool();
        UpdateToolButtons();
    }

    private void OnHighlighterClick(object sender, RoutedEventArgs e)
    {
        _tool = EditorTool.Highlighter;
        ApplyDrawingTool();
        UpdateToolButtons();
    }

    private void OnEraserClick(object sender, RoutedEventArgs e)
    {
        _tool = EditorTool.Eraser;
        ApplyDrawingTool();
        UpdateToolButtons();
    }

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton selected || selected.Tag is not string colorText)
        {
            return;
        }

        _selectedColor = (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(colorText);
        foreach (WpfButton button in FindVisualChildren<WpfButton>(this).Where(button => button.Tag is string))
        {
            button.BorderBrush = button == selected ? MediaBrushes.White : MediaBrushes.Transparent;
        }

        if (_tool == EditorTool.Eraser)
        {
            _tool = EditorTool.Pen;
        }

        ApplyDrawingTool();
        UpdateToolButtons();
    }

    private void OnThicknessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (InkSurface is not null)
        {
            ApplyDrawingTool();
        }
    }

    private void ApplyDrawingTool()
    {
        if (_tool == EditorTool.Eraser)
        {
            InkSurface.EditingMode = InkCanvasEditingMode.EraseByStroke;
            return;
        }

        InkSurface.EditingMode = InkCanvasEditingMode.Ink;
        double thickness = ThicknessSlider.Value;
        InkSurface.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = _selectedColor,
            Width = _tool == EditorTool.Highlighter ? Math.Max(14, thickness * 2.5) : thickness,
            Height = _tool == EditorTool.Highlighter ? Math.Max(14, thickness * 2.5) : thickness,
            FitToCurve = true,
            IgnorePressure = true,
            IsHighlighter = _tool == EditorTool.Highlighter,
            StylusTip = StylusTip.Ellipse
        };
    }

    private void UpdateToolButtons()
    {
        MediaBrush selected = new SolidColorBrush(MediaColor.FromRgb(44, 49, 68));
        PenButton.Background = _tool == EditorTool.Pen ? selected : MediaBrushes.Transparent;
        HighlighterButton.Background = _tool == EditorTool.Highlighter ? selected : MediaBrushes.Transparent;
        EraserButton.Background = _tool == EditorTool.Eraser ? selected : MediaBrushes.Transparent;
        PenButton.Foreground = _tool == EditorTool.Pen ? MediaBrushes.White : new SolidColorBrush(MediaColor.FromRgb(173, 178, 197));
        HighlighterButton.Foreground = _tool == EditorTool.Highlighter ? MediaBrushes.White : new SolidColorBrush(MediaColor.FromRgb(173, 178, 197));
        EraserButton.Foreground = _tool == EditorTool.Eraser ? MediaBrushes.White : new SolidColorBrush(MediaColor.FromRgb(173, 178, 197));
    }

    private void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (_ignoreStrokeChanges)
        {
            return;
        }

        _undo.Push(new StrokeChange(e.Added.ToList(), e.Removed.ToList()));
        _redo.Clear();
        UpdateHistoryButtons();
    }

    private void OnStrokesReplaced(object sender, InkCanvasStrokesReplacedEventArgs e)
    {
        e.NewStrokes.StrokesChanged += OnStrokesChanged;
    }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0) return;
        StrokeChange change = _undo.Pop();
        ApplyHistoryChange(change, reverse: true);
        _redo.Push(change);
        UpdateHistoryButtons();
    }

    private void OnRedoClick(object sender, RoutedEventArgs e)
    {
        if (_redo.Count == 0) return;
        StrokeChange change = _redo.Pop();
        ApplyHistoryChange(change, reverse: false);
        _undo.Push(change);
        UpdateHistoryButtons();
    }

    private void ApplyHistoryChange(StrokeChange change, bool reverse)
    {
        _ignoreStrokeChanges = true;
        try
        {
            IEnumerable<Stroke> remove = reverse ? change.Added : change.Removed;
            IEnumerable<Stroke> add = reverse ? change.Removed : change.Added;
            foreach (Stroke stroke in remove.Where(InkSurface.Strokes.Contains).ToList()) InkSurface.Strokes.Remove(stroke);
            foreach (Stroke stroke in add.Where(stroke => !InkSurface.Strokes.Contains(stroke))) InkSurface.Strokes.Add(stroke);
        }
        finally
        {
            _ignoreStrokeChanges = false;
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        if (InkSurface.Strokes.Count > 0) InkSurface.Strokes.Clear();
    }

    private void UpdateHistoryButtons()
    {
        UndoButton.IsEnabled = _undo.Count > 0;
        RedoButton.IsEnabled = _redo.Count > 0;
    }

    private void OnPreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        SetZoom(_zoom * (e.Delta > 0 ? 1.12 : 1 / 1.12), keepCentered: true);
        _fitMode = false;
        e.Handled = true;
    }

    private void OnEditorPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        const double scrollStep = 80;
        bool handled = true;

        switch (e.Key)
        {
            case Key.Left when ImageScrollViewer.ScrollableWidth > 0:
                ImageScrollViewer.ScrollToHorizontalOffset(ImageScrollViewer.HorizontalOffset - scrollStep);
                break;
            case Key.Right when ImageScrollViewer.ScrollableWidth > 0:
                ImageScrollViewer.ScrollToHorizontalOffset(ImageScrollViewer.HorizontalOffset + scrollStep);
                break;
            case Key.Up when ImageScrollViewer.ScrollableHeight > 0:
                ImageScrollViewer.ScrollToVerticalOffset(ImageScrollViewer.VerticalOffset - scrollStep);
                break;
            case Key.Down when ImageScrollViewer.ScrollableHeight > 0:
                ImageScrollViewer.ScrollToVerticalOffset(ImageScrollViewer.VerticalOffset + scrollStep);
                break;
            default:
                handled = false;
                break;
        }

        if (handled)
        {
            e.Handled = true;
        }
    }

    private void OnZoomOutClick(object sender, RoutedEventArgs e)
    {
        SetZoom(_zoom / 1.2, keepCentered: true);
        _fitMode = false;
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e)
    {
        SetZoom(_zoom * 1.2, keepCentered: true);
        _fitMode = false;
    }

    private void OnFitClick(object sender, RoutedEventArgs e)
    {
        _fitMode = true;
        FitToWindow();
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_fitMode && IsLoaded)
        {
            Dispatcher.BeginInvoke(FitToWindow);
        }
    }

    private void FitToWindow()
    {
        double availableWidth = Math.Max(1, ImageScrollViewer.ViewportWidth - 48);
        double availableHeight = Math.Max(1, ImageScrollViewer.ViewportHeight - 48);
        double fit = Math.Min(availableWidth / _originalImage.PixelWidth, availableHeight / _originalImage.PixelHeight);
        SetZoom(Math.Min(1, fit), keepCentered: false);
        ImageScrollViewer.ScrollToHorizontalOffset(0);
        ImageScrollViewer.ScrollToVerticalOffset(0);
    }

    private void SetZoom(double value, bool keepCentered)
    {
        double horizontalRatio = ImageScrollViewer.ExtentWidth > 0
            ? (ImageScrollViewer.HorizontalOffset + ImageScrollViewer.ViewportWidth / 2) / ImageScrollViewer.ExtentWidth
            : 0.5;
        double verticalRatio = ImageScrollViewer.ExtentHeight > 0
            ? (ImageScrollViewer.VerticalOffset + ImageScrollViewer.ViewportHeight / 2) / ImageScrollViewer.ExtentHeight
            : 0.5;

        _zoom = Math.Clamp(value, 0.05, 5);
        ZoomTransform.ScaleX = _zoom;
        ZoomTransform.ScaleY = _zoom;
        ZoomText.Text = $"{_zoom:P0}";
        EditingSurface.UpdateLayout();

        if (keepCentered)
        {
            ImageScrollViewer.ScrollToHorizontalOffset(horizontalRatio * ImageScrollViewer.ExtentWidth - ImageScrollViewer.ViewportWidth / 2);
            ImageScrollViewer.ScrollToVerticalOffset(verticalRatio * ImageScrollViewer.ExtentHeight - ImageScrollViewer.ViewportHeight / 2);
        }
    }

    private BitmapSource RenderEditedImage()
    {
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            drawing.DrawImage(_originalImage, new Rect(0, 0, _originalImage.PixelWidth, _originalImage.PixelHeight));
            InkSurface.Strokes.Draw(drawing);
        }
        var result = new RenderTargetBitmap(
            _originalImage.PixelWidth,
            _originalImage.PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        result.Render(visual);
        result.Freeze();
        return result;
    }

    private async void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await CaptureService.CopyToClipboardAsync(RenderEditedImage());
            await ShowStatusAsync("Copiado para a área de transferência");
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    private async void OnFinishClick(object sender, RoutedEventArgs e)
    {
        FinishButton.IsEnabled = false;
        try
        {
            string path = await CaptureService.SaveAndCopyAsync(RenderEditedImage());
            EditingCompleted?.Invoke(path);
            Close();
        }
        catch (Exception exception)
        {
            FinishButton.IsEnabled = true;
            ShowError(exception.Message);
        }
    }

    private async Task ShowStatusAsync(string message)
    {
        StatusText.Text = message;
        StatusPill.Visibility = Visibility.Visible;
        await Task.Delay(1800);
        StatusPill.Visibility = Visibility.Collapsed;
    }

    private static void ShowError(string message)
    {
        System.Windows.MessageBox.Show(message, "PrintFreeTool", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && IsInsideButton(source)) return;
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private static bool IsInsideButton(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is WpfButton) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (T descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private sealed record StrokeChange(IReadOnlyList<Stroke> Added, IReadOnlyList<Stroke> Removed);
    private enum EditorTool { Pen, Highlighter, Eraser }
}
