using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace Pulse.Services;

/// <summary>System-wide hotkeys — they work while the game has focus.</summary>
public sealed class HotkeyManager : IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    readonly IntPtr _hwnd;
    readonly HwndSource _source;
    readonly Dictionary<int, Action> _actions = new();
    int _nextId = 0xB000;

    public HotkeyManager(Window window)
    {
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(Hook);
    }

    public bool Register(ModifierKeys modifiers, Key key, Action action)
    {
        uint mods = MOD_NOREPEAT;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= MOD_WIN;

        int id = _nextId++;
        if (!Native.RegisterHotKey(_hwnd, id, mods, (uint)KeyInterop.VirtualKeyFromKey(key)))
            return false;

        _actions[id] = action;
        return true;
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (int id in _actions.Keys) Native.UnregisterHotKey(_hwnd, id);
        _actions.Clear();
        _source.RemoveHook(Hook);
    }
}
