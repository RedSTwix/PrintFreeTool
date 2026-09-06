using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using WinRT;

namespace PrintFreeTool;

internal static class WindowsGraphicsCaptureService
{
    private const uint D3D11CreateDeviceBgraSupport = 0x20;
    private const uint D3D11SdkVersion = 7;
    private const int D3DDriverTypeHardware = 1;
    private const int D3DDriverTypeWarp = 5;

    private static readonly Guid GraphicsCaptureItemInteropId = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid GraphicsCaptureItemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid DxgiDeviceId = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    public static bool IsSupported => GraphicsCaptureSession.IsSupported();

    public static async Task<BitmapSource> CaptureWindowAsync(IntPtr window)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("Windows Graphics Capture não está disponível nesta versão do Windows.");
        }

        GraphicsCaptureItem item = CreateCaptureItem(window);
        IDirect3DDevice device = CreateDirect3DDevice();

        try
        {
            using Direct3D11CaptureFramePool framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                item.Size);
            using GraphicsCaptureSession session = framePool.CreateCaptureSession(item);
            session.IsCursorCaptureEnabled = false;

            var completion = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);

            void FrameArrived(Direct3D11CaptureFramePool sender, object args)
            {
                Direct3D11CaptureFrame frame = sender.TryGetNextFrame();
                if (!completion.TrySetResult(frame))
                {
                    frame.Dispose();
                }
            }

            framePool.FrameArrived += FrameArrived;
            try
            {
                session.StartCapture();
                using Direct3D11CaptureFrame frame = await completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
                using SoftwareBitmap surfaceCopy = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                    frame.Surface,
                    BitmapAlphaMode.Premultiplied);
                return ConvertSoftwareBitmap(surfaceCopy);
            }
            finally
            {
                framePool.FrameArrived -= FrameArrived;
            }
        }
        finally
        {
            if (device is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static GraphicsCaptureItem CreateCaptureItem(IntPtr window)
    {
        using IObjectReference factory = ActivationFactory.Get(
            "Windows.Graphics.Capture.GraphicsCaptureItem",
            GraphicsCaptureItemInteropId);

        IntPtr vtable = Marshal.ReadIntPtr(factory.ThisPtr);
        IntPtr methodPointer = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
        var createForWindow = Marshal.GetDelegateForFunctionPointer<CreateForWindowDelegate>(methodPointer);

        Guid itemInterfaceId = GraphicsCaptureItemId;
        int result = createForWindow(factory.ThisPtr, window, ref itemInterfaceId, out IntPtr itemPointer);
        Marshal.ThrowExceptionForHR(result);

        try
        {
            return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
        }
        finally
        {
            Marshal.Release(itemPointer);
        }
    }

    private static IDirect3DDevice CreateDirect3DDevice()
    {
        int result = D3D11CreateDevice(
            IntPtr.Zero,
            D3DDriverTypeHardware,
            IntPtr.Zero,
            D3D11CreateDeviceBgraSupport,
            IntPtr.Zero,
            0,
            D3D11SdkVersion,
            out IntPtr d3dDevice,
            out _,
            out IntPtr deviceContext);

        if (result < 0)
        {
            result = D3D11CreateDevice(
                IntPtr.Zero,
                D3DDriverTypeWarp,
                IntPtr.Zero,
                D3D11CreateDeviceBgraSupport,
                IntPtr.Zero,
                0,
                D3D11SdkVersion,
                out d3dDevice,
                out _,
                out deviceContext);
        }

        Marshal.ThrowExceptionForHR(result);

        IntPtr dxgiDevice = IntPtr.Zero;
        IntPtr inspectableDevice = IntPtr.Zero;
        try
        {
            Guid interfaceId = DxgiDeviceId;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3dDevice, ref interfaceId, out dxgiDevice));
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out inspectableDevice));
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectableDevice);
        }
        finally
        {
            if (inspectableDevice != IntPtr.Zero) Marshal.Release(inspectableDevice);
            if (dxgiDevice != IntPtr.Zero) Marshal.Release(dxgiDevice);
            if (deviceContext != IntPtr.Zero) Marshal.Release(deviceContext);
            if (d3dDevice != IntPtr.Zero) Marshal.Release(d3dDevice);
        }
    }

    private static BitmapSource ConvertSoftwareBitmap(SoftwareBitmap source)
    {
        SoftwareBitmap? converted = null;
        SoftwareBitmap bitmap = source;

        if (source.BitmapPixelFormat != BitmapPixelFormat.Bgra8 || source.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
        {
            converted = SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            bitmap = converted;
        }

        try
        {
            using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
            using var reference = buffer.CreateReference();
            IMemoryBufferByteAccess byteAccess = reference.As<IMemoryBufferByteAccess>();
            byteAccess.GetBuffer(out IntPtr data, out _);

            BitmapPlaneDescription plane = buffer.GetPlaneDescription(0);
            int rowBytes = bitmap.PixelWidth * 4;
            var pixels = new byte[rowBytes * bitmap.PixelHeight];

            for (int row = 0; row < bitmap.PixelHeight; row++)
            {
                IntPtr sourceRow = IntPtr.Add(data, plane.StartIndex + row * plane.Stride);
                Marshal.Copy(sourceRow, pixels, row * rowBytes, rowBytes);
            }

            BitmapSource result = BitmapSource.Create(
                bitmap.PixelWidth,
                bitmap.PixelHeight,
                bitmap.DpiX > 0 ? bitmap.DpiX : 96,
                bitmap.DpiY > 0 ? bitmap.DpiY : 96,
                PixelFormats.Bgra32,
                null,
                pixels,
                rowBytes);
            result.Freeze();
            return result;
        }
        finally
        {
            converted?.Dispose();
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateForWindowDelegate(
        IntPtr thisPointer,
        IntPtr window,
        ref Guid interfaceId,
        out IntPtr result);

    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-8656-1D720F1029B8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMemoryBufferByteAccess
    {
        void GetBuffer(out IntPtr buffer, out uint capacity);
    }

    [DllImport("d3d11.dll", SetLastError = false)]
    private static extern int D3D11CreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out IntPtr device,
        out uint featureLevel,
        out IntPtr immediateContext);

    [DllImport("d3d11.dll", SetLastError = false)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(
        IntPtr dxgiDevice,
        out IntPtr graphicsDevice);
}

internal sealed class ProtectedContentException : Exception
{
    public ProtectedContentException(string message) : base(message)
    {
    }
}
