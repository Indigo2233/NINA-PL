using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Linq;

namespace NINA.PL.Equipment.Alpaca;

/// <summary>
/// UDP discovery and management queries for ASCOM Alpaca servers.
/// </summary>
public static class AlpacaDiscovery
{
    private const int DiscoveryPort = 32227;

    /// <summary>
    /// Broadcasts an Alpaca discovery request, collects server ports, then loads <c>/management/v1/configureddevices</c> for each host.
    /// </summary>
    public static async Task<List<AlpacaDevice>> DiscoverAsync(int timeoutMs = 5000, CancellationToken cancellationToken = default)
    {
        var servers = await DiscoverServersAsync(timeoutMs, cancellationToken).ConfigureAwait(false);
        var results = new List<AlpacaDevice>();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        foreach (var (host, alpacaPort) in servers)
        {
            var baseUrl = $"http://{host}:{alpacaPort}";
            try
            {
                var uri = $"{baseUrl}/management/v1/configureddevices";
                using var resp = await http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                    continue;

                var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("Value", out var value) || value.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var el in value.EnumerateArray())
                {
                    var deviceType = ReadString(el, "DeviceType", "deviceType");
                    if (string.IsNullOrWhiteSpace(deviceType))
                        continue;

                    var number = ReadInt(el, "DeviceNumber", "deviceNumber");
                    var name = ReadString(el, "DeviceName", "deviceName", "Name", "name");
                    var unique = ReadString(el, "UniqueID", "UniqueId", "uniqueID", "uniqueId");

                    results.Add(new AlpacaDevice
                    {
                        ServerUrl = baseUrl,
                        DeviceType = deviceType,
                        DeviceNumber = number,
                        Name = string.IsNullOrEmpty(name) ? $"{deviceType} #{number}" : name,
                        UniqueId = unique ?? string.Empty
                    });
                }
            }
            catch
            {
                // ignore unreachable or non-compliant hosts
            }
        }

        return results;
    }

    private static async Task<HashSet<(string Host, int Port)>> DiscoverServersAsync(int timeoutMs,
        CancellationToken cancellationToken)
    {
        var replies = new ConcurrentBag<(string Host, int Port)>();

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);

        var receiveTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                    var text = Encoding.UTF8.GetString(result.Buffer);
                    if (TryParseAlpacaPort(text, out var port) && port > 0)
                    {
                        var host = result.RemoteEndPoint.Address.ToString();
                        replies.Add((host, port));
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException)
                {
                    break;
                }
            }
        }, CancellationToken.None);

        // ASCOM Alpaca discovery JSON (ProtocolVersion 1).
        var payload = Encoding.UTF8.GetBytes("""{"Command":"discovery"}""");
        try
        {
            await udp.SendAsync(payload, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort), cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // broadcast may fail on some adapters; still wait for any directed replies
        }

        try
        {
            await receiveTask.ConfigureAwait(false);
        }
        catch
        {
            // ignore
        }

        return replies.Distinct().ToHashSet();
    }

    private static bool TryParseAlpacaPort(string text, out int port)
    {
        port = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("AlpacaPort", out var ap) && ap.TryGetInt32(out port))
                return true;
            if (root.TryGetProperty("alpacaport", out var ap2) && ap2.TryGetInt32(out port))
                return true;
        }
        catch
        {
            // not JSON
        }

        return false;
    }

    private static string? ReadString(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (el.TryGetProperty(n, out var p))
                return p.GetString();
            foreach (var prop in el.EnumerateObject())
            {
                if (string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.GetString();
            }
        }

        return null;
    }

    private static int ReadInt(JsonElement el, params string[] names)
    {
        foreach (var n in names)
        {
            if (el.TryGetProperty(n, out var p) && p.TryGetInt32(out var v))
                return v;
            foreach (var prop in el.EnumerateObject())
            {
                if (string.Equals(prop.Name, n, StringComparison.OrdinalIgnoreCase) &&
                    prop.Value.TryGetInt32(out var v2))
                    return v2;
            }
        }

        return 0;
    }
}
