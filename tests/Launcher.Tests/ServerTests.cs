using System.Net;
using DieReanimated.Launcher;
using Xunit;

namespace Launcher.Tests;

/// <summary>A tiny server that plays the control plane: manifest with ETag/304, status, or an HTML page.</summary>
internal sealed class FakeServer : IDisposable
{
    private readonly HttpListener _listener = new();
    public string Url { get; }
    public string ManifestJson = """{"v":1,"server":{"name":"Fake","requestHost":"h","requestPort":1555},"launcher":{"version":"0.1.0","min":"0.1.0"},"patch":{"version":"0.1.0"},"news":[]}""";
    public string StatusJson = """{"crib":true,"matchmaking":false,"online":3,"inPlay":2,"matches":1,"regions":[{"region":"eu","matches":1,"players":2,"online":true},{"region":"na","matches":0,"players":0,"online":false}],"live":[{"mode":"Scavenger","region":"eu","players":2}]}""";
    public bool ServeHtml;
    public int Hits;
    /// <summary>The query string of the last manifest request ("" when none).</summary>
    public string LastManifestQuery = "";
    public readonly Dictionary<string, byte[]> Content = new();

    public FakeServer()
    {
        int port = Random.Shared.Next(20000, 40000);
        Url = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(Url + "/");
        _listener.Start();
        _ = Task.Run(Serve);
    }

    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); } catch { break; }
            Hits++;
            string path = ctx.Request.Url!.AbsolutePath;
            string body; string type = "application/json";
            if (ServeHtml) { body = "<html><body>admin</body></html>"; type = "text/html"; }
            else if (path == "/api/launcher")
            {
                LastManifestQuery = ctx.Request.Url!.Query;
                string etag = "\"" + ManifestJson.GetHashCode().ToString("x") + "\"";
                if (ctx.Request.Headers["If-None-Match"] == etag) { ctx.Response.StatusCode = 304; ctx.Response.Close(); continue; }
                ctx.Response.Headers["ETag"] = etag;
                body = ManifestJson;
            }
            else if (path == "/api/launcher/status") body = StatusJson;
            else if (path.StartsWith("/api/launcher/content/") && Content.TryGetValue(path[22..], out byte[]? raw))
            {
                ctx.Response.ContentLength64 = raw.Length;
                await ctx.Response.OutputStream.WriteAsync(raw);
                ctx.Response.Close(); continue;
            }
            else { ctx.Response.StatusCode = 404; ctx.Response.Close(); continue; }
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(body);
            ctx.Response.ContentType = type;
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        }
    }

    public void Dispose() { try { _listener.Stop(); } catch { } }
}

public sealed class ManifestClientTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "diemf_" + Guid.NewGuid().ToString("N"));
    private readonly FakeServer _server = new();
    public void Dispose() { _server.Dispose(); try { Directory.Delete(_root, true); } catch { } }

    [Fact]
    public async Task FetchesThenRevalidatesWithTheEtag()
    {
        var client = new ManifestClient(new LauncherPaths(_root));
        var first = await client.FetchAsync(_server.Url);
        Assert.Equal(ManifestClient.Source.Network, first.Source);
        Assert.Equal("Fake", first.Manifest!.Server.Name);

        var second = await client.FetchAsync(_server.Url);
        Assert.Equal(ManifestClient.Source.NotModified, second.Source);
        Assert.Equal("Fake", second.Manifest!.Server.Name);
    }

    [Fact]
    public async Task AnHtmlPageIsNotAManifest_FallsBackToTheCache()
    {
        var client = new ManifestClient(new LauncherPaths(_root));
        await client.FetchAsync(_server.Url);
        _server.ServeHtml = true;
        var f = await client.FetchAsync(_server.Url);
        Assert.Equal(ManifestClient.Source.Cached, f.Source);
        Assert.Equal("Fake", f.Manifest!.Server.Name);
    }

    [Fact]
    public async Task AnHtmlPageWithNoCacheIsNoManifest_NotAnException()
    {
        _server.ServeHtml = true;
        var f = await new ManifestClient(new LauncherPaths(_root)).FetchAsync(_server.Url);
        Assert.Equal(ManifestClient.Source.None, f.Source);
        Assert.Null(f.Manifest);
    }

    [Fact]
    public async Task TheCacheBelongsToTheServerItCameFrom()
    {
        var paths = new LauncherPaths(_root);
        var client = new ManifestClient(paths);
        await client.FetchAsync(_server.Url);
        Assert.NotNull(client.LoadCached(_server.Url + "/api/launcher"));
        Assert.Null(client.LoadCached("http://elsewhere.example/api/launcher"));

        // an unreachable *different* server must not be answered with this server's manifest
        var other = await client.FetchAsync("http://127.0.0.1:1");
        Assert.Equal(ManifestClient.Source.None, other.Source);
    }

    [Fact]
    public async Task TheSteamIdRidesTheQuery_ButTheCacheStaysTheServers()
    {
        var client = new ManifestClient(new LauncherPaths(_root));
        await client.FetchAsync(_server.Url, steamId: 76561198000000001UL);
        Assert.Equal("?steamId=76561198000000001", _server.LastManifestQuery);
        Assert.NotNull(client.LoadCached(_server.Url + "/api/launcher"));

        await client.FetchAsync(_server.Url);
        Assert.Equal("", _server.LastManifestQuery);
    }

    [Fact]
    public void ParseIsTotal()
    {
        Assert.Null(Manifest.Parse("<html>"));
        Assert.Null(Manifest.Parse(""));
        Assert.Null(Manifest.Parse("{\"v\":0}"));
        Assert.NotNull(Manifest.Parse("{\"v\":1}"));
    }
}

public sealed class StatusClientTests : IDisposable
{
    private readonly FakeServer _server = new();
    public void Dispose() => _server.Dispose();

    [Fact]
    public async Task ReadsTheLivePicture()
    {
        var s = await new StatusClient().FetchAsync(_server.Url);
        Assert.NotNull(s);
        Assert.True(s!.Crib); Assert.False(s.Matchmaking);
        Assert.Equal(3, s.Online); Assert.Equal(2, s.InPlay); Assert.Equal(1, s.Matches);
        Assert.Equal(2, s.Regions.Count);
        Assert.Equal("Europe", ServerStatus.RegionName(s.Regions[0].Code));
        Assert.False(s.Regions[1].Online);
        Assert.Single(s.Live);
        Assert.Equal("Scavenger", s.Live[0].Mode);
    }

    [Fact]
    public async Task UnreachableOrNotJsonIsNull_NotAnException()
    {
        Assert.Null(await new StatusClient().FetchAsync("http://127.0.0.1:1"));
        _server.ServeHtml = true;
        Assert.Null(await new StatusClient().FetchAsync(_server.Url));
    }

    [Theory]
    [InlineData("eu", "Europe")]
    [InlineData("NA", "North America")]
    [InlineData("ap", "Asia · Pacific")]
    [InlineData("local", "Local")]
    [InlineData("tokyo", "tokyo")]
    public void RegionNamesArePlayerFacing(string code, string name) => Assert.Equal(name, ServerStatus.RegionName(code));
}

/// <summary>Maintenance: a live manifest naming it (and not allowing this player) turns the button into MAINTENANCE;
/// a whitelisted player, or a manifest from the cache, does not.</summary>
public sealed class MaintenanceStateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "diemt_" + Guid.NewGuid().ToString("N"));
    private readonly string _game;
    private readonly FakeServer _server = new();

    public MaintenanceStateTests()
    {
        _game = Path.Combine(_root, "game");
        Directory.CreateDirectory(_game);
        File.WriteAllText(Path.Combine(_game, Defaults.CribExe), "");   // enough for GameInstall.At
    }
    public void Dispose() { _server.Dispose(); try { Directory.Delete(_root, true); } catch { } }

    private const string Base = """{"v":1,"server":{"name":"Fake","requestHost":"h","requestPort":1555},"launcher":{"version":"0.0.1","min":"0.0.1"},"patch":{"version":"0.1.0"},"news":[]""";

    private LauncherSession Session()
    {
        var paths = new LauncherPaths(Path.Combine(_root, "data"));
        return new LauncherSession(paths, new Settings { ServerUrl = _server.Url }, new Log(paths), _game);
    }

    [Fact]
    public void ParsesTheMaintenanceBlock()
    {
        var m = Manifest.Parse(Base + ""","maintenance":{"message":"back at 6","allowed":false}}""")!;
        Assert.Equal("back at 6", m.Maintenance!.Message);
        Assert.False(m.Maintenance.Allowed);
        Assert.Null(Manifest.Parse(Base + "}")!.Maintenance);
    }

    [Fact]
    public async Task NotWhitelisted_ShowsMaintenance()
    {
        if (GameProcess.IsCribRunning()) return;   // IN GAME outranks everything; can't be asserted on a box running the game
        _server.ManifestJson = Base + ""","maintenance":{"message":"back at 6","allowed":false}}""";
        var s = await Session().RefreshAsync();
        Assert.Equal(LauncherAction.Maintenance, s.Action);
        Assert.Equal("back at 6", s.Detail);
    }

    [Fact]
    public async Task Whitelisted_OrNoMaintenance_IsTheUsualState()
    {
        if (GameProcess.IsCribRunning()) return;
        _server.ManifestJson = Base + ""","maintenance":{"message":"","allowed":true}}""";
        Assert.NotEqual(LauncherAction.Maintenance, (await Session().RefreshAsync()).Action);
        _server.ManifestJson = Base + "}";
        Assert.NotEqual(LauncherAction.Maintenance, (await Session().RefreshAsync()).Action);
    }

    [Fact]
    public async Task ACachedManifestNeverLocksTheButton()
    {
        if (GameProcess.IsCribRunning()) return;
        _server.ManifestJson = Base + ""","maintenance":{"message":"","allowed":false}}""";
        var session = Session();
        Assert.Equal(LauncherAction.Maintenance, (await session.RefreshAsync()).Action);
        _server.ServeHtml = true;                    // the server stops answering with a manifest → the cache is used
        Assert.NotEqual(LauncherAction.Maintenance, (await session.RefreshAsync()).Action);
    }
}
