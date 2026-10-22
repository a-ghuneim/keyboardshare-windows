using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KeyboardShare;
internal sealed class KeyboardRouter : IDisposable
{
    readonly Native.HookProc callback;
    readonly nint hook;
    readonly HashSet<Keys> physical = new();
    readonly Dictionary<Keys, KeyEvent> localHeld = new();
    readonly HashSet<Keys> suppressedUntilUp = new();
    readonly Hotkey local, remote;
    readonly Func<KeyEvent?, bool> send;
    readonly Func<bool> connected;
    public bool Remote { get; private set; }
    public event Action? Changed;
    public KeyboardRouter(Config config, Func<KeyEvent?, bool> send, Func<bool> connected)
    {
        local = Hotkey.Parse(config.LocalHotkey); remote = Hotkey.Parse(config.RemoteHotkey); this.send = send; this.connected = connected;
        callback = Callback; hook = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
        if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    bool Matches(Hotkey h, Keys k) => h.Key == k && h.Ctrl == Any(Keys.LControlKey, Keys.RControlKey) && h.Alt == Any(Keys.LMenu, Keys.RMenu) && h.Shift == Any(Keys.LShiftKey, Keys.RShiftKey) && h.Win == Any(Keys.LWin, Keys.RWin);
    bool Any(Keys a, Keys b) => physical.Contains(a) || physical.Contains(b);
    public void Select(bool value)
    {
        if (value && !connected()) return;
        send(null); // Ordered release-all boundary, including when already local.
        foreach (var e in localHeld.Values) { try { Native.Inject(e with { Up = true }); } catch (Exception ex) { Log.Write(ex.Message); } }
        localHeld.Clear();
        suppressedUntilUp.UnionWith(physical); Remote = value; Changed?.Invoke();
    }
    nint Callback(int code, nint message, nint data)
    {
        if (code < 0) return Native.CallNextHookEx(hook, code, message, data);
        var d = Marshal.PtrToStructure<Native.HookData>(data);
        if ((d.Flags & 0x10) != 0) return Native.CallNextHookEx(hook, code, message, data);
        var k = (Keys)d.Vk; bool up = (d.Flags & 0x80) != 0;
        var e = new KeyEvent((ushort)d.Vk, (ushort)d.Scan, (d.Flags & 1) != 0, up);
        if (!up) physical.Add(k);
        try
        {
            if (!up && (Matches(Hotkey.Emergency, k) || Matches(local, k) || Matches(remote, k)))
            { bool target = Matches(remote, k); Select(target); suppressedUntilUp.Add(k); return 1; }
            if (suppressedUntilUp.Contains(k)) { if (up) suppressedUntilUp.Remove(k); return 1; }
            if (Remote)
            {
                if (!send(e)) Select(false);
                return 1;
            }
            if (up) localHeld.Remove(k); else localHeld[k] = e;
            return Native.CallNextHookEx(hook, code, message, data);
        }
        catch (Exception ex) { Log.Write(ex.ToString()); Select(false); return 1; }
        finally { if (up) physical.Remove(k); }
    }
    public void Dispose() { Select(false); Native.UnhookWindowsHookEx(hook); }
}
