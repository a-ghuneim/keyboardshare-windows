using System.Net;
using System.Net.Sockets;

namespace KeyboardShare;
internal static class PeerAddress
{
    public static async Task<IPAddress[]> ResolveAsync(string name, Func<string, CancellationToken, Task<IPAddress[]>> resolver, CancellationToken ct)
    {
        // Keep literals compatible with existing configuration; resolve names afresh
        // on each attempt. Windows owns DNS caching and TTL handling, not this app.
        if (IPAddress.TryParse(name, out var literal)) return [literal];
        var addresses = await resolver(name, ct).WaitAsync(ct);
        addresses = addresses.Where(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
            .Where(a => !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any)).Distinct().ToArray();
        if (addresses.Length == 0) throw new SocketException((int)SocketError.HostNotFound);
        return addresses;
    }

    public static bool Matches(IPAddress actual, IEnumerable<IPAddress> expected) =>
        expected.Any(address => address.MapToIPv6().Equals(actual.MapToIPv6()));
}
