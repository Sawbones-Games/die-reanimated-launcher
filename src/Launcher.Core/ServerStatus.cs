using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace DieReanimated.Launcher;

/// <summary>
/// The live picture the server publishes at <c>GET {server}/api/launcher/status</c>: what is up, how many players
/// are on, and what is being played, by region. Read-only, cheap (the server memoises it for a few seconds and
/// rate-limits per client), never cached to disk — it is only ever "now".
/// </summary>
public sealed class ServerStatus
{
    [JsonPropertyName("crib")] public bool Crib { get; set; }
    [JsonPropertyName("matchmaking")] public bool Matchmaking { get; set; }
    /// <summary>Players signed in to the crib.</summary>
    [JsonPropertyName("online")] public int Online { get; set; }
    /// <summary>Players currently inside a match.</summary>
    [JsonPropertyName("inPlay")] public int InPlay { get; set; }
    [JsonPropertyName("matches")] public int Matches { get; set; }
    [JsonPropertyName("regions")] public List<Region> Regions { get; set; } = new();
    [JsonPropertyName("live")] public List<LiveMatch> Live { get; set; } = new();
    /// <summary>The community Discord's counts, when the server knows them (it asks Discord, the launcher never does).</summary>
    [JsonPropertyName("discord")] public DiscordCounts? Discord { get; set; }
    /// <summary>The server is in maintenance: only whitelisted accounts can log in.</summary>
    [JsonPropertyName("maintenance")] public bool Maintenance { get; set; }

    public sealed class DiscordCounts
    {
        [JsonPropertyName("members")] public int Members { get; set; }
        [JsonPropertyName("online")] public int Online { get; set; }
    }

    public sealed class Region
    {
        /// <summary>Server-side region code (<c>eu</c>, <c>na</c>, <c>ap</c>, <c>local</c>…).</summary>
        [JsonPropertyName("region")] public string Code { get; set; } = "";
        [JsonPropertyName("matches")] public int Matches { get; set; }
        [JsonPropertyName("players")] public int Players { get; set; }
        [JsonPropertyName("online")] public bool Online { get; set; }
    }

    public sealed class LiveMatch
    {
        [JsonPropertyName("mode")] public string Mode { get; set; } = "";
        [JsonPropertyName("region")] public string Region { get; set; } = "";
        [JsonPropertyName("players")] public int Players { get; set; }
    }

    /// <summary>A player-facing name for a region code. Unknown codes are shown as given.</summary>
    public static string RegionName(string code) => code.ToLowerInvariant() switch
    {
        "eu" => "Europe",
        "na" => "North America",
        "ap" or "asia" or "apac" => "Asia · Pacific",
        "local" => "Local",
        _ => code,
    };
}

/// <summary>Fetches <see cref="ServerStatus"/>. Unreachable, slow or malformed → <c>null</c>; the caller shows
/// "offline" and tries again later. Nothing is retried in a loop here.</summary>
public sealed class StatusClient
{
    private readonly HttpClient _http;

    public StatusClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"DIE-Reanimated-Launcher/{Defaults.VersionText}");
    }

    public async Task<ServerStatus?> FetchAsync(string serverUrl, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<ServerStatus>(serverUrl.TrimEnd('/') + "/api/launcher/status", ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
