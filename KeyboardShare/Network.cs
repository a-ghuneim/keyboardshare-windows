using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Channels;

namespace KeyboardShare;
internal static class Authentication
{
    public static byte[] Proof(string token, byte role, byte[] transcript) => HMACSHA256.HashData(SHA256.HashData(Encoding.UTF8.GetBytes(token)), new[] { role }.Concat(transcript).ToArray());
    public static async Task Authenticate(SslStream stream, bool server, string token, CancellationToken ct)
    {
        byte[] own = RandomNumberGenerator.GetBytes(32), other = new byte[32];
        await stream.WriteAsync(own, ct); await stream.ReadExactlyAsync(other, ct);
        var cert = server ? stream.LocalCertificate! : stream.RemoteCertificate!;
        byte[] transcript = (server ? own.Concat(other) : other.Concat(own)).Concat(SHA256.HashData(cert.GetRawCertData())).ToArray();
        await stream.WriteAsync(Proof(token, server ? (byte)1 : (byte)2, transcript), ct);
        byte[] proof = new byte[32]; await stream.ReadExactlyAsync(proof, ct);
        if (!CryptographicOperations.FixedTimeEquals(proof, Proof(token, server ? (byte)2 : (byte)1, transcript))) throw new AuthenticationException("Shared token authentication failed.");
    }
}
internal sealed class Network : IDisposable
{
    readonly Config config;
    readonly Func<string, CancellationToken, Task<IPAddress[]>> resolve;
    readonly CancellationTokenSource stop = new();
    Channel<byte[]>? output;
    TcpClient? active;
    volatile bool connected;
    public bool Connected { get => connected; private set => connected = value; }
    public bool TargetRemote { get; private set; }
    public event Action? Changed;
    public Network(Config config, Func<string, CancellationToken, Task<IPAddress[]>>? resolver = null)
    {
        this.config = config;
        resolve = resolver ?? ((name, ct) => Dns.GetHostAddressesAsync(name, ct));
    }
    public Task Run() => Task.Run(async () => { try { if (config.Mode == "Host") await HostLoop(); else await ClientLoop(); } catch (Exception) when (stop.IsCancellationRequested) { } });
    public void SelectTarget(bool remote)
    {
        if (Connected && output?.Writer.TryWrite([3, remote ? (byte)1 : (byte)0, 0, 0, 0, 0]) != true) active?.Dispose();
    }
    public bool Send(KeyEvent? e)
    {
        var q = output;
        if (!Connected || q == null) return false;
        byte[] frame = e is { } k ? [1, (byte)k.Vk, (byte)(k.Vk >> 8), (byte)k.Scan, (byte)(k.Scan >> 8), (byte)((k.Extended ? 1 : 0) | (k.Up ? 2 : 0))] : [2, 0, 0, 0, 0, 0];
        if (q.Writer.TryWrite(frame)) return true;
        active?.Dispose(); return false;
    }
    async Task HostLoop()
    {
        TcpListener? listener = null;
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=KeyboardShare", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        // Reimport through PFX so Windows Schannel can acquire the private key.
        using var certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx));
        try
        {
            listener = new TcpListener(IPAddress.Parse(config.BindIP), config.Port); listener.Start();
            while (!stop.IsCancellationRequested)
            {
                using var tcp = await listener.AcceptTcpClientAsync(stop.Token);
                try
                {
                    using var lookup = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    lookup.CancelAfter(TimeSpan.FromSeconds(5));
                    var expected = await PeerAddress.ResolveAsync(config.PeerIP, resolve, lookup.Token);
                    if (!PeerAddress.Matches(((IPEndPoint)tcp.Client.RemoteEndPoint!).Address, expected)) continue;
                    await Session(tcp, certificate);
                }
                catch (Exception ex) when (!stop.IsCancellationRequested) { Log.Write(ex.Message); }
            }
        }
        catch (Exception ex) when (!stop.IsCancellationRequested) { Log.Write(ex.ToString()); Changed?.Invoke(); }
        finally { listener?.Stop(); }
    }
    async Task<TcpClient> ConnectPeer(CancellationToken ct)
    {
        var addresses = await PeerAddress.ResolveAsync(config.PeerIP, resolve, ct);
        Exception? lastError = null;
        foreach (var address in addresses)
        {
            ct.ThrowIfCancellationRequested();
            var tcp = new TcpClient(address.AddressFamily);
            try
            {
                using var candidate = CancellationTokenSource.CreateLinkedTokenSource(ct);
                candidate.CancelAfter(TimeSpan.FromSeconds(2));
                await tcp.ConnectAsync(address, config.Port, candidate.Token);
                if (!PeerAddress.Matches(((IPEndPoint)tcp.Client.RemoteEndPoint!).Address, addresses)) throw new AuthenticationException("Unexpected peer IP.");
                return tcp;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested) { lastError = ex; tcp.Dispose(); }
            catch { tcp.Dispose(); throw; }
        }
        throw new IOException("Cannot connect to the configured peer's current addresses.", lastError);
    }
    async Task ClientLoop()
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var tcp = await ConnectPeer(timeout.Token);
                await Session(tcp, null);
            }
            catch (Exception ex) when (!stop.IsCancellationRequested) { Log.Write(ex.Message); }
            try { await Task.Delay(1500, stop.Token); } catch (OperationCanceledException) { break; }
        }
    }
    async Task Session(TcpClient tcp, X509Certificate2? certificate)
    {
        active = tcp; tcp.NoDelay = true;
        bool host = certificate != null;
        using var tls = new SslStream(tcp.GetStream(), false, (_, _, _, _) => true);
        using var handshake = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); handshake.CancelAfter(TimeSpan.FromSeconds(5));
        // Certificate trust is established by the transcript-bound shared-secret proof below.
        if (host) await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate, EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, handshake.Token);
        else await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "KeyboardShare", EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 }, handshake.Token);
        await Authentication.Authenticate(tls, host, config.Token, handshake.Token);
        var queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1024) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
        var injector = new Injector(); output = queue; Connected = true; Changed?.Invoke();
        var writer = WriteLoop(tls, queue, session.Token);
        try
        {
            byte[] frame = new byte[6];
            while (!session.IsCancellationRequested)
            {
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(session.Token); idle.CancelAfter(TimeSpan.FromSeconds(3));
                await tls.ReadExactlyAsync(frame, idle.Token);
                if (frame[0] == 0) continue;
                if (host) throw new IOException("Client sent an unexpected input packet.");
                if (frame[0] == 3 && frame[1] <= 1) { TargetRemote = frame[1] == 1; Changed?.Invoke(); }
                else if (frame[0] == 2) injector.Release();
                else if (frame[0] == 1 && frame[5] <= 3 && TargetRemote)
                {
                    ushort vk = (ushort)(frame[1] | frame[2] << 8), scan = (ushort)(frame[3] | frame[4] << 8);
                    if (vk is 0 or > 254 || scan > 255) throw new IOException("Invalid key packet.");
                    injector.Apply(new(vk, scan, (frame[5] & 1) != 0, (frame[5] & 2) != 0));
                }
                else throw new IOException("Invalid packet.");
            }
        }
        finally
        {
            Connected = false; TargetRemote = false; output = null; session.Cancel(); tcp.Dispose();
            try { await writer; } catch (Exception ex) { Log.Write(ex.Message); }
            injector.Release(); Changed?.Invoke();
        }
    }
    static async Task WriteLoop(SslStream stream, Channel<byte[]> queue, CancellationToken ct)
    {
        // One writer owns TLS; periodic heartbeats also detect half-open connections.
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(ct); tick.CancelAfter(500);
                byte[] frame;
                try { frame = await queue.Reader.ReadAsync(tick.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { frame = new byte[6]; }
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(2000);
                await stream.WriteAsync(frame, deadline.Token);
            }
        }
        catch { stream.Dispose(); throw; }
    }
    public void Dispose() { stop.Cancel(); active?.Dispose(); }
}
