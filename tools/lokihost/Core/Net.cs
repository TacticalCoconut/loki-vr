using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LokiHost.Core;

/// <summary>Addresses, port checks, the Windows Firewall rule.</summary>
public static class Net
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Public-address services tried in turn (each answers the caller's IPv4 address as plain text).</summary>
    public static readonly string[] PublicIpServices =
        { "https://api.ipify.org", "https://checkip.amazonaws.com", "https://ipv4.icanhazip.com" };

    /// <summary>This network's public IPv4 address as the internet sees it, or null (with the errors in <paramref name="error"/>).</summary>
    public static async Task<(string? ip, string error)> PublicIpAsync()
    {
        var errors = new StringBuilder();
        foreach (var url in PublicIpServices)
        {
            try
            {
                var text = (await Http.GetStringAsync(url)).Trim();
                if (IPAddress.TryParse(text, out var a) && a.AddressFamily == AddressFamily.InterNetwork) return (text, "");
                errors.Append($"{url}: unexpected answer; ");
            }
            catch (Exception e) { errors.Append($"{url}: {e.Message}; "); }
        }
        return (null, errors.ToString());
    }

    /// <summary>One IPv4 address of a network adapter, as <see cref="PickLan"/> weighs it.</summary>
    public sealed record Adapter(string Name, NetworkInterfaceType Type, bool Up, string Ip, string Mask, string[] Gateways);

    /// <summary>Every IPv4 address of every adapter, in the order Windows lists them.</summary>
    public static List<Adapter> Adapters()
    {
        var list = new List<Adapter>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            IPInterfaceProperties props;
            try { props = ni.GetIPProperties(); } catch (NetworkInformationException) { continue; }
            var gws = props.GatewayAddresses.Where(g => g.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(g => g.Address.ToString()).ToArray();
            foreach (var u in props.UnicastAddresses.Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork))
                list.Add(new Adapter(ni.Name, ni.NetworkInterfaceType, ni.OperationalStatus == OperationalStatus.Up,
                    u.Address.ToString(), u.IPv4Mask?.ToString() ?? "255.255.255.0", gws));
        }
        return list;
    }

    static readonly NetworkInterfaceType[] Physical =
    {
        NetworkInterfaceType.Ethernet, NetworkInterfaceType.Wireless80211, NetworkInterfaceType.GigabitEthernet,
        NetworkInterfaceType.FastEthernetT, NetworkInterfaceType.FastEthernetFx, NetworkInterfaceType.Ethernet3Megabit,
    };

    /// <summary>An IPv4 address as a number (0 for anything else).</summary>
    static uint V4(string ip)
    {
        if (!IPAddress.TryParse(ip, out var a) || a.AddressFamily != AddressFamily.InterNetwork) return 0;
        var b = a.GetAddressBytes();
        return (uint)b[0] << 24 | (uint)b[1] << 16 | (uint)b[2] << 8 | b[3];
    }

    /// <summary>10/8, 172.16/12, 192.168/16: the addresses a home router hands out.</summary>
    public static bool IsPrivate(string ip)
    {
        uint a = V4(ip);
        return a >> 24 == 10 || a >> 20 == 0xAC1 || a >> 16 == 0xC0A8;
    }

    /// <summary>
    /// The home network's address of this PC: an adapter that is up, is a physical Ethernet / Wi-Fi one and has a
    /// gateway inside its own subnet (the router), a private address first. VPN tunnels are skipped (WgTunnel,
    /// WireGuard, OpenVPN TAP report a virtual type, and Windows can list them first: on a test PC WgTunnel 10.8.0.2,
    /// with a gateway of its own, comes before the Wi-Fi), and so are virtual adapters without a real gateway (a
    /// KM-TEST loopback with gateway 0.0.0.0, WSL's vEthernet). Falls back to any up, non-loopback adapter with a
    /// gateway; ("", "") when there is none.
    /// </summary>
    public static (string ip, string mask) PickLan(IEnumerable<Adapter> adapters)
    {
        var up = adapters.Where(a => a.Up && a.Type != NetworkInterfaceType.Loopback && V4(a.Ip) != 0).ToList();
        bool RouterInSubnet(Adapter a) => a.Gateways.Any(g => V4(g) != 0 && (V4(g) & V4(a.Mask)) == (V4(a.Ip) & V4(a.Mask)));
        var best = up.Where(a => Physical.Contains(a.Type) && RouterInSubnet(a)).OrderByDescending(a => IsPrivate(a.Ip)).FirstOrDefault()
            ?? up.FirstOrDefault(a => a.Gateways.Any(g => V4(g) != 0));
        return best == null ? ("", "") : (best.Ip, best.Mask);
    }

    /// <summary>The LAN address and mask the router forwards to (see <see cref="PickLan"/>), or ("", "").</summary>
    public static (string ip, string mask) LanAddress() => PickLan(Adapters());

    /// <summary>The local address Windows sends internet traffic from (a UDP "connect" picks the route and sends nothing), or "".</summary>
    public static string InternetSourceAddress()
    {
        try
        {
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("1.1.1.1", 53);
            return ((IPEndPoint)s.LocalEndPoint!).Address.ToString();
        }
        catch (SocketException) { return ""; }
    }

    /// <summary>
    /// The adapter that carries this PC's internet traffic when it is not the LAN one: a VPN (null when the traffic
    /// leaves through the LAN, or when it cannot be told). Then the public address found belongs to the VPN and friends
    /// on the internet cannot reach this PC through the router's port forwarding (measured on a test PC with WireGuard VPN on:
    /// the address services answer an address of WireGuard VPN's network even when asked through the Wi-Fi address).
    /// </summary>
    public static string? VpnAdapter(IEnumerable<Adapter> adapters, string lanIp, string internetSource)
    {
        if (lanIp == "" || internetSource == "" || internetSource == lanIp) return null;
        return adapters.FirstOrDefault(x => x.Ip == internetSource)?.Name ?? internetSource;
    }

    public static string? VpnAdapter() { var all = Adapters(); return VpnAdapter(all, PickLan(all).ip, InternetSourceAddress()); }

    /// <summary>Shown wherever the public address is used while a VPN carries the internet traffic.</summary>
    public static string VpnWarning(string adapter) =>
        $"This PC's internet traffic goes through a VPN ({adapter}), so the public address found is the VPN's: friends on the "
        + "internet cannot reach your server through your router. Turn the VPN off while hosting, then Stop and Start again. "
        + "Players on your home network are not affected (some VPNs block them too: look for a \"LAN\" / \"invisibility\" setting).";

    /// <summary>Does something accept TCP connections on host:port within the timeout?</summary>
    public static async Task<bool> CanConnectAsync(string host, int port, int timeoutMs = 3000)
    {
        using var c = new TcpClient();
        try
        {
            var t = c.ConnectAsync(host, port);
            return await Task.WhenAny(t, Task.Delay(timeoutMs)) == t && c.Connected;
        }
        catch (Exception) { return false; }
    }

    /// <summary>
    /// Is something listening on this TCP port on this PC? Read from the system's listener table, so no connection is
    /// made: connecting every 3 s (the status lights) made the login and map servers log "Closed connection from
    /// '127.0.0.1'" each time (measured 2026-09-30: 37 such lines in the first two minutes).
    /// </summary>
    public static Task<bool> ListeningLocallyAsync(int port) =>
        Task.FromResult(IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port));

    /// <summary>
    /// Temporary listeners on the ports nothing listens on yet, so a port can be tested before the servers run.
    /// Dispose to close them.
    /// </summary>
    public sealed class TempListeners : IDisposable
    {
        readonly List<TcpListener> _l = new();
        readonly CancellationTokenSource _cts = new();
        public List<int> Opened { get; } = new();
        public List<(int port, string error)> Failed { get; } = new();

        public static async Task<TempListeners> OpenAsync(IEnumerable<int> ports)
        {
            var t = new TempListeners();
            foreach (var port in ports)
            {
                if (await ListeningLocallyAsync(port)) continue; // the server itself answers
                try
                {
                    var l = new TcpListener(IPAddress.Any, port);
                    l.Start();
                    t._l.Add(l);
                    t.Opened.Add(port);
                    _ = t.AcceptLoop(l);
                }
                catch (SocketException e) { t.Failed.Add((port, e.Message)); }
            }
            return t;
        }

        async Task AcceptLoop(TcpListener l)
        {
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    using var c = await l.AcceptTcpClientAsync(_cts.Token);
                }
            }
            catch (Exception) { /* closed */ }
        }

        public void Dispose()
        {
            _cts.Cancel();
            foreach (var l in _l) l.Stop();
        }
    }

    public enum PortState { Open, Closed, Unknown }

    /// <summary>The public port-check service asked to connect back from the internet.</summary>
    public const string CheckerUrl = "https://portchecker.io/api/v1/query";

    /// <summary>
    /// Asks the internet to connect to ip:port (portchecker.io: POST {"host", "ports"}). Best effort: when the service
    /// cannot be reached or answers in an unexpected form, every port is Unknown and <c>note</c> says why; the
    /// definitive check is a friend's PC (the "Test a server" tab).
    /// </summary>
    public static async Task<(Dictionary<int, PortState> result, string note)> ExternalCheckAsync(string ip, int[] ports)
    {
        try
        {
            var body = JsonSerializer.Serialize(new { host = ip, ports });
            using var resp = await Http.PostAsync(CheckerUrl, new StringContent(body, Encoding.UTF8, "application/json"));
            var text = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) return (AllUnknown(ports), $"port checker answered HTTP {(int)resp.StatusCode}");
            var parsed = ParseChecker(text, ports);
            return parsed == null ? (AllUnknown(ports), "port checker answered in an unexpected form") : (parsed, "");
        }
        catch (Exception e) { return (AllUnknown(ports), "port checker unreachable: " + e.Message); }
    }

    static Dictionary<int, PortState> AllUnknown(int[] ports) => ports.ToDictionary(p => p, _ => PortState.Unknown);

    /// <summary>
    /// Reads a port checker's JSON leniently: any array of objects that carry a port number and a boolean status
    /// ("status" / "open" / "is_open"), wherever it sits. Null when none of the asked ports is found.
    /// </summary>
    public static Dictionary<int, PortState>? ParseChecker(string json, int[] ports)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); } catch (JsonException) { return null; }
        var found = new Dictionary<int, PortState>();
        void Walk(JsonElement e)
        {
            if (e.ValueKind == JsonValueKind.Object)
            {
                int? port = null; bool? open = null;
                foreach (var p in e.EnumerateObject())
                {
                    if (p.NameEquals("port") && p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n)) port = n;
                    else if ((p.NameEquals("status") || p.NameEquals("open") || p.NameEquals("is_open"))
                             && p.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) open = p.Value.GetBoolean();
                    else Walk(p.Value);
                }
                if (port is int pt && open is bool o && ports.Contains(pt)) found[pt] = o ? PortState.Open : PortState.Closed;
            }
            else if (e.ValueKind == JsonValueKind.Array)
                foreach (var x in e.EnumerateArray()) Walk(x);
        }
        using (doc) Walk(doc.RootElement);
        if (found.Count == 0) return null;
        foreach (var p in ports) found.TryAdd(p, PortState.Unknown);
        return found;
    }

    /// <summary>The inbound rule for the servers' ports and the pose relay's (Windows Firewall), run elevated when the user
    /// asks. A rule made by a Loki Host from before the relay is named after 7900-7902: "Remove that rule" of that version
    /// (or wf.msc) removes it.</summary>
    public static string FirewallRuleName(HostSettings s) => $"Loki Host (TCP {s.LoginPort}-{s.LastPort})";

    public static string FirewallAddArgs(HostSettings s) =>
        $"advfirewall firewall add rule name=\"{FirewallRuleName(s)}\" dir=in action=allow protocol=TCP localport={string.Join(",", s.Ports)}";

    public static string FirewallDeleteArgs(HostSettings s) =>
        $"advfirewall firewall delete rule name=\"{FirewallRuleName(s)}\"";

    /// <summary>The line a friend adds to "servers" in Loki_Data\StreamingAssets\loki.json (client LoginPanel.ServerEntry);
    /// presencePort: the headset pose relay (0 or left out: none runs).</summary>
    public static string LokiJsonEntry(string name, string host, int port, int presencePort = 0) =>
        presencePort > 0 ? JsonSerializer.Serialize(new { name, host, port, kind = "rathena", presencePort })
                         : JsonSerializer.Serialize(new { name, host, port, kind = "rathena" });
}
