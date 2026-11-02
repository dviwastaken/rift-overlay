using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RiftOverlay.Services.OpGg;

/// <summary>
/// Reads champion builds from OP.GG's published MCP server.
/// <para>
/// This is the one part of RiftOverlay that depends on somebody else's service, and it is a
/// courtesy dependency rather than a contract: the endpoint needs no key, but OP.GG publishes no
/// quota and no terms for it. Everything here is therefore built to fail quietly — a champion with
/// no build is a missing panel, never an error the user has to dismiss and never a stall in the
/// overlay's own loop.
/// </para>
/// <para>
/// The transport is MCP's Streamable HTTP: an <c>initialize</c> handshake returns a session id in a
/// header, an <c>initialized</c> notification follows, and only then are tool calls accepted. The
/// session is kept and reused; it is re-established automatically if the server drops it.
/// </para>
/// </summary>
public sealed class OpGgClient : IDisposable
{
    private const string Endpoint = "https://mcp-api.op.gg/mcp";
    private const string ToolName = "lol_get_champion_analysis";

    /// <summary>
    /// The closed set of fields the analysis is asked for. Requesting everything would pull
    /// synergies, trends and skill videos the client has no use for, on someone else's bandwidth.
    /// </summary>
    private static readonly string[] Fields =
    [
        "data.summary.average_stats.{play,win_rate,pick_rate,ban_rate,kda}",
        "data.summary.{id,is_rotation,is_rip}",
        "data.summary.positions[].name",
        "data.summary.positions[].stats.{play,win_rate,pick_rate,role_rate,ban_rate}",
        "data.summary.positions[].stats.tier_data.{tier,rank}",
        "data.starter_items.{ids[],ids_names[],play,win,pick_rate}",
        "data.boots.{ids[],ids_names[],play,win,pick_rate}",
        "data.core_items.{ids[],ids_names[],play,win,pick_rate}",
        "data.fourth_items[].{ids[],ids_names[],play,win,pick_rate}",
        "data.fifth_items[].{ids[],ids_names[],play,win,pick_rate}",
        "data.sixth_items[].{ids[],ids_names[],play,win,pick_rate}",
        "data.last_items[].{ids[],ids_names[],play,win,pick_rate}",
        "data.skills.{order[],play,win,pick_rate}",
        "data.skill_masteries.{ids[],play,win,pick_rate}",
        "data.skill_masteries.builds[].{order[],play,win,pick_rate}",
        "data.runes.{primary_page_id,primary_page_name,primary_rune_ids[],primary_rune_names[]," +
        "secondary_page_id,secondary_page_name,secondary_rune_ids[],secondary_rune_names[]," +
        "stat_mod_ids[],play,win,pick_rate}",
        "data.summoner_spells.{ids[],ids_names[],play,win,pick_rate}",
        "data.weak_counters[].{champion_id,champion_name,play,win,win_rate,my_win_rate,counter_win_rate}",
        "data.strong_counters[].{champion_id,champion_name,play,win,win_rate,my_win_rate,counter_win_rate}",
        "data.{damage_type}",
    ];

    private readonly HttpClient _http;
    private readonly ILog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, ChampionAnalysis> _cache = new(StringComparer.OrdinalIgnoreCase);

    private string? _sessionId;
    private int _nextId = 1;

    public OpGgClient(ILog? log = null, HttpMessageHandler? handler = null)
    {
        _log = log ?? NullLog.Instance;

        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    /// <summary>Set once a call has succeeded; until then the build panel has nothing to show.</summary>
    public bool IsReady { get; private set; }

    /// <summary>
    /// Fetches a champion's build, or null when it cannot be had. Results are cached in memory for
    /// the life of the process: a champion's aggregate moves over hours, and champion select asks
    /// the same question repeatedly within seconds.
    /// </summary>
    /// <param name="champion">Name as the game spells it; the server tolerates spacing and case.</param>
    /// <param name="position">top, jungle, mid, adc, support — or "all".</param>
    public async Task<ChampionAnalysis?> GetBuildAsync(
        string champion, string position, string locale = "tr_TR", CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(champion))
        {
            return null;
        }

        var key = $"{champion}|{position}|{locale}";

        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(token).ConfigureAwait(false);

        try
        {
            if (_cache.TryGetValue(key, out cached))
            {
                return cached;
            }

            var analysis = await FetchAsync(champion, position, locale, token).ConfigureAwait(false);

            if (analysis is not null)
            {
                _cache[key] = analysis;
                IsReady = true;
            }

            return analysis;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log.Error($"OP.GG build alınamadı: {champion} ({position})", exception);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ChampionAnalysis?> FetchAsync(
        string champion, string position, string locale, CancellationToken token)
    {
        var text = await CallToolAsync(champion, position, locale, token).ConfigureAwait(false);

        if (text is null)
        {
            return null;
        }

        // The server reports field paths it did not recognise instead of failing. Logging it is how
        // a schema change surfaces as a message rather than as a panel that quietly went blank.
        if (text.Contains("FieldDiagnostics", StringComparison.Ordinal))
        {
            _log.Info($"OP.GG alan uyarısı ({champion}): tanınmayan alan istendi, şema değişmiş olabilir.");
        }

        return OpGgResponseParser.Parse(text);
    }

    private async Task<string?> CallToolAsync(
        string champion, string position, string locale, CancellationToken token)
    {
        var response = await SendToolCallAsync(champion, position, locale, token).ConfigureAwait(false);

        // A dropped session comes back as an error rather than a transport failure, so one retry
        // with a fresh handshake is the difference between working after a while and never working
        // again until restart.
        if (response is null)
        {
            _sessionId = null;
            response = await SendToolCallAsync(champion, position, locale, token).ConfigureAwait(false);
        }

        return response;
    }

    private async Task<string?> SendToolCallAsync(
        string champion, string position, string locale, CancellationToken token)
    {
        if (!await EnsureSessionAsync(token).ConfigureAwait(false))
        {
            return null;
        }

        var request = new
        {
            jsonrpc = "2.0",
            id = _nextId++,
            method = "tools/call",
            @params = new
            {
                name = ToolName,
                arguments = new
                {
                    game_mode = "ranked",
                    champion,
                    position,
                    lang = locale,
                    desired_output_fields = Fields,
                },
            },
        };

        var body = await PostAsync(request, token).ConfigureAwait(false);

        if (body is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(body);

        if (!document.RootElement.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("content", out var content))
        {
            return null;
        }

        var builder = new StringBuilder();

        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("text", out var value) && value.GetString() is { } line)
            {
                builder.AppendLine(line);
            }
        }

        var text = builder.ToString();

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private async Task<bool> EnsureSessionAsync(CancellationToken token)
    {
        if (_sessionId is not null)
        {
            return true;
        }

        var initialize = new
        {
            jsonrpc = "2.0",
            id = _nextId++,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2025-06-18",
                capabilities = new { },
                clientInfo = new { name = "RiftOverlay", version = "1.0" },
            },
        };

        using var content = Json(initialize);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };

        Accept(request);

        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode ||
            !response.Headers.TryGetValues("Mcp-Session-Id", out var values))
        {
            return false;
        }

        _sessionId = values.FirstOrDefault();

        if (_sessionId is null)
        {
            return false;
        }

        // The server rejects tool calls until this notification lands; it takes no reply.
        await PostAsync(new { jsonrpc = "2.0", method = "notifications/initialized" }, token)
            .ConfigureAwait(false);

        return true;
    }

    private async Task<string?> PostAsync(object payload, CancellationToken token)
    {
        using var content = Json(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };

        Accept(request);

        if (_sessionId is not null)
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
        }

        using var response = await _http.SendAsync(request, token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

        return string.IsNullOrWhiteSpace(body) ? null : Unwrap(body);
    }

    /// <summary>
    /// The endpoint may answer as plain JSON or as a server-sent event stream, depending on the
    /// request. Taking the last <c>data:</c> line covers both, since a single JSON body has none.
    /// </summary>
    private static string Unwrap(string body)
    {
        if (!body.Contains("data:", StringComparison.Ordinal))
        {
            return body;
        }

        var last = body
            .Split('\n')
            .Select(line => line.TrimStart())
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].Trim())
            .LastOrDefault(line => line.StartsWith('{'));

        return last ?? body;
    }

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static void Accept(HttpRequestMessage request)
    {
        // Both are required: the server negotiates between a plain body and an event stream, and
        // omitting either has it refuse the request outright.
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
