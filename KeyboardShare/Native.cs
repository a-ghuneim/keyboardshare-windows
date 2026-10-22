using System.ComponentModel;
using System.Runtime.InteropServices;

namespace KeyboardShare;
public readonly record struct KeyEvent(ushort Vk, ushort Scan, bool Extended, bool Up);
internal static class Native
{
    public delegate nint HookProc(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)] public struct HookData { public uint Vk, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public Union Data; }
    [StructLayout(LayoutKind.Explicit)] struct Union { [FieldOffset(0)] public Keyboard Keyboard; [FieldOffset(0)] public Mouse Mouse; }
    [StructLayout(LayoutKind.Sequential)] struct Keyboard { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandle(string? module);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] input, int size);
    public static void Inject(KeyEvent e)
    {
        // Pause uses a multi-byte scan sequence; use its virtual key instead.
        bool scan = e.Scan != 0 && e.Vk != (ushort)Keys.Pause;
        var input = new Input { Type = 1, Data = new Union { Keyboard = new Keyboard { Vk = scan ? (ushort)0 : e.Vk, Scan = scan ? e.Scan : (ushort)0, Flags = (scan ? 8u : 0) | (e.Extended ? 1u : 0) | (e.Up ? 2u : 0), Extra = 0x4B534852 } } };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1) throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput failed (check elevation / desktop).");
    }
}
internal sealed class Injector
{
    readonly Dictionary<(ushort, bool), KeyEvent> held = new();
    public void Apply(KeyEvent e) { Native.Inject(e); if (e.Up) held.Remove((e.Scan == 0 ? e.Vk : e.Scan, e.Extended)); else held[(e.Scan == 0 ? e.Vk : e.Scan, e.Extended)] = e; }
    public void Release() { foreach (var e in held.Values.ToArray()) { try { Native.Inject(e with { Up = true }); } catch (Exception ex) { Log.Write(ex.Message); } } held.Clear(); }
}
