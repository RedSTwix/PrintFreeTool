using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PrintFreeTool;

internal sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkControl = 0x11;
    private const int VkAlt = 0x12;
    private const int VkShift = 0x10;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;
    private const int VkPrintScreen = 0x2C;

    private readonly LowLevelKeyboardProc _callback;
    private IntPtr _hookHandle;
    private bool _shortcutIsDown;
    private int _pressedVirtualKey;
    private bool _printScreenIsDown;

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

        using Process currentProcess = Process.GetCurrentProcess();
        using ProcessModule? currentModule = currentProcess.MainModule;
        IntPtr moduleHandle = GetModuleHandle(currentModule?.ModuleName);

        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _callback, moduleHandle, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "O Windows recusou o gancho global de teclado.");
        }
    }

    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            int virtualKey = Marshal.ReadInt32(data);
            bool isKeyDown = message == (IntPtr)WmKeyDown || message == (IntPtr)WmSysKeyDown;
            bool isKeyUp = message == (IntPtr)WmKeyUp || message == (IntPtr)WmSysKeyUp;

            if (Enabled && virtualKey == VkPrintScreen && isKeyDown && GetCurrentModifiers() == HotkeyModifiers.None)
            {
                if (!_printScreenIsDown)
                {
                    _printScreenIsDown = true;
                    PrintScreenPressed?.Invoke();
                }

                return (IntPtr)1;
            }

            if (virtualKey == VkPrintScreen && isKeyUp && _printScreenIsDown)
            {
                _printScreenIsDown = false;
                return (IntPtr)1;
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
        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
