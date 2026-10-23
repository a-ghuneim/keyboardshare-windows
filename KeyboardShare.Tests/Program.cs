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

new Config { PeerIP = "LAPTOP-CLIENT" }.Validate(); Check(true, "Windows computer name accepted");
new Config { PeerIP = "client.example.test" }.Validate(); Check(true, "qualified hostname accepted");
Reject(() => new Config { PeerIP = "https://client:45832" }.Validate(), "peer URL rejected");
Reject(() => new Config { BindIP = "HOST-PC" }.Validate(), "listen address stays a literal IP");
int resolverCalls = 0;
IPAddress resolved = IPAddress.Parse("127.0.0.2");
Task<IPAddress[]> ResolveTest(string _, CancellationToken ct) { ct.ThrowIfCancellationRequested(); resolverCalls++; return Task.FromResult(new[] { resolved }); }
var first = await PeerAddress.ResolveAsync("peer.test", ResolveTest, CancellationToken.None);
resolved = IPAddress.Loopback;
var second = await PeerAddress.ResolveAsync("peer.test", ResolveTest, CancellationToken.None);
Check(resolverCalls == 2 && !first.SequenceEqual(second), "name is resolved again after address change");
Check(!PeerAddress.Matches(IPAddress.Loopback, first) && PeerAddress.Matches(IPAddress.Loopback, second), "peer filter tracks changed hostname address");
Check(PeerAddress.Matches(IPAddress.Loopback.MapToIPv6(), second), "IPv4 mapped peer comparison");
var literal = await PeerAddress.ResolveAsync("127.0.0.1", ResolveTest, CancellationToken.None);
Check(resolverCalls == 2 && literal.SequenceEqual(new[] { IPAddress.Loopback }), "existing literal settings bypass DNS");
var localhost = await PeerAddress.ResolveAsync("localhost", (name, ct) => Dns.GetHostAddressesAsync(name, ct), CancellationToken.None);
Check(localhost.Any(IPAddress.IsLoopback), "actual Windows localhost resolution");
try { await PeerAddress.ResolveAsync("empty.test", (_, _) => Task.FromResult(Array.Empty<IPAddress>()), CancellationToken.None); throw new Exception("Empty DNS result accepted"); }
catch (SocketException) { Check(true, "empty DNS result fails closed"); }
using (var cancelled = new CancellationTokenSource(100))
{
    try { await PeerAddress.ResolveAsync("slow.test", async (_, _) => { await Task.Delay(1000); return new[] { IPAddress.Loopback }; }, cancelled.Token); throw new Exception("DNS deadline ignored"); }
    catch (OperationCanceledException) { Check(true, "lookup deadline bounds even a slow resolver"); }
}

async Task Eventually(Func<bool> condition)
{
    using var deadline = new CancellationTokenSource(15000);
    while (!condition()) await Task.Delay(25, deadline.Token);
}

// Simulate DHCP changes using distinct real loopback listener addresses and a
// controlled resolver. Configured names and token stay unchanged throughout.
var dynamicHostConfig = C("Host", token); dynamicHostConfig.PeerIP = "client.test";
var dynamicClientConfig = C("Client", token); dynamicClientConfig.PeerIP = "host.test";
IPAddress hostAddress = IPAddress.Parse("127.0.0.2"), allowedClient = IPAddress.Parse("127.0.0.2");
int clientLookups = 0, hostLookups = 0;
Task<IPAddress[]> ResolveHost(string name, CancellationToken ct)
{
    Interlocked.Increment(ref clientLookups);
    return Task.FromResult(new[] { Volatile.Read(ref hostAddress) });
}
Task<IPAddress[]> ResolveClient(string name, CancellationToken ct)
{
    if (Interlocked.Increment(ref hostLookups) == 1) throw new SocketException((int)SocketError.HostNotFound);
    return Task.FromResult(new[] { Volatile.Read(ref allowedClient) });
}
using var dynamicClient = new Network(dynamicClientConfig, ResolveHost); var dynamicClientTask = dynamicClient.Run();
using var oldHost = new Network(dynamicHostConfig, ResolveClient); var oldHostTask = oldHost.Run();
await Eventually(() => Volatile.Read(ref clientLookups) >= 2);
Check(!dynamicClient.Connected, "stale Host address does not authenticate");
Volatile.Write(ref hostAddress, IPAddress.Loopback);
await Eventually(() => Volatile.Read(ref hostLookups) >= 2);
Check(!oldHost.Connected && !dynamicClient.Connected, "transient DNS failure and wrong peer address fail closed");
Volatile.Write(ref allowedClient, IPAddress.Loopback);
await Eventually(() => oldHost.Connected && dynamicClient.Connected);
Check(true, "hostname connection recovers after DNS failure and Client address update");
int lookupsBeforeMove = Volatile.Read(ref clientLookups);
oldHost.Dispose(); await oldHostTask.WaitAsync(TimeSpan.FromSeconds(6));
await Eventually(() => !dynamicClient.Connected);
Volatile.Write(ref hostAddress, IPAddress.Parse("127.0.0.2"));
var movedHostConfig = C("Host", token); movedHostConfig.BindIP = "127.0.0.2"; movedHostConfig.PeerIP = "client.test";
using var movedHost = new Network(movedHostConfig, ResolveClient); var movedHostTask = movedHost.Run();
await Eventually(() => movedHost.Connected && dynamicClient.Connected);
Check(Volatile.Read(ref clientLookups) > lookupsBeforeMove, "reconnect follows Host move to a different address without changing peer settings");
Check(!dynamicClient.TargetRemote, "reconnect after address change leaves keyboard local");
dynamicClient.Dispose(); movedHost.Dispose();
await Task.WhenAll(dynamicClientTask, movedHostTask).WaitAsync(TimeSpan.FromSeconds(6));
Console.WriteLine($"{passed} checks passed.");
