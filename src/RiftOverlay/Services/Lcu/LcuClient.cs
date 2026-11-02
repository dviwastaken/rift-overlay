using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RiftOverlay.Services.Lcu;

/// <summary>
/// Talks to the League client's local API over loopback.
/// <para>
/// Riot requires an application to be registered before it is <em>released</em> using these
/// endpoints; this is a personal tool and stays on the reading side apart from rune pages, which
/// only ever change at the user's request.
/// </para>
/// </summary>
public sealed class LcuClient : IDisposable
{
    // Shared with everything that talks to this API — see LcuJson for why this must not be a
    // second, local set of options.
    private static JsonSerializerOptions SerializerOptions => LcuJson.Options;

    private readonly ILog _log;

    private HttpClient? _client;
    private int _port;

    public LcuClient(ILog? log = null) => _log = log ?? NullLog.Instance;

    public bool IsConnected => _client is not null;

    /// <summary>
    /// Reconnects when the client restarts on a new port. Cheap enough to call on a slow poll:
    /// nothing happens when the port has not changed.
    /// </summary>
    public bool Refresh()
    {
        var credentials = LcuCredentials.Discover(_log);

        if (credentials is null)
        {
            if (_client is not null)
            {
                _log.Info("League istemcisi kapandı, LCU bağlantısı bırakıldı.");
                Disconnect();
            }

            return false;
        }

        if (_client is not null && credentials.Port == _port)
        {
            return true;
        }

        Disconnect();

        var handler = new HttpClientHandler
        {
            // Loopback only, with the client's own self-signed certificate.
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };

        _client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://127.0.0.1:{credentials.Port}"),
            Timeout = TimeSpan.FromSeconds(5),
        };

        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"riot:{credentials.Password}"));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);

        _port = credentials.Port;
        _log.Info($"LCU bağlantısı kuruldu (port {credentials.Port}).");

        return true;
    }

    /// <returns>Null when the client is gone, the endpoint is absent, or the body will not parse.</returns>
    public async Task<T?> GetAsync<T>(string path, CancellationToken token = default)
    {
        if (_client is null)
        {
            return default;
        }

        try
        {
            using var response = await _client.GetAsync(path, token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // 404 is ordinary: champion select has no session until it starts.
                return default;
            }

            var json = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            return JsonSerializer.Deserialize<T>(json, SerializerOptions);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log.Warn($"LCU isteği başarısız ({path}): {exception.Message}");
            return default;
        }
    }

    public async Task<bool> PostAsync(string path, object body, CancellationToken token = default) =>
        await SendAsync(HttpMethod.Post, path, body, token).ConfigureAwait(false);

    public async Task<bool> DeleteAsync(string path, CancellationToken token = default) =>
        await SendAsync(HttpMethod.Delete, path, null, token).ConfigureAwait(false);

    /// <summary>
    /// Replaces a resource outright. Item sets are the reason this exists, and they are the reason
    /// to be careful with it: that endpoint takes the users entire set collection in one body, so a
    /// caller must read the current value and edit it rather than sending only its own additions.
    /// </summary>
    public async Task<bool> PutAsync(string path, object body, CancellationToken token = default) =>
        await SendAsync(HttpMethod.Put, path, body, token).ConfigureAwait(false);

    private async Task<bool> SendAsync(
        HttpMethod method, string path, object? body, CancellationToken token)
    {
        if (_client is null)
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(method, path);

            if (body is not null)
            {
                // The options are not optional here: serialising without them sends PascalCase
                // property names, which the client accepts with 200 and then ignores.
                request.Content = new StringContent(
                    JsonSerializer.Serialize(body, SerializerOptions), Encoding.UTF8, "application/json");
            }

            using var response = await _client.SendAsync(request, token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _log.Warn($"LCU {method} başarısız ({path}): {(int)response.StatusCode}");
            }

            return response.IsSuccessStatusCode;
        }
        catch (Exception exception)
        {
            _log.Warn($"LCU {method} hatası ({path}): {exception.Message}");
            return false;
        }
    }

    private void Disconnect()
    {
        _client?.Dispose();
        _client = null;
        _port = 0;
    }

    public void Dispose() => Disconnect();
}
