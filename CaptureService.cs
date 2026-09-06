using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrintFreeTool;

internal static class CaptureService
{
    private const int ClipboardAttempts = 8;

    public static string CaptureFolder
    {
        get
        {
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures))
            {
                pictures = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            }

            return Path.Combine(pictures, "PrintFreeTool");
        }
    }

    public static BitmapSource CaptureRegion(int left, int top, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "A área selecionada precisa ter largura e altura positivas.");
        }

        using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
        }

        IntPtr bitmapHandle = bitmap.GetHbitmap();
        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(bitmapHandle);
        }
    }

    public static BitmapSource CaptureConfiguredScreens(PrintScreenSettings settings)
    {
        System.Windows.Forms.Screen[] available = System.Windows.Forms.Screen.AllScreens;
        List<System.Windows.Forms.Screen> selected = settings.Mode == PrintScreenCaptureMode.AllScreens
            ? available.ToList()
            : available.Where(screen => settings.SelectedMonitorIds.Contains(screen.DeviceName, StringComparer.OrdinalIgnoreCase)).ToList();

        if (selected.Count == 0)
        {
            System.Windows.Forms.Screen? primary = System.Windows.Forms.Screen.PrimaryScreen;
            if (primary is null)
            {
                throw new InvalidOperationException("Nenhuma tela foi encontrada pelo Windows.");
            }
            selected.Add(primary);
        }

        int left = selected.Min(screen => screen.Bounds.Left);
        int top = selected.Min(screen => screen.Bounds.Top);
        int right = selected.Max(screen => screen.Bounds.Right);
        int bottom = selected.Max(screen => screen.Bounds.Bottom);

        using var bitmap = new Bitmap(right - left, bottom - top, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(System.Drawing.Color.Black);
            foreach (System.Windows.Forms.Screen screen in selected)
            {
                graphics.CopyFromScreen(
                    screen.Bounds.Left,
                    screen.Bounds.Top,
                    screen.Bounds.Left - left,
                    screen.Bounds.Top - top,
                    screen.Bounds.Size,
                    CopyPixelOperation.SourceCopy);
            }
        }

        return ConvertBitmap(bitmap);
    }

    public static IReadOnlyList<ScreenCapture> CaptureEachScreen()
    {
        System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
        var captures = new List<ScreenCapture>(screens.Length);

        for (int index = 0; index < screens.Length; index++)
        {
            System.Windows.Forms.Screen screen = screens[index];
            BitmapSource image = CaptureRegion(
                screen.Bounds.Left,
                screen.Bounds.Top,
                screen.Bounds.Width,
                screen.Bounds.Height);
            captures.Add(new ScreenCapture(index + 1, screen.DeviceName, screen.Primary, image));
        }

        return captures;
    }

    public static IntPtr GetActiveWindowHandle()
    {
        IntPtr window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr root = GetAncestor(window, 2); // GA_ROOT
        return root != IntPtr.Zero ? root : window;
    }

    public static ActiveWindowSnapshot CaptureActiveWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window) || IsIconic(window))
        {
            throw new InvalidOperationException("A janela ativa não está disponível para captura.");
        }

        NativeRect bounds;
        int result = DwmGetWindowAttribute(window, 9, out bounds, Marshal.SizeOf<NativeRect>()); // DWMWA_EXTENDED_FRAME_BOUNDS
        if (result != 0 && !GetWindowRect(window, out bounds))
        {
            throw new InvalidOperationException("O Windows não informou os limites da janela ativa.");
        }

        System.Drawing.Rectangle virtualDesktop = System.Windows.Forms.SystemInformation.VirtualScreen;
        System.Drawing.Rectangle windowBounds = System.Drawing.Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        System.Drawing.Rectangle visibleBounds = System.Drawing.Rectangle.Intersect(windowBounds, virtualDesktop);
        if (visibleBounds.Width <= 0 || visibleBounds.Height <= 0)
        {
            throw new InvalidOperationException("A janela ativa está fora da área visível dos monitores.");
        }

        int titleLength = GetWindowTextLength(window);
        var title = new StringBuilder(Math.Max(1, titleLength + 1));
        GetWindowText(window, title, title.Capacity);
        string windowTitle = string.IsNullOrWhiteSpace(title.ToString()) ? "Janela" : title.ToString().Trim();

        BitmapSource image = CaptureRegion(visibleBounds.Left, visibleBounds.Top, visibleBounds.Width, visibleBounds.Height);
        return new ActiveWindowSnapshot(windowTitle, image);
    }

    public static async Task<ActiveWindowSnapshot> CaptureActiveWindowHighQualityAsync(IntPtr window)
    {
        string title = GetWindowTitle(window);

        try
        {
            BitmapSource graphicsCapture = await WindowsGraphicsCaptureService.CaptureWindowAsync(window);
            if (!IsBlankProtectedFrame(graphicsCapture))
            {
                return new ActiveWindowSnapshot(title, graphicsCapture);
            }

            ActiveWindowSnapshot screenFallback = CaptureActiveWindow(window);
            if (!IsBlankProtectedFrame(screenFallback.Image))
            {
                return screenFallback;
            }

            throw new ProtectedContentException(
                "A janela devolveu uma imagem vazia. O conteúdo pode estar protegido por DRM.");
        }
        catch (ProtectedContentException)
        {
            throw;
        }
        catch (COMException exception) when ((uint)exception.HResult == 0x80070005)
        {
            throw new ProtectedContentException(
                "O Windows bloqueou o acesso à imagem desta janela. Ela pode conter conteúdo protegido por DRM.");
        }
        catch
        {
            // Older or incompatible applications still work through the screen-composition fallback.
            return CaptureActiveWindow(window);
        }
    }

    private static string GetWindowTitle(IntPtr window)
    {
        int titleLength = GetWindowTextLength(window);
        var title = new StringBuilder(Math.Max(1, titleLength + 1));
        GetWindowText(window, title, title.Capacity);
        return string.IsNullOrWhiteSpace(title.ToString()) ? "Janela" : title.ToString().Trim();
    }

    private static bool IsBlankProtectedFrame(BitmapSource image)
    {
        BitmapSource bgra = image.Format == PixelFormats.Bgra32 || image.Format == PixelFormats.Pbgra32
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);

        int stepX = Math.Max(1, bgra.PixelWidth / 160);
        int stepY = Math.Max(1, bgra.PixelHeight / 100);
        int stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        int samples = 0;
        int blankSamples = 0;
        for (int y = 0; y < bgra.PixelHeight; y += stepY)
        {
            for (int x = 0; x < bgra.PixelWidth; x += stepX)
            {
                int offset = y * stride + x * 4;
                byte blue = pixels[offset];
                byte green = pixels[offset + 1];
                byte red = pixels[offset + 2];
                byte alpha = pixels[offset + 3];
                samples++;
                if (alpha <= 2 || (red <= 4 && green <= 4 && blue <= 4))
                {
                    blankSamples++;
                }
            }
        }

        return samples > 0 && blankSamples >= samples * 0.99;
    }

    public static async Task<string> SaveAndCopyAsync(BitmapSource image, string? fileSuffix = null)
    {
        Directory.CreateDirectory(CaptureFolder);
        string savedPath = Path.Combine(CaptureFolder, CreateFileName(fileSuffix));
        SaveImage(image, savedPath);
        await CopyToClipboardAsync(image);
        return savedPath;
    }

    public static async Task<IReadOnlyList<string>> SaveSeparateScreensAndCopyAsync(IReadOnlyList<ScreenCapture> captures)
    {
        if (captures.Count == 0)
        {
            throw new InvalidOperationException("Nenhuma tela foi encontrada pelo Windows.");
        }

        Directory.CreateDirectory(CaptureFolder);
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        var savedPaths = new List<string>(captures.Count);

        foreach (ScreenCapture capture in captures)
        {
            string path = Path.Combine(CaptureFolder, $"Captura_{timestamp}_Tela-{capture.Number}.png");
            SaveImage(capture.Image, path);
            savedPaths.Add(path);
        }

        ScreenCapture clipboardCapture = captures.FirstOrDefault(capture => capture.IsPrimary) ?? captures[0];
        await CopyToClipboardAsync(clipboardCapture.Image);
        return savedPaths;
    }

    public static void SaveImage(BitmapSource image, string path)
    {
        BitmapEncoder encoder = Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? new JpegBitmapEncoder { QualityLevel = 95 }
            : new PngBitmapEncoder();

        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    public static async Task CopyToClipboardAsync(BitmapSource image)
    {
        Exception? lastError = null;

        for (int attempt = 0; attempt < ClipboardAttempts; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetImage(image);
                return;
            }
            catch (COMException exception)
            {
                lastError = exception;
                await Task.Delay(35 * (attempt + 1));
            }
        }

        throw new InvalidOperationException("A área de transferência está ocupada por outro programa.", lastError);
    }

    public static string CreateFileName(string? suffix = null)
    {
        string safeSuffix = string.Empty;
        if (!string.IsNullOrWhiteSpace(suffix))
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            safeSuffix = new string(suffix.Where(character => !invalid.Contains(character)).Take(45).ToArray()).Trim();
        }

        string baseName = $"Captura_{DateTime.Now:yyyy-MM-dd_HH-mm-ss-fff}";
        return string.IsNullOrWhiteSpace(safeSuffix) ? $"{baseName}.png" : $"{baseName}_{safeSuffix}.png";
    }

    private static BitmapSource ConvertBitmap(Bitmap bitmap)
    {
        IntPtr bitmapHandle = bitmap.GetHbitmap();
        try
        {
            BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmapHandle,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(bitmapHandle);
        }
    }

    public static void OpenCaptureFolder()
    {
        Directory.CreateDirectory(CaptureFolder);
        Process.Start(new ProcessStartInfo
        {
            FileName = CaptureFolder,
            UseShellExecute = true
        });
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr objectHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out NativeRect value, int valueSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal sealed record ScreenCapture(int Number, string DeviceId, bool IsPrimary, BitmapSource Image);
internal sealed record ActiveWindowSnapshot(string Title, BitmapSource Image);
