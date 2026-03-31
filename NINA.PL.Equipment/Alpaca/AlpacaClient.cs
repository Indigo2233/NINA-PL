using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace NINA.PL.Equipment.Alpaca;

/// <summary>
/// Low-level ASCOM Alpaca REST client (device API v1).
/// </summary>
public sealed class AlpacaClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string _serverUrl;
    private readonly string _deviceTypeSegment;
    private readonly int _deviceNumber;
    private int _clientTransactionId;
    private bool _disposed;

    public AlpacaClient(string serverUrl, string deviceType, int deviceNumber, HttpClient? httpClient = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceType);

        _serverUrl = serverUrl.TrimEnd('/');
        _deviceTypeSegment = deviceType.ToLowerInvariant().Trim();
        _deviceNumber = deviceNumber;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _ownsHttp = httpClient is null;
    }

    /// <summary>GET a device property; Alpaca path segment is lower-case.</summary>
    public async Task<T?> GetAsync<T>(string property, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var tx = Interlocked.Increment(ref _clientTransactionId);
        var path = $"{BuildDevicePath()}/{property.Trim().ToLowerInvariant()}";
        var uri = $"{path}?ClientTransactionID={tx}&ClientID=0";
        using var resp = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureHttpOk(resp, json);
        return DeserializeValue<T>(json);
    }

    /// <summary>GET property and return the full JSON document (for large <c>imagearray</c> payloads).</summary>
    public async Task<string> GetResponseJsonAsync(string property, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var tx = Interlocked.Increment(ref _clientTransactionId);
        var path = $"{BuildDevicePath()}/{property.Trim().ToLowerInvariant()}";
        var uri = $"{path}?ClientTransactionID={tx}&ClientID=0";
        using var resp = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureHttpOk(resp, json);
        return json;
    }

    /// <summary>PUT a device method or writable property; parameters go in the form body per Alpaca spec.</summary>
    public async Task PutAsync(string action, IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var tx = Interlocked.Increment(ref _clientTransactionId);
        var path = $"{BuildDevicePath()}/{action.Trim().ToLowerInvariant()}";
        var pairs = new List<KeyValuePair<string, string>>
        {
            new("ClientTransactionID", tx.ToString(CultureInfo.InvariantCulture)),
            new("ClientID", "0")
        };

        if (parameters is not null)
        {
            foreach (var kv in parameters)
                pairs.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));
        }

        using var content = new FormUrlEncodedContent(pairs);
        using var resp = await _http.PutAsync(path, content, cancellationToken).ConfigureAwait(false);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureHttpOk(resp, json);
        VerifyAlpacaErrors(json);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsHttp)
            _http.Dispose();
    }

    private string BuildDevicePath() => $"{_serverUrl}/api/v1/{_deviceTypeSegment}/{_deviceNumber}";

    private static void EnsureHttpOk(HttpResponseMessage resp, string body)
    {
        if (resp.IsSuccessStatusCode)
            return;
        var snippet = body.Length <= 512 ? body : body[..512];
        throw new HttpRequestException(
            $"Alpaca HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}. Body: {snippet}");
    }

    private T? DeserializeValue<T>(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        VerifyAlpacaErrors(root);

        if (!root.TryGetProperty("Value", out var valueEl))
            return default;

        if (typeof(T) == typeof(JsonElement))
            return (T)(object)valueEl.Clone();

        return JsonSerializer.Deserialize<T>(valueEl.GetRawText(), JsonOptions);
    }

    private static void VerifyAlpacaErrors(string json)
    {
        using var doc = JsonDocument.Parse(json);
        VerifyAlpacaErrors(doc.RootElement);
    }

    private static void VerifyAlpacaErrors(JsonElement root)
    {
        if (!root.TryGetProperty("ErrorNumber", out var errEl))
            return;
        var err = errEl.GetInt32();
        if (err == 0)
            return;

        var msg = root.TryGetProperty("ErrorMessage", out var m) ? m.GetString() : null;
        throw new InvalidOperationException($"Alpaca error {err}: {msg}");
    }

    /// <summary>Parses <c>Value</c> from a JSON Alpaca response string (for large payloads like image arrays).</summary>
    public static JsonElement ParseValueElement(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        VerifyAlpacaErrors(root);
        if (!root.TryGetProperty("Value", out var v))
            throw new InvalidOperationException("Alpaca response missing Value.");
        return v.Clone();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
