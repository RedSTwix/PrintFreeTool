using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace PrintFreeTool;

internal static class AppIcon
{
    public static Icon Create()
    {
        const int size = 64;
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var shape = RoundedRectangle(new RectangleF(2, 2, 60, 60), 15);
        using var gradient = new LinearGradientBrush(
            new PointF(3, 3),
            new PointF(61, 61),
            Color.FromArgb(76, 201, 240),
            Color.FromArgb(168, 85, 247));
        graphics.FillPath(gradient, shape);

        using var pen = new Pen(Color.White, 4.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        graphics.DrawPath(pen, CameraShape());
        graphics.DrawEllipse(pen, 25, 27, 14, 14);

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static BitmapSource CreateImageSource()
    {
        using Icon icon = Create();
        BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromWidthAndHeight(64, 64));
        source.Freeze();
        return source;
    }

    private static GraphicsPath CameraShape()
    {
        var path = new GraphicsPath();
        path.AddLines(
        [
            new PointF(13, 23),
            new PointF(20, 23),
            new PointF(24, 17),
            new PointF(40, 17),
            new PointF(44, 23),
            new PointF(51, 23),
            new PointF(51, 47),
            new PointF(13, 47)
        ]);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath RoundedRectangle(RectangleF rectangle, float radius)
    {
        float diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
