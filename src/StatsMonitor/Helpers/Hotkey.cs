using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace StatsMonitor.Helpers;

public static class HotkeyParser
{
    public const uint Alt = 1, Control = 2, Shift = 4, Win = 8;

    /// <summary>Parses text such as "Ctrl+Alt+G" into Win32 modifier flags and a virtual-key code.</summary>
    public static bool TryParse(string? text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0; virtualKey = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Control; break;
                case "alt": modifiers |= Alt; break;
                case "shift": modifiers |= Shift; break;
                case "win" or "windows": modifiers |= Win; break;
                default: return false;
            }
        }
        if (modifiers == 0) return false;
        if (!Enum.TryParse<Key>(parts[^1], true, out var key) || key == Key.None) return false;
        virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0;
    }
}

/// <summary>System-wide hotkey delivered through a message-only window, independent of the app windows.</summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotkey = 0x0312, Id = 0x5348, ModNoRepeat = 0x4000;
    private readonly HwndSource _source;
    private bool _registered;

    public event Action? Pressed;

    public GlobalHotkey()
    {
        _source = new HwndSource(new HwndSourceParameters("StatsMonitorHotkey") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        _source.AddHook(Hook);
    }

    /// <summary>Registers (or replaces) the hotkey. Returns false when it is invalid or already in use.</summary>
    public bool Register(string? text)
    {
        Unregister();
        if (!HotkeyParser.TryParse(text, out var mod, out var vk)) return false;
        _registered = RegisterHotKey(_source.Handle, Id, mod | ModNoRepeat, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_source.Handle, Id);
        _registered = false;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == Id)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(Hook);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
