using System.Windows.Input;
using System.Windows.Interop;

namespace SoundMatrix;

/// <summary>System-wide hotkeys via RegisterHotKey on a message-only window.</summary>
internal sealed class GlobalHotkeys : IDisposable
{
    readonly HwndSource _source;
    readonly Dictionary<int, Action> _handlers = [];
    int _nextId = 1;

    public GlobalHotkeys()
    {
        _source = new HwndSource(new HwndSourceParameters("SoundMatrix.Hotkeys") { ParentWindow = new IntPtr(-3) /* HWND_MESSAGE */ });
        _source.AddHook(WndProc);
    }

    public bool Register(Hotkey hotkey, Action handler)
    {
        if (hotkey.IsEmpty) return false;
        uint mods = Native.MOD_NOREPEAT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= Native.MOD_ALT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Control)) mods |= Native.MOD_CONTROL;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= Native.MOD_SHIFT;
        if (hotkey.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= Native.MOD_WIN;

        var id = _nextId++;
        if (!Native.RegisterHotKey(_source.Handle, id, mods, (uint)KeyInterop.VirtualKeyFromKey(hotkey.Key)))
            return false;
        _handlers[id] = handler;
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys) Native.UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handled = true;
            handler();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.Dispose();
    }
}
