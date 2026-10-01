using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DieReanimated.Launcher;

/// <summary>
/// The server manifest — the ONE document the launcher fetches per start (<c>GET {server}/api/launcher</c>).
/// It names versions and addresses; it never carries a binary or a URL to one. Bytes come from the release named
/// by the version (<see cref="Defaults.ReleaseBaseUrl"/>).
/// </summary>
public sealed class Manifest
{
    [JsonPropertyName("v")] public int V { get; set; } = 1;
    [JsonPropertyName("server")] public ServerInfo Server { get; set; } = new();
    [JsonPropertyName("launcher")] public VersionInfo Launcher { get; set; } = new();
    [JsonPropertyName("patch")] public PatchInfo Patch { get; set; } = new();
    [JsonPropertyName("news")] public List<NewsItem> News { get; set; } = new();
    /// <summary>Community links the window offers (a Discord invite); absent → not shown. Only http(s) URLs are opened.</summary>
    [JsonPropertyName("links")] public Links Link { get; set; } = new();
    /// <summary>Present only while the server is in maintenance; <see cref="MaintenanceInfo.Allowed"/> says whether the
    /// account the launcher named (<c>?steamId=</c>) is whitelisted. Absent → no maintenance.</summary>
    [JsonPropertyName("maintenance")] public MaintenanceInfo? Maintenance { get; set; }

    public sealed class MaintenanceInfo
    {
        /// <summary>The operator's note for players (may be empty).</summary>
        [JsonPropertyName("message")] public string Message { get; set; } = "";
        /// <summary>This player may still log in (a whitelisted tester): show the normal button.</summary>
        [JsonPropertyName("allowed")] public bool Allowed { get; set; }
    }

    public sealed class Links
    {
        [JsonPropertyName("discord")] public string? Discord { get; set; }
    }

    public sealed class ServerInfo
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "Reanimated";
        /// <summary>The crib ("request server") the game logs in to — host or IP, written to ip.cfg.</summary>
        [JsonPropertyName("requestHost")] public string RequestHost { get; set; } = "";
        [JsonPropertyName("requestPort")] public int RequestPort { get; set; } = 1555;
        [JsonPropertyName("matchmakingHostEU")] public string MatchmakingHostEU { get; set; } = "";
        [JsonPropertyName("matchmakingHostNA")] public string MatchmakingHostNA { get; set; } = "";
        [JsonPropertyName("matchmakingPort")] public int MatchmakingPort { get; set; } = 2555;
    }

    public sealed class VersionInfo
    {
        /// <summary>The current release. Newer than the running launcher → UPDATE.</summary>
        [JsonPropertyName("version")] public string Version { get; set; } = "0.0.0";
        /// <summary>The oldest launcher the server still accepts. Older than this → UPDATE is the only action.</summary>
        [JsonPropertyName("min")] public string Min { get; set; } = "0.0.0";
    }

    public sealed class PatchInfo
    {
        /// <summary>The DieAuth.dll version players should have. Differs from the installed one → UPDATE.</summary>
        [JsonPropertyName("version")] public string Version { get; set; } = "0.0.0";
        /// <summary>The release tag whose assets carry that DieAuth.dll (defaults to <c>v{launcher.version}</c>).</summary>
        [JsonPropertyName("release")] public string? Release { get; set; }
    }

    public sealed class NewsItem
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        /// <summary>notes | event | roster | community</summary>
        [JsonPropertyName("kind")] public string Kind { get; set; } = "notes";
        [JsonPropertyName("date")] public string Date { get; set; } = "";
        [JsonPropertyName("eventDate")] public string? EventDate { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; } = "";
        [JsonPropertyName("summary")] public string Summary { get; set; } = "";
        /// <summary>Content hashes: fetched from <c>/api/launcher/content/{sha256}</c> only when not cached.</summary>
        [JsonPropertyName("image")] public string? Image { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        /// <summary>Art in the player's own install — a texture name or <c>Atlas#Sprite</c> (see GameArt), decoded
        /// locally; used when there is no image.</summary>
        [JsonPropertyName("art")] public string? Art { get; set; }
        /// <summary>Where the picture's interest is, <c>"x,y"</c> in 0–1 from the top-left; the part that survives a crop. Default centre.</summary>
        [JsonPropertyName("artFocus")] public string? ArtFocus { get; set; }

        /// <summary>The focal point as numbers (0.5, 0.5 when absent or malformed).</summary>
        public (double X, double Y) Focus()
        {
            var p = (ArtFocus ?? "").Split(',');
            if (p.Length == 2 && double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double x)
                && double.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double y))
                return (Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1));
            return (0.5, 0.5);
        }
    }

    /// <summary>A manifest from JSON; <c>null</c> when the text is not one (an HTML error page, a truncated file).</summary>
    public static Manifest? Parse(string json)
    {
        try
        {
            var m = JsonSerializer.Deserialize<Manifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return m is { V: >= 1 } ? m : null;
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>Fetches the manifest with <c>If-None-Match</c> and keeps the last good copy on disk, so an unchanged
/// manifest costs a 304 and an unreachable server costs nothing but a note.</summary>
public sealed class ManifestClient
{
    public enum Source { Network, NotModified, Cached, None }
    public readonly record struct Fetched(Manifest? Manifest, Source Source, string Detail);

    private readonly LauncherPaths _paths;
    private readonly HttpClient _http;

    public ManifestClient(LauncherPaths paths, HttpClient? http = null)
    {
        _paths = paths;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DIE-Reanimated-Launcher/" + Defaults.VersionText);
    }

    /// <param name="steamId">The signed-in Steam account (0 = unknown). The server uses it only during maintenance, to
    /// say whether this player is whitelisted; it does not change the cache identity (the ETag covers the answer).</param>
    public async Task<Fetched> FetchAsync(string serverUrl, CancellationToken ct = default, ulong steamId = 0)
    {
        string url = serverUrl.TrimEnd('/') + "/api/launcher";
        bool sameServer = CachedFrom() == url;
        string? etag = sameServer && File.Exists(_paths.ManifestEtagFile) ? File.ReadAllText(_paths.ManifestEtagFile).Trim() : null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, steamId != 0 ? $"{url}?steamId={steamId}" : url);
            if (etag is { Length: > 0 }) req.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
            using var resp = await _http.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.NotModified)
            {
                var cached = LoadCached(url);
                if (cached != null) return new Fetched(cached, Source.NotModified, "304 — unchanged");
            }
            if (resp.IsSuccessStatusCode)
            {
                string json = await resp.Content.ReadAsStringAsync(ct);
                var m = Manifest.Parse(json);
                if (m is null) return Fallback(url, "the response was not a manifest");
                File.WriteAllText(_paths.ManifestFile, json);
                File.WriteAllText(_paths.ManifestEtagFile, resp.Headers.ETag?.ToString() ?? "");
                File.WriteAllText(_paths.ManifestSourceFile, url);
                return new Fetched(m, Source.Network, $"{(int)resp.StatusCode} — {json.Length} B");
            }
            return Fallback(url, $"HTTP {(int)resp.StatusCode}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return Fallback(url, "offline: " + e.Message);
        }
    }

    private Fetched Fallback(string url, string why)
    {
        var cached = LoadCached(url);
        return new Fetched(cached, cached is null ? Source.None : Source.Cached, why + (cached is null ? "; no cached manifest" : "; using cached"));
    }

    /// <summary>The cached manifest, if it came from this server.</summary>
    public Manifest? LoadCached(string manifestUrl)
    {
        try
        {
            if (CachedFrom() != manifestUrl || !File.Exists(_paths.ManifestFile)) return null;
            return Manifest.Parse(File.ReadAllText(_paths.ManifestFile));
        }
        catch { return null; }
    }

    private string? CachedFrom()
    {
        try { return File.Exists(_paths.ManifestSourceFile) ? File.ReadAllText(_paths.ManifestSourceFile).Trim() : null; }
        catch { return null; }
    }
}
