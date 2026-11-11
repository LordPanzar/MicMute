namespace MicMute;

/// <summary>Message-only window that receives global hotkeys (RegisterHotKey).</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    public event Action<int> Pressed;
    private readonly Dictionary<int, List<int>> _registered = new();

    public HotkeyWindow()
    {
        CreateHandle(new CreateParams { Parent = Native.HWND_MESSAGE });
    }

    /// <summary>F13–F24 don't exist on normal keyboards, so they're safe to use alone (e.g. sent by G HUB or macro keys).</summary>
    public static bool IsExtraKey(Keys k) => k >= Keys.F13 && k <= Keys.F24;

    public bool Register(int id, HotkeyConfig hk)
    {
        Unregister(id);
        if (hk == null || hk.IsEmpty) return true;

        // A lone F13–F24 also fires while Ctrl/Alt/Shift/Win happen to be held,
        // so pressing it in the middle of something else still works.
        var mods = hk.Modifiers == 0 && IsExtraKey(hk.Key)
            ? Enumerable.Range(0, 16).Select(m => (uint)m).ToList()
            : new List<uint> { hk.Modifiers };

        var ids = new List<int>();
        foreach (uint m in mods)
        {
            int sub = id * 100 + ids.Count;
            if (Native.RegisterHotKey(Handle, sub, m | Native.MOD_NOREPEAT, (uint)hk.Key)) ids.Add(sub);
            else if (m == hk.Modifiers) { foreach (var s in ids) Native.UnregisterHotKey(Handle, s); return false; }
        }
        _registered[id] = ids;
        return true;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id, out var ids))
            foreach (var s in ids) Native.UnregisterHotKey(Handle, s);
    }

    public void UnregisterAll()
    {
        foreach (var id in _registered.Keys.ToList()) Unregister(id);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
        {
            Pressed?.Invoke(m.WParam.ToInt32() / 100);
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
