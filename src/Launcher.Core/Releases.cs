using System.Diagnostics;
using System.Security.Cryptography;

namespace DieReanimated.Launcher;

/// <summary>
/// Downloads release assets and verifies them. Policy comes from the server (a version); bytes come from the
/// GitHub Releases of the repository that built this launcher (<see cref="Defaults.ReleaseBaseUrl"/>/v{version}/{asset})
/// and are checked against the <c>SHA256SUMS</c> published in the same release before anything is done with them.
///
/// With a token (the dev channel: a private repository's assets are not public) the same release is reached
/// through the GitHub API instead — the asset list of the tag, then each asset as <c>application/octet-stream</c>.
/// The verification is the same either way.
/// </summary>
public sealed class Releases
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _repository;
    private readonly string? _token;

    public Releases(HttpClient? http = null, string? baseUrl = null, string? repository = null, string? token = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.UserAgent.TryParseAdd($"DIE-Reanimated-Launcher/{Defaults.VersionText}");
        _baseUrl = (baseUrl ?? Defaults.ReleaseBaseUrl).TrimEnd('/');
        _repository = repository ?? Defaults.ReleaseRepository;
        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    /// <summary>The URL an asset of a tag is fetched from: the public download URL, or with a token the API asset
    /// URL resolved from the tag's release.</summary>
    private async Task<(string url, string? accept)> LocateAsync(string tag, string asset, CancellationToken ct)
    {
        if (_token is null) return ($"{_baseUrl}/{tag}/{asset}", null);
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_repository}/releases/tags/{tag}");
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        req.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"release {tag} of {_repository}: HTTP {(int)resp.StatusCode} from the GitHub API");
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(await resp.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            if (a.GetProperty("name").GetString() == asset) return (a.GetProperty("url").GetString()!, "application/octet-stream");
        throw new InvalidOperationException($"release {tag} of {_repository} has no asset named {asset}");
    }

    private async Task<HttpResponseMessage> FetchAsync(string tag, string asset, CancellationToken ct)
    {
        var (url, accept) = await LocateAsync(tag, asset, ct);
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (_token != null) req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _token);
        if (accept != null) req.Headers.Accept.ParseAdd(accept);
        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return resp;
    }

    public static string TagFor(string version) => version.StartsWith('v') ? version : "v" + version;

    /// <summary>A release tag this launcher will fetch: <c>v</c> and a dotted version, nothing else. The tag comes from
    /// the server's manifest and is spliced into a URL path, so anything more (a <c>/</c>, a <c>..</c>, a query) could
    /// walk the request out of this repository's releases — and bring another repository's SHA256SUMS with it.</summary>
    public static bool IsReleaseTag(string? tag) =>
        tag is { Length: > 1 } && System.Text.RegularExpressions.Regex.IsMatch(tag, @"^v\d+(\.\d+){1,3}\z");

    /// <summary>Parse <c>sha256sum</c> output: <c>&lt;hex&gt;  &lt;name&gt;</c> per line (a leading <c>*</c> on the name is binary mode).</summary>
    public static Dictionary<string, string> ParseSums(string text)
    {
        var sums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length < 66) continue;
            string hex = line[..64];
            string name = line[64..].TrimStart(' ', '*');
            if (hex.All(Uri.IsHexDigit) && name.Length > 0) sums[name] = hex.ToLowerInvariant();
        }
        return sums;
    }

    public static string Sha256Hex(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
    }

    /// <summary>Bytes received so far and the total when the server said one (else -1).</summary>
    public readonly record struct Progress(long Done, long Total)
    {
        public double? Fraction => Total > 0 ? Math.Clamp((double)Done / Total, 0, 1) : null;
    }

    /// <summary>Download <paramref name="asset"/> from the release <paramref name="tag"/> into <paramref name="destDir"/>,
    /// verify it against that release's SHA256SUMS, and return its path. Throws on any mismatch.</summary>
    public async Task<string> DownloadVerifiedAsync(string tag, string asset, string destDir, Action<string>? log = null, CancellationToken ct = default,
        IProgress<Progress>? progress = null)
    {
        if (!IsReleaseTag(tag))
            throw new ArgumentException($"'{tag}' is not a release tag (v1.2.3); refusing to fetch it", nameof(tag));
        Directory.CreateDirectory(destDir);
        string sumsText;
        using (var sumsResp = await FetchAsync(tag, Defaults.SumsAsset, ct)) sumsText = await sumsResp.Content.ReadAsStringAsync(ct);
        var sums = ParseSums(sumsText);
        if (!sums.TryGetValue(asset, out string? expected))
            throw new InvalidOperationException($"{Defaults.SumsAsset} of {tag} has no entry for {asset}");

        string dest = Path.Combine(destDir, asset);
        string tmp = dest + ".part";
        using (var resp = await FetchAsync(tag, asset, ct))
        {
            long total = resp.Content.Headers.ContentLength ?? -1;
            progress?.Report(new Progress(0, total));
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(tmp);
            byte[] buf = new byte[256 * 1024];
            long done = 0; int n;
            while ((n = await src.ReadAsync(buf, ct)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, n), ct);
                done += n;
                progress?.Report(new Progress(done, total));
            }
        }
        string actual = Sha256Hex(tmp);
        if (actual != expected)
        {
            File.Delete(tmp);
            throw new InvalidOperationException($"{asset} from {tag}: SHA-256 {actual} does not match the release's {expected}");
        }
        File.Move(tmp, dest, overwrite: true);
        log?.Invoke($"{asset} {tag} verified ({new FileInfo(dest).Length:N0} B, sha256 {expected[..12]}…)");
        return dest;
    }
}

/// <summary>Replaces the running executable with a downloaded one and restarts. Windows cannot overwrite a running
/// exe but can rename it: current → <c>.old</c>, new → current, start, exit. The new instance removes <c>.old</c>.</summary>
public static class SelfUpdate
{
    public static string? RunningExe => Environment.ProcessPath;

    public static bool IsNewer(string candidate, Version running) =>
        Version.TryParse(candidate, out var v) && Normalize(v) > Normalize(running);

    public static bool IsOlderThanMin(string min, Version running) =>
        Version.TryParse(min, out var v) && Normalize(running) < Normalize(v);

    private static Version Normalize(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>True only when the running process is a published launcher binary — never the dotnet host running
    /// a .dll (development, CI), which must not be renamed out from under itself.</summary>
    public static bool CanSelfUpdate =>
        RunningExe is { } p && Path.GetFileName(p).EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
        && !Path.GetFileName(p).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Swap the binary and start the new one with the same arguments. Returns the started process; the
    /// caller must exit promptly.</summary>
    public static Process Apply(string newExe, IEnumerable<string> args)
    {
        if (!CanSelfUpdate) throw new InvalidOperationException("self-update applies only to the published launcher executable, not to a .dll run under dotnet");
        string current = RunningExe ?? throw new InvalidOperationException("cannot determine the running executable");
        string old = current + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(current, old);
        try { File.Move(newExe, current); }
        catch { File.Move(old, current); throw; }   // put ourselves back if the new file cannot land
        var psi = new ProcessStartInfo(current) { WorkingDirectory = Path.GetDirectoryName(current)!, UseShellExecute = false };
        foreach (string a in args) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("--updated");
        return Process.Start(psi) ?? throw new InvalidOperationException("the updated launcher did not start");
    }

    /// <summary>Call at start-up: removes the previous binary left by <see cref="Apply"/>.</summary>
    public static void CleanUp()
    {
        try
        {
            string? current = RunningExe;
            if (current != null && File.Exists(current + ".old")) File.Delete(current + ".old");
        }
        catch { /* still locked by the exiting instance — next start */ }
    }
}
