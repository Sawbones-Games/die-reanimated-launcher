using System.Net;
using System.Security.Cryptography;
using DieReanimated.Launcher;
using Xunit;

namespace Launcher.Tests;

public sealed class LaunchArgumentsTests
{
    // The retail launcher's exact rules, reproduced.
    [Theory]
    [InlineData(null, false, 1920, 1080, "-popupwindow -screen-width 1920 -screen-height 1080")]   // fresh install: default borderless, no saved resolution
    [InlineData(1, true, 1920, 1080, "-popupwindow")]                                                // borderless, resolution saved
    [InlineData(0, true, 1920, 1080, "")]                                                            // windowed
    [InlineData(2, true, 2560, 1440, "-popupwindow")]                                                // fullscreen (old launcher builds): the Crib can't run it → borderless
    [InlineData(2, false, 2560, 1440, "-popupwindow -screen-width 2560 -screen-height 1440")]
    public void MatchesTheRetailStub(int? mode, bool saved, int w, int h, string expected) =>
        Assert.Equal(expected, GameProcess.BuildArguments(mode, saved, w, h));

    [Fact]
    public void ForwardsOurOwnArguments() =>
        Assert.Equal("-popupwindow -nolog", GameProcess.BuildArguments(1, true, 1920, 1080, new[] { "-nolog" }));
}

public sealed class VersionRuleTests
{
    [Theory]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.1.1", "0.1.0", true)]
    [InlineData("0.0.9", "0.1.0", false)]
    [InlineData("garbage", "0.1.0", false)]
    public void NewerMeansStrictlyGreater(string candidate, string running, bool expected) =>
        Assert.Equal(expected, SelfUpdate.IsNewer(candidate, Version.Parse(running)));

    [Theory]
    [InlineData("0.1.0", "0.1.0", false)]
    [InlineData("0.2.0", "0.1.0", true)]
    [InlineData("0.1.0", "0.1.0.7", false)]   // a 4-part build number never matters
    public void MinIsInclusive(string min, string running, bool expected) =>
        Assert.Equal(expected, SelfUpdate.IsOlderThanMin(min, Version.Parse(running)));

    [Fact]
    public void TagIsVPrefixedOnce()
    {
        Assert.Equal("v0.2.0", Releases.TagFor("0.2.0"));
        Assert.Equal("v0.2.0", Releases.TagFor("v0.2.0"));
    }
}

public sealed class SumsTests
{
    [Fact]
    public void ParsesSha256sumOutput()
    {
        string text = "ab" + new string('0', 62) + "  DIE-Reanimated-Launcher.exe\n" +
                      "CD" + new string('1', 62) + " *DieAuth.dll\n" +
                      "not a line\n";
        var sums = Releases.ParseSums(text);
        Assert.Equal(2, sums.Count);
        Assert.Equal("ab" + new string('0', 62), sums["DIE-Reanimated-Launcher.exe"]);
        Assert.Equal("cd" + new string('1', 62), sums["dieauth.dll"]);   // case-insensitive names, lower-case hex
    }
}

public sealed class IpConfigTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dieipcfg_" + Guid.NewGuid().ToString("N"));
    public IpConfigTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static Manifest.ServerInfo Server(string host) => new() { RequestHost = host, RequestPort = 1555, MatchmakingHostEU = "mm-eu." + host, MatchmakingHostNA = "mm-na." + host, MatchmakingPort = 2555 };

    [Fact]
    public void RewritesOnlyTheAddressesItOwns_AndReportsUnchanged()
    {
        string path = Path.Combine(_dir, "ip.cfg");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <branch name="public">
                <RequestServerIP>127.0.0.1</RequestServerIP>
                <RequestServerPort>1555</RequestServerPort>
                <MatchmakingServerIPEU>127.0.0.1</MatchmakingServerIPEU>
                <MatchmakingServerIPNA>127.0.0.1</MatchmakingServerIPNA>
                <StatsServerIP>212.62.72.119</StatsServerIP>
                <MatchmakingServerPort>2555</MatchmakingServerPort>
              </branch>
              <branch name="dev"><RequestServerIP>10.0.0.1</RequestServerIP></branch>
            </root>
            """);

        Assert.True(IpConfig.Apply(path, Server("crib.example.org"), out string detail));
        Assert.Contains("crib.example.org", detail);
        string text = File.ReadAllText(path);
        Assert.Contains("<RequestServerIP>crib.example.org</RequestServerIP>", text);
        Assert.Contains("<MatchmakingServerIPEU>mm-eu.crib.example.org</MatchmakingServerIPEU>", text);
        Assert.Contains("<StatsServerIP>212.62.72.119</StatsServerIP>", text);     // untouched
        Assert.Contains("<RequestServerIP>10.0.0.1</RequestServerIP>", text);      // other branch untouched
        Assert.Equal("crib.example.org", IpConfig.CurrentRequestHost(path));

        Assert.False(IpConfig.Apply(path, Server("crib.example.org"), out detail));
        Assert.Equal("unchanged", detail);
    }

    [Fact]
    public void CreatesTheFileWhenMissing()
    {
        string path = Path.Combine(_dir, "ip.cfg");
        Assert.True(IpConfig.Apply(path, Server("h"), out _));
        Assert.Equal("h", IpConfig.CurrentRequestHost(path));
    }
}

public sealed class ManifestTests
{
    [Fact]
    public void ParsesTheServerShape()
    {
        var m = Manifest.Parse("""{"v":1,"server":{"name":"R","requestHost":"h","requestPort":1555,"matchmakingHostEU":"e","matchmakingHostNA":"n","matchmakingPort":2555},"launcher":{"version":"0.2.0","min":"0.1.0"},"patch":{"version":"0.1.1"},"news":[{"id":"a","kind":"event","date":"2026-09-14","eventDate":"2026-09-20","title":"T","summary":"S","image":"abc","body":"def"}]}""")!;
        Assert.Equal("h", m.Server.RequestHost);
        Assert.Equal("0.2.0", m.Launcher.Version);
        Assert.Equal("0.1.1", m.Patch.Version);
        Assert.Single(m.News);
        Assert.Equal("event", m.News[0].Kind);
        Assert.Equal("2026-09-20", m.News[0].EventDate);
    }
}

/// <summary>Release downloads against a local HTTP server standing in for GitHub: the digest gate is the point.</summary>
public sealed class ReleaseDownloadTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dierel_" + Guid.NewGuid().ToString("N"));
    private readonly HttpListener _listener = new();
    private readonly string _prefix;
    private readonly byte[] _payload = System.Text.Encoding.UTF8.GetBytes("pretend this is DieAuth.dll");

    public ReleaseDownloadTests()
    {
        Directory.CreateDirectory(_dir);
        int port = Random.Shared.Next(20000, 40000);
        _prefix = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(_prefix);
        _listener.Start();
        _ = Task.Run(Serve);
    }

    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); } catch { break; }
            string path = ctx.Request.Url!.AbsolutePath;
            byte[] body;
            if (path.EndsWith("/SHA256SUMS"))
            {
                string good = Convert.ToHexString(SHA256.HashData(_payload)).ToLowerInvariant();
                string tampered = path.Contains("v9.9.9") ? new string('0', 64) : good;
                body = System.Text.Encoding.UTF8.GetBytes($"{tampered}  DieAuth.dll\n");
            }
            else if (path.EndsWith("/DieAuth.dll")) body = _payload;
            else { ctx.Response.StatusCode = 404; ctx.Response.Close(); continue; }
            ctx.Response.ContentLength64 = body.Length;
            await ctx.Response.OutputStream.WriteAsync(body);
            ctx.Response.Close();
        }
    }

    public void Dispose() { try { _listener.Stop(); } catch { } try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public async Task DownloadsAndVerifies()
    {
        var releases = new Releases(baseUrl: _prefix + "releases/download");
        string path = await releases.DownloadVerifiedAsync("v0.1.0", "DieAuth.dll", _dir);
        Assert.Equal(_payload, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task ReportsProgressUpToTheTotal()
    {
        var releases = new Releases(baseUrl: _prefix + "releases/download");
        var seen = new List<Releases.Progress>();
        var progress = new SynchronousProgress(seen.Add);
        await releases.DownloadVerifiedAsync("v1.0.0", "DieAuth.dll", _dir, progress: progress);
        Assert.NotEmpty(seen);
        Assert.Equal(_payload.Length, seen[^1].Done);
        Assert.Equal(_payload.Length, seen[^1].Total);
        Assert.Equal(1.0, seen[^1].Fraction);
    }

    private sealed class SynchronousProgress : IProgress<Releases.Progress>
    {
        private readonly Action<Releases.Progress> _a; public SynchronousProgress(Action<Releases.Progress> a) => _a = a;
        public void Report(Releases.Progress value) => _a(value);
    }

    [Fact]
    public async Task RefusesADigestMismatch()
    {
        var releases = new Releases(baseUrl: _prefix + "releases/download");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => releases.DownloadVerifiedAsync("v9.9.9", "DieAuth.dll", _dir));
        Assert.Contains("does not match", ex.Message);
        Assert.False(File.Exists(Path.Combine(_dir, "DieAuth.dll")));
        Assert.False(File.Exists(Path.Combine(_dir, "DieAuth.dll.part")));
    }

    /// <summary>The tag comes from the manifest. A server must not be able to walk it out of this repository's
    /// releases into another repository's — which would bring that repository's SHA256SUMS along with it.</summary>
    [Theory]
    [InlineData("v1/../../evil/releases/download/v0.1.0")]
    [InlineData("v0.1.0/../../../evil/releases/download/v0.1.0")]
    [InlineData("../v0.1.0")]
    [InlineData("v0.1.0/x")]
    [InlineData("v0.1.0?x=1")]
    [InlineData("v0.1.0%2F..")]
    [InlineData("0.1.0")]
    [InlineData("v0.1.0\n")]
    [InlineData("")]
    public async Task RefusesATagThatIsNotAPlainVersion(string tag)
    {
        var releases = new Releases(baseUrl: _prefix + "releases/download");
        await Assert.ThrowsAsync<ArgumentException>(() => releases.DownloadVerifiedAsync(tag, "DieAuth.dll", _dir));
        Assert.False(File.Exists(Path.Combine(_dir, "DieAuth.dll")));
    }

    [Fact]
    public async Task RefusesAnAssetTheReleaseDoesNotList()
    {
        var releases = new Releases(baseUrl: _prefix + "releases/download");
        await Assert.ThrowsAsync<InvalidOperationException>(() => releases.DownloadVerifiedAsync("v0.1.0", "Other.exe", _dir));
    }
}

public sealed class SelfUpdateGuardTests
{
    [Fact]
    public void RefusesToRenameTheDotnetHost()
    {
        // Under `dotnet test` the process is dotnet.exe / testhost — never a launcher binary.
        Assert.False(SelfUpdate.CanSelfUpdate && Path.GetFileName(SelfUpdate.RunningExe!).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase));
        if (!SelfUpdate.CanSelfUpdate)
            Assert.Throws<InvalidOperationException>(() => SelfUpdate.Apply("nonexistent.exe", Array.Empty<string>()));
    }
}
