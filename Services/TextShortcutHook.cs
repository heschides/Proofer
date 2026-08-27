using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Sati.Services;

/// <summary>
/// Handles Win+Shift+number only while the Sati shell is active and an explicitly
/// marked note/Scratchpad TextBox has focus. All other keyboard input is passed to
/// Windows unchanged.
/// </summary>
public sealed class TextShortcutHook(TextShortcutService shortcuts) : IDisposable
{
    private const int WhKeyboardLowLevel = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeftWindows = 0x5B;
    private const int VkRightWindows = 0x5C;

    private readonly HashSet<uint> _consumedKeys = [];
    private Window? _owner;
    private IntPtr _hook;
    private LowLevelKeyboardProc? _installedCallback;
    private bool _faulted;

    public void Start(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_hook != IntPtr.Zero)
            return;

        _owner = owner;
        _faulted = false;
        _installedCallback = HookCallback;
        _hook = SetWindowsHookEx(
            WhKeyboardLowLevel,
            _installedCallback,
            GetModuleHandle(null),
            0);

        if (_hook == IntPtr.Zero)
        {
            var exception = new Win32Exception(Marshal.GetLastWin32Error());
            AppErrorLog.Record(exception, "text-shortcuts.keyboard-hook");
            _installedCallback = null;
            _owner = null;
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _consumedKeys.Clear();
        _installedCallback = null;
        _owner = null;
        GC.SuppressFinalize(this);
    }

    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        if (_faulted)
            return CallNextHookEx(_hook, code, message, data);

        try
        {
            return HandleKeyboardMessage(code, message, data);
        }
        catch (Exception exception)
        {
            // No input-hook failure is allowed to escape into Windows' unmanaged
            // callback boundary. Disable shortcut handling for this session, log
            // once without snippet content, and let Windows handle the key.
            _faulted = true;
            _consumedKeys.Clear();
            AppErrorLog.Record(exception, "text-shortcuts.keyboard-callback");
            return CallNextHookEx(_hook, code, message, data);
        }
    }

    private IntPtr HandleKeyboardMessage(int code, IntPtr message, IntPtr data)
    {
        if (code < 0)
            return CallNextHookEx(_hook, code, message, data);

        var messageCode = unchecked((int)message.ToInt64());
        if (messageCode is not (WmKeyDown or WmSysKeyDown or WmKeyUp or WmSysKeyUp))
            return CallNextHookEx(_hook, code, message, data);

        var key = unchecked((uint)Marshal.ReadInt32(data));
        if (!TryMapDigit(key, out var digit))
            return CallNextHookEx(_hook, code, message, data);

        if (messageCode is WmKeyUp or WmSysKeyUp)
        {
            return _consumedKeys.Remove(key)
                ? new IntPtr(1)
                : CallNextHookEx(_hook, code, message, data);
        }

        if (_owner is not { IsActive: true } owner)
            return CallNextHookEx(_hook, code, message, data);

        if (!HasRequiredModifiers() || Keyboard.FocusedElement is not TextBox target ||
            !TextShortcutTarget.GetIsEnabled(target) || target.IsReadOnly || !target.IsEnabled)
        {
            return CallNextHookEx(_hook, code, message, data);
        }

        var text = shortcuts.GetTextForDigit(digit);
        if (string.IsNullOrEmpty(text))
            return CallNextHookEx(_hook, code, message, data);

        // Suppress key-repeat while the keys remain held. The first press inserts
        // once; its matching key-up is consumed above.
        if (_consumedKeys.Add(key))
        {
            _ = owner.Dispatcher.BeginInvoke(
                new Action(() => TextShortcutTarget.TryInsert(target, text)),
                DispatcherPriority.Input);
        }

        return new IntPtr(1);
    }

    internal static bool TryMapDigit(uint virtualKey, out int digit)
    {
        if (virtualKey is >= 0x31 and <= 0x39)
        {
            digit = (int)(virtualKey - 0x30);
            return true;
        }

        if (virtualKey == 0x30)
        {
            digit = 0;
            return true;
        }

        digit = -1;
        return false;
    }

    private static bool HasRequiredModifiers() =>
        IsDown(VkShift) &&
        (IsDown(VkLeftWindows) || IsDown(VkRightWindows)) &&
        !IsDown(VkControl) &&
        !IsDown(VkMenu);

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int hookType,
        LowLevelKeyboardProc callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
