using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace PrintFreeTool;

internal sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmHotkey = 0x0312;
    private const int WmInput = 0x00FF;
    private const int VkControl = 0x11;
    private const int VkAlt = 0x12;
    private const int VkShift = 0x10;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;
    private const int VkPrintScreen = 0x2C;
    private const int PrintScreenHotkeyId = 0x5054;
    private const uint ModNoRepeat = 0x4000;
    private const uint PrintScreenScanCode = 0x37;
    private const uint LlkhfExtended = 0x01;
    private const long PrintScreenDebounceMilliseconds = 350;
    private const int PrintScreenPollingIntervalMilliseconds = 20;
    private const uint RidInput = 0x10000003;
    private const uint RimTypeKeyboard = 1;
    private const uint RidevRemove = 0x00000001;
    private const uint RidevInputSink = 0x00000100;
    private const ushort RiKeyBreak = 0x0001;
    private const ushort RiKeyE0 = 0x0002;

    private readonly LowLevelKeyboardProc _callback;
    private IntPtr _hookHandle;
    private bool _printScreenRegistered;
    private HwndSource? _rawInputWindow;
    private bool _rawInputRegistered;
    private bool _shortcutIsDown;
    private int _pressedVirtualKey;
    private bool _printScreenIsDown;
    private long _lastPrintScreenSignal;
    private System.Threading.Timer? _printScreenPollTimer;
    private int _polledPrintScreenIsDown;

    public KeyboardHook(HotkeyShortcut shortcut)
    {
        _callback = HookCallback;
        Shortcut = shortcut;
    }

    public event Action? HotkeyPressed;
    public event Action? EditorHotkeyPressed;
    public event Action? ActiveWindowHotkeyPressed;
    public event Action? PrintScreenPressed;
    public HotkeyShortcut Shortcut { get; set; }
    public bool Enabled { get; set; } = true;

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            return;
        }

        RegisterNativePrintScreen();
        RegisterRawKeyboardInput();

        try
        {
            using Process currentProcess = Process.GetCurrentProcess();
            using ProcessModule? currentModule = currentProcess.MainModule;
            IntPtr moduleHandle = GetModuleHandle(currentModule?.ModuleName);

            _hookHandle = SetWindowsHookEx(WhKeyboardLl, _callback, moduleHandle, 0);
            if (_hookHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "O Windows recusou o gancho global de teclado.");
            }

            _printScreenPollTimer = new System.Threading.Timer(
                PollPrintScreen,
                null,
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(PrintScreenPollingIntervalMilliseconds));
        }
        catch
        {
            UnregisterRawKeyboardInput();
            UnregisterNativePrintScreen();
            throw;
        }
    }

    private void RegisterNativePrintScreen()
    {
        _printScreenRegistered = RegisterHotKey(
            IntPtr.Zero,
            PrintScreenHotkeyId,
            ModNoRepeat,
            VkPrintScreen);

        if (_printScreenRegistered)
        {
            ComponentDispatcher.ThreadPreprocessMessage += OnThreadMessage;
        }
    }

    private void OnThreadMessage(ref MSG message, ref bool handled)
    {
        if (message.message == WmHotkey && message.wParam.ToInt32() == PrintScreenHotkeyId)
        {
            handled = true;
            if (Enabled)
            {
                RaisePrintScreen();
            }
        }
    }

    private void RegisterRawKeyboardInput()
    {
        var parameters = new HwndSourceParameters("PrintFreeTool.RawInputWindow")
        {
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE
            WindowStyle = 0,
            Width = 0,
            Height = 0
        };

        _rawInputWindow = new HwndSource(parameters);
        _rawInputWindow.AddHook(RawInputWindowProc);

        RawInputDevice keyboard = new()
        {
            UsagePage = 0x01,
            Usage = 0x06,
            Flags = RidevInputSink,
            TargetWindow = _rawInputWindow.Handle
        };

        _rawInputRegistered = RegisterRawInputDevices(
            [keyboard],
            1,
            (uint)Marshal.SizeOf<RawInputDevice>());

        if (!_rawInputRegistered)
        {
            _rawInputWindow.RemoveHook(RawInputWindowProc);
            _rawInputWindow.Dispose();
            _rawInputWindow = null;
        }
    }

    private IntPtr RawInputWindowProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmInput)
        {
            ProcessRawKeyboardInput(lParam);
        }

        return IntPtr.Zero;
    }

    private void ProcessRawKeyboardInput(IntPtr rawInputHandle)
    {
        uint size = 0;
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        if (GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
        {
            return;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(rawInputHandle, RidInput, buffer, ref size, headerSize) != size)
            {
                return;
            }

            RawInputHeader header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            if (header.Type != RimTypeKeyboard)
            {
                return;
            }

            IntPtr keyboardPointer = IntPtr.Add(buffer, Marshal.SizeOf<RawInputHeader>());
            RawKeyboard keyboard = Marshal.PtrToStructure<RawKeyboard>(keyboardPointer);
            bool isPrintScreen = keyboard.VirtualKey == VkPrintScreen ||
                                 (keyboard.MakeCode == PrintScreenScanCode && (keyboard.Flags & RiKeyE0) != 0);
            if (!isPrintScreen)
            {
                return;
            }

            bool isKeyUp = (keyboard.Flags & RiKeyBreak) != 0;
            if (!isKeyUp && Enabled)
            {
                if (!_printScreenIsDown)
                {
                    _printScreenIsDown = true;
                    RaisePrintScreen();
                }
            }
            else if (isKeyUp)
            {
                if (Enabled && !_printScreenIsDown)
                {
                    RaisePrintScreen();
                }

                _printScreenIsDown = false;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            KeyboardEvent keyboardEvent = Marshal.PtrToStructure<KeyboardEvent>(data);
            int virtualKey = (int)keyboardEvent.VirtualKey;
            bool isKeyDown = message == (IntPtr)WmKeyDown || message == (IntPtr)WmSysKeyDown;
            bool isKeyUp = message == (IntPtr)WmKeyUp || message == (IntPtr)WmSysKeyUp;
            bool isPhysicalPrintScreen = virtualKey == VkPrintScreen ||
                                         (keyboardEvent.ScanCode == PrintScreenScanCode &&
                                          (keyboardEvent.Flags & LlkhfExtended) != 0);

            if (Enabled && isPhysicalPrintScreen && isKeyDown)
            {
                if (!_printScreenIsDown)
                {
                    _printScreenIsDown = true;
                    RaisePrintScreen();
                }

                return (IntPtr)1;
            }

            if (isPhysicalPrintScreen && isKeyUp)
            {
                // Some multimedia and gaming keyboards expose Print Screen only on key-up.
                if (Enabled && !_printScreenIsDown)
                {
                    RaisePrintScreen();
                }

                _printScreenIsDown = false;
                if (Enabled)
                {
                    return (IntPtr)1;
                }
            }

            if (Enabled && virtualKey == HotkeyShortcut.ActiveWindowDefault.VirtualKey && isKeyDown && ModifiersMatch(HotkeyShortcut.ActiveWindowDefault.Modifiers))
            {
                if (!_shortcutIsDown)
                {
                    _shortcutIsDown = true;
                    _pressedVirtualKey = virtualKey;
                    ActiveWindowHotkeyPressed?.Invoke();
                }

                return (IntPtr)1;
            }

            if (Enabled && virtualKey == HotkeyShortcut.EditorDefault.VirtualKey && isKeyDown && ModifiersMatch(HotkeyShortcut.EditorDefault.Modifiers))
            {
                if (!_shortcutIsDown)
                {
                    _shortcutIsDown = true;
                    _pressedVirtualKey = virtualKey;
                    EditorHotkeyPressed?.Invoke();
                }

                return (IntPtr)1;
            }

            if (Enabled && virtualKey == Shortcut.VirtualKey && isKeyDown && ModifiersMatch(Shortcut.Modifiers))
            {
                if (!_shortcutIsDown)
                {
                    _shortcutIsDown = true;
                    _pressedVirtualKey = virtualKey;
                    HotkeyPressed?.Invoke();
                }

                // Prevent the broken Windows snipping handler from also receiving the shortcut.
                return (IntPtr)1;
            }

            if (virtualKey == _pressedVirtualKey && isKeyUp && _shortcutIsDown)
            {
                _shortcutIsDown = false;
                _pressedVirtualKey = 0;
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookHandle, code, message, data);
    }

    private void RaisePrintScreen()
    {
        long now = Environment.TickCount64;
        long previous = Interlocked.Read(ref _lastPrintScreenSignal);

        while (now - previous >= PrintScreenDebounceMilliseconds)
        {
            long observed = Interlocked.CompareExchange(ref _lastPrintScreenSignal, now, previous);
            if (observed == previous)
            {
                PrintScreenPressed?.Invoke();
                return;
            }

            previous = observed;
        }
    }

    private void PollPrintScreen(object? state)
    {
        bool isDown = KeyIsDown(VkPrintScreen);
        if (!isDown)
        {
            Volatile.Write(ref _polledPrintScreenIsDown, 0);
            return;
        }

        if (Enabled && Interlocked.Exchange(ref _polledPrintScreenIsDown, 1) == 0)
        {
            RaisePrintScreen();
        }
    }

    private static bool ModifiersMatch(HotkeyModifiers expected)
    {
        return GetCurrentModifiers() == expected;
    }

    private static HotkeyModifiers GetCurrentModifiers()
    {
        HotkeyModifiers actual = HotkeyModifiers.None;
        if (KeyIsDown(VkControl)) actual |= HotkeyModifiers.Control;
        if (KeyIsDown(VkAlt)) actual |= HotkeyModifiers.Alt;
        if (KeyIsDown(VkShift)) actual |= HotkeyModifiers.Shift;
        if (KeyIsDown(VkLeftWindows) || KeyIsDown(VkRightWindows)) actual |= HotkeyModifiers.Windows;
        return actual;
    }

    private static bool KeyIsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        _printScreenPollTimer?.Dispose();
        _printScreenPollTimer = null;
        UnregisterRawKeyboardInput();

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }

        UnregisterNativePrintScreen();
    }

    private void UnregisterRawKeyboardInput()
    {
        if (_rawInputRegistered)
        {
            RawInputDevice keyboard = new()
            {
                UsagePage = 0x01,
                Usage = 0x06,
                Flags = RidevRemove,
                TargetWindow = IntPtr.Zero
            };
            RegisterRawInputDevices([keyboard], 1, (uint)Marshal.SizeOf<RawInputDevice>());
            _rawInputRegistered = false;
        }

        if (_rawInputWindow is not null)
        {
            _rawInputWindow.RemoveHook(RawInputWindowProc);
            _rawInputWindow.Dispose();
            _rawInputWindow = null;
        }
    }

    private void UnregisterNativePrintScreen()
    {
        if (!_printScreenRegistered)
        {
            return;
        }

        ComponentDispatcher.ThreadPreprocessMessage -= OnThreadMessage;
        UnregisterHotKey(IntPtr.Zero, PrintScreenHotkeyId);
        _printScreenRegistered = false;
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KeyboardEvent
    {
        public readonly uint VirtualKey;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr TargetWindow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RawInputHeader
    {
        public readonly uint Type;
        public readonly uint Size;
        public readonly IntPtr Device;
        public readonly IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RawKeyboard
    {
        public readonly ushort MakeCode;
        public readonly ushort Flags;
        public readonly ushort Reserved;
        public readonly ushort VirtualKey;
        public readonly uint Message;
        public readonly uint ExtraInformation;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [In] RawInputDevice[] devices,
        uint deviceCount,
        uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr rawInput,
        uint command,
        IntPtr data,
        ref uint size,
        uint headerSize);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
