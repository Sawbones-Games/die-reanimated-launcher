using System.Security.Cryptography;

namespace DieReanimated.Launcher;

/// <summary>
/// News bodies and images, by content hash. A hash names exactly one sequence of bytes, so an item is fetched
/// once ever (<c>GET {server}/api/launcher/content/{sha256}</c>), verified against its own name, and kept under
/// <see cref="LauncherPaths.Content"/> for good. Nothing is ever re-checked or expired: the manifest names a new
/// hash when content changes. A failed fetch is a <c>null</c> — the screen shows what it has.
/// </summary>
public sealed class ContentStore
{
    private readonly LauncherPaths _paths;
    private readonly HttpClient _http;
    private readonly Action<string>? _log;

    public ContentStore(LauncherPaths paths, HttpClient? http = null, Action<string>? log = null)
    {
        _paths = paths; _log = log;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.TryParseAdd($"DIE-Reanimated-Launcher/{Defaults.VersionText}");
    }

    public static bool IsHash(string? s) => s is { Length: 64 } && s.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>The file for a hash if it is already here (its name is its proof), else <c>null</c>.</summary>
    public string? Local(string hash)
    {
        if (!IsHash(hash)) return null;
        string p = Path.Combine(_paths.Content, hash);
        return File.Exists(p) ? p : null;
    }

    /// <summary>The file for a hash, fetching it if this is the first time. <c>null</c> when it cannot be had.</summary>
    public async Task<string?> GetAsync(string serverUrl, string hash, CancellationToken ct = default)
    {
        if (Local(hash) is { } have) return have;
        if (!IsHash(hash)) return null;
        string url = serverUrl.TrimEnd('/') + "/api/launcher/content/" + hash;
        try
        {
            byte[] bytes = await _http.GetByteArrayAsync(url, ct);
            string actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (actual != hash)
            {
                _log?.Invoke($"content {hash[..12]}: the bytes do not match their name ({actual[..12]}) — discarded");
                return null;
            }
            string dest = Path.Combine(_paths.Content, hash);
            string tmp = dest + "." + Guid.NewGuid().ToString("N") + ".part";
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            try { File.Move(tmp, dest, overwrite: true); }
            catch (IOException) { File.Delete(tmp); if (!File.Exists(dest)) throw; }   // lost a race to ourselves: fine
            _log?.Invoke($"content {hash[..12]}: {bytes.Length} B fetched");
            return dest;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            _log?.Invoke($"content {hash[..12]}: {e.Message}");
            return null;
        }
    }

    /// <summary>A markdown body as text, or <c>null</c>.</summary>
    public async Task<string?> ReadTextAsync(string serverUrl, string hash, CancellationToken ct = default)
    {
        string? p = await GetAsync(serverUrl, hash, ct);
        if (p is null) return null;
        try { return await File.ReadAllTextAsync(p, ct); } catch (IOException) { return null; }
    }
}
