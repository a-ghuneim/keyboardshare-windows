using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace KeyboardShare;
public sealed class Config
{
    public string Mode { get; set; } = "Host";
    public string PeerIP { get; set; } = "192.168.1.2";
    public string BindIP { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 45832;
    public string Token { get; set; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public string LocalHotkey { get; set; } = "Control+Alt+D1";
    public string RemoteHotkey { get; set; } = "Control+Alt+D2";
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyboardShare");
    public static string FilePath => Path.Combine(DirectoryPath, "config.json");
    public void Validate()
    {
        if (Mode is not ("Host" or "Client")) throw new ArgumentException("Mode must be Host or Client.");
        if (!IPAddress.TryParse(PeerIP, out _) && (string.IsNullOrWhiteSpace(PeerIP) || Uri.CheckHostName(PeerIP) != UriHostNameType.Dns)) throw new ArgumentException("Enter the other laptop's computer name or IP address (without a URL or port).");
        if (!IPAddress.TryParse(BindIP, out _)) throw new ArgumentException("Host listen IP must be an IP address. Use 0.0.0.0 with DHCP.");
        if (Port is < 1024 or > 65535) throw new ArgumentException("Port must be 1024–65535.");
        if (Token.Length < 32) throw new ArgumentException("Use a shared token with at least 32 characters.");
        var a = Hotkey.Parse(LocalHotkey); var b = Hotkey.Parse(RemoteHotkey);
        if (a == b || a == Hotkey.Emergency || b == Hotkey.Emergency) throw new ArgumentException("Switch hotkeys must be distinct and cannot use the emergency shortcut.");
    }
    public static Config Load() => File.Exists(FilePath) ? JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new() : new();
    public void Save() { Validate(); Directory.CreateDirectory(DirectoryPath); var temp = FilePath + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, FilePath, true); }
}
public readonly record struct Hotkey(Keys Key, bool Ctrl, bool Alt, bool Shift, bool Win)
{
    public static Hotkey Emergency => new(Keys.Escape, true, true, false, false);
    public static Hotkey Parse(string text)
    {
        bool c = false, a = false, s = false, w = false; Keys key = Keys.None;
        foreach (string part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant()) { case "ctrl": case "control": c = true; break; case "alt": a = true; break; case "shift": s = true; break; case "win": w = true; break; default: if (key != Keys.None || !Enum.TryParse(part, true, out key) || (int)key > 255) throw new ArgumentException("Invalid hotkey: " + text); break; }
        }
        if (key == Keys.None || !(c || a || s || w) || key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or Keys.LWin or Keys.RWin) throw new ArgumentException("Use modifiers plus one key, e.g. Control+Alt+D2.");
        return new(key, c, a, s, w);
    }
}
