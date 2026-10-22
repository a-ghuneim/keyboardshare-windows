using KeyboardShare;
using System.Net;
using System.Net.Sockets;

int passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
void Reject(Action action, string name) { try { action(); } catch (ArgumentException) { Check(true, name); return; } throw new Exception(name); }
Check(Hotkey.Parse("Ctrl+Alt+D2") == new Hotkey(Keys.D2, true, true, false, false), "hotkey parsing");
Reject(() => Hotkey.Parse("Control+Alt"), "modifier-only shortcut rejected");
Reject(() => new Config { LocalHotkey = "Control+Alt+Escape" }.Validate(), "emergency conflict rejected");
Reject(() => new Config { Port = 0 }.Validate(), "invalid port rejected");
Reject(() => new Config { Token = "short" }.Validate(), "weak token rejected");
byte[] transcript = new byte[96];
var proof = Authentication.Proof("secret", 1, transcript);
Check(!proof.SequenceEqual(Authentication.Proof("other", 1, transcript)), "wrong secret changes proof");
Check(!proof.SequenceEqual(Authentication.Proof("secret", 2, transcript)), "role reflection rejected");
transcript[95] = 1;
Check(!proof.SequenceEqual(Authentication.Proof("secret", 1, transcript)), "TLS certificate/transcript binding");
var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
string token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
Config C(string mode, string secret) => new() { Mode = mode, PeerIP = "127.0.0.1", BindIP = "127.0.0.1", Port = port, Token = secret };
using var host = new Network(C("Host", token)); var ht = host.Run();
using (var bad = new Network(C("Client", new string('x', 40))))
{
    var bt = bad.Run(); await Task.Delay(2200); Check(!host.Connected && !bad.Connected, "unauthenticated client never connected"); bad.Dispose(); await bt.WaitAsync(TimeSpan.FromSeconds(6));
}
using (var client = new Network(C("Client", token)))
{
    var ct = client.Run();
    async Task Wait(Func<bool> condition) { using var deadline = new CancellationTokenSource(10000); while (!condition()) await Task.Delay(25, deadline.Token); }
    await Wait(() => host.Connected && client.Connected); Check(true, "mutual TLS/token connection after rejected peer");
    Check(host.Send(null), "ordered release-all packet queued");
    await Task.Delay(700); Check(client.Connected, "heartbeat and release-all processed");
    client.Dispose(); await ct.WaitAsync(TimeSpan.FromSeconds(6)); await Wait(() => !host.Connected); Check(true, "disconnect detected");
}
using (var client = new Network(C("Client", token)))
{
    var ct = client.Run(); using var deadline = new CancellationTokenSource(10000); while (!host.Connected || !client.Connected) await Task.Delay(25, deadline.Token);
    Check(true, "host accepts reconnection"); client.Dispose(); await ct.WaitAsync(TimeSpan.FromSeconds(6));
}
host.Dispose(); try { await ht.WaitAsync(TimeSpan.FromSeconds(6)); } catch (OperationCanceledException) { }
Console.WriteLine($"{passed} checks passed.");
