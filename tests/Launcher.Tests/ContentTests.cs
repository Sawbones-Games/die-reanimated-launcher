using System.Security.Cryptography;
using DieReanimated.Launcher;
using Xunit;

namespace Launcher.Tests;

public sealed class MarkdownTests
{
    [Fact]
    public void ParsesTheSubset()
    {
        var blocks = Markdown.Parse("## Gameplay\n\nMap 1 is live\nend to end.\n\n- Account XP **rounds** per box\n* second\n\n### Fixes\nplain `code` and *soft*\n");
        Assert.Equal(6, blocks.Count);
        Assert.Equal(Markdown.BlockKind.Heading, blocks[0].Kind); Assert.Equal(2, blocks[0].Level); Assert.Equal("Gameplay", blocks[0].Spans[0].Text);
        Assert.Equal(Markdown.BlockKind.Paragraph, blocks[1].Kind); Assert.Equal("Map 1 is live end to end.", blocks[1].Spans[0].Text);
        Assert.Equal(Markdown.BlockKind.Bullet, blocks[2].Kind);
        Assert.Collection(blocks[2].Spans,
            s => Assert.Equal(("Account XP ", false), (s.Text, s.Bold)),
            s => Assert.Equal(("rounds", true), (s.Text, s.Bold)),
            s => Assert.Equal((" per box", false), (s.Text, s.Bold)));
        Assert.Equal(Markdown.BlockKind.Bullet, blocks[3].Kind);
        Assert.Equal(3, blocks[4].Level);
        Assert.Collection(blocks[5].Spans,
            s => Assert.Equal("plain ", s.Text),
            s => Assert.True(s.Code),
            s => Assert.Equal(" and ", s.Text),
            s => Assert.True(s.Italic));
    }

    [Fact]
    public void UnbalancedMarksAreText()
    {
        var spans = Markdown.Inline("a ** b and c ` d");
        Assert.Single(spans);
        Assert.Equal("a ** b and c ` d", spans[0].Text);
    }
}

public sealed class ContentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "diect_" + Guid.NewGuid().ToString("N"));
    private readonly FakeServer _server = new();
    public void Dispose() { _server.Dispose(); try { Directory.Delete(_root, true); } catch { } }

    private static string HashOf(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    [Fact]
    public async Task FetchesOnceThenServesFromDisk()
    {
        byte[] body = System.Text.Encoding.UTF8.GetBytes("## hello\n\nworld\n");
        string hash = HashOf(body);
        _server.Content[hash] = body;
        var store = new ContentStore(new LauncherPaths(_root));

        Assert.Null(store.Local(hash));
        string? p = await store.GetAsync(_server.Url, hash);
        Assert.NotNull(p);
        Assert.Equal(body, await File.ReadAllBytesAsync(p!));
        int hits = _server.Hits;

        Assert.Equal(p, store.Local(hash));
        Assert.Equal(p, await store.GetAsync(_server.Url, hash));
        Assert.Equal(hits, _server.Hits);                       // never asked again
        Assert.Equal("## hello\n\nworld\n", await store.ReadTextAsync(_server.Url, hash));
    }

    [Fact]
    public async Task BytesThatDoNotMatchTheirNameAreDiscarded()
    {
        byte[] body = System.Text.Encoding.UTF8.GetBytes("genuine");
        string hash = HashOf(body);
        _server.Content[hash] = System.Text.Encoding.UTF8.GetBytes("tampered");
        var store = new ContentStore(new LauncherPaths(_root));
        Assert.Null(await store.GetAsync(_server.Url, hash));
        Assert.Null(store.Local(hash));
        Assert.Empty(Directory.GetFiles(new LauncherPaths(_root).Content));
    }

    [Fact]
    public async Task MissingOrMalformedIsNull()
    {
        var store = new ContentStore(new LauncherPaths(_root));
        Assert.Null(await store.GetAsync(_server.Url, new string('a', 64)));
        Assert.Null(await store.GetAsync(_server.Url, "not-a-hash"));
        Assert.Null(store.Local("../../etc/passwd"));
    }
}

/// <summary>Against a real install, named by <c>DIE_GAME_DIR</c>; without it (CI) these return without checking.</summary>
public sealed class GameArtTests
{
    private static readonly string Game = Environment.GetEnvironmentVariable("DIE_GAME_DIR") ?? "";
    private static bool Installed => Game.Length > 0 && File.Exists(Path.Combine(GameArt.DataDir(Game), "resources.assets"));

    [Fact]
    public void IndexesTheKnownKeyArt()
    {
        if (!Installed) return;
        var index = GameArt.Index(Game).Textures;
        var berg = Assert.Single(index, e => e.Name == "BergSurvivor_Large");
        Assert.Equal((1152, 670, 3), (berg.Width, berg.Height, berg.Format));
        var white = Assert.Single(index, e => e.Name == "MrWhite_Large");
        Assert.Equal((1152, 616, 10), (white.Width, white.Height, white.Format));
        var scav = Assert.Single(index, e => e.Name == "GameMode_Big_Scavenger");
        Assert.Equal((548, 280, 12), (scav.Width, scav.Height, scav.Format));
        Assert.Contains(index, e => e.Name == "DIEpidemicLoadingScreen" && e.Width == 2048);
    }

    [Theory]
    [InlineData("BergSurvivor_Large")]      // RGB24
    [InlineData("MrWhite_Large")]           // DXT1
    [InlineData("GameMode_Big_Scavenger")]  // DXT5
    [InlineData("CribAtlas01")]             // RGBA32, 4096²
    public void DecodesEveryFormatTheGameUses(string name)
    {
        if (!Installed) return;
        var t = GameArt.Load(Game, name);
        Assert.NotNull(t);
        Assert.Equal(t!.Width * t.Height * 4, t.Bgra.Length);
        // real art is not a flat colour: the pixels vary, and alpha is opaque somewhere
        var distinct = new HashSet<uint>();
        bool opaque = false;
        for (int i = 0; i < t.Bgra.Length; i += 4 * 97) { distinct.Add(BitConverter.ToUInt32(t.Bgra, i)); opaque |= t.Bgra[i + 3] == 255; }
        Assert.True(distinct.Count > 50, $"{name}: only {distinct.Count} distinct colours");
        Assert.True(opaque);
    }

    // Reference pixels from an independent decoder (UnityPy) of the same install: R,G,B at (x,y), top-down.
    [Theory]
    [InlineData("BergSurvivor_Large", 100, 100, 124, 176, 236)]
    [InlineData("BergSurvivor_Large", 1000, 500, 57, 45, 42)]
    [InlineData("MrWhite_Large", 100, 100, 74, 40, 24)]
    [InlineData("MrWhite_Large", 1000, 500, 35, 66, 60)]
    [InlineData("GameMode_Big_Scavenger", 100, 100, 93, 64, 87)]
    [InlineData("GameMode_Big_Scavenger", 400, 200, 148, 60, 57)]
    public void PixelsMatchAnIndependentDecoder(string name, int x, int y, int r, int g, int b)
    {
        if (!Installed) return;
        var t = GameArt.Load(Game, name)!;
        int i = (y * t.Width + x) * 4;
        // DXT endpoints are interpolated with integer maths that differs by ±1 between decoders
        Assert.InRange(t.Bgra[i + 2], r - 2, r + 2);
        Assert.InRange(t.Bgra[i + 1], g - 2, g + 2);
        Assert.InRange(t.Bgra[i], b - 2, b + 2);
    }

    [Fact]
    public void IndexesTheThreeAtlases()
    {
        if (!Installed) return;
        var cat = GameArt.Index(Game);
        Assert.Equal(new[] { "CribAtlas01", "ItemIconAtlas", "ShopCardAtlas" }, cat.Atlases.Select(a => a.Name).OrderBy(n => n));
        var shop = cat.Atlases.Single(a => a.Name == "ShopCardAtlas");
        Assert.Contains(shop.Sprites, s => s.Name == "ShopCard_Berg_Survivor");
        var crib = cat.Atlases.Single(a => a.Name == "CribAtlas01");
        Assert.Contains(crib.Sprites, s => s.Name == "DIE_Logo_512");
    }

    [Theory]
    [InlineData("ShopCardAtlas#ShopCard_Berg_Survivor")]   // DXT5 atlas, a region
    [InlineData("#DIE_Logo_512")]                          // RGBA32 atlas, searched by sprite name
    [InlineData("CribAtlas01#Icon_Weapon_Fists_Big")]
    public void CutsSpritesOutOfTheAtlases(string spec)
    {
        if (!Installed) return;
        var t = GameArt.Load(Game, spec);
        Assert.NotNull(t);
        Assert.InRange(t!.Width, 16, 1024); Assert.InRange(t.Height, 16, 1024);
        var distinct = new HashSet<uint>();
        for (int i = 0; i < t.Bgra.Length; i += 4 * 13) distinct.Add(BitConverter.ToUInt32(t.Bgra, i));
        Assert.True(distinct.Count > 20, $"{spec}: only {distinct.Count} distinct colours");
    }

    [Fact]
    public void ARegionEqualsTheSamePixelsOfTheWhole()
    {
        if (!Installed) return;
        var cat = GameArt.Index(Game);
        var e = cat.Texture("GameMode_Big_Scavenger")!;                 // DXT5, so the block-band path is exercised
        var whole = GameArt.Load(e)!;
        var part = GameArt.LoadRegion(e, 37, 19, 100, 50)!;             // deliberately off the 4-px block grid
        for (int y = 0; y < 50; y++)
            Assert.Equal(whole.Bgra.AsSpan(((19 + y) * e.Width + 37) * 4, 400).ToArray(), part.Bgra.AsSpan(y * 400, 400).ToArray());
        var e2 = cat.Texture("BergSurvivor_Large")!;                    // RGB24, the row path
        var whole2 = GameArt.Load(e2)!;
        var part2 = GameArt.LoadRegion(e2, 500, 300, 64, 8)!;
        Assert.Equal(whole2.Bgra.AsSpan((300 * e2.Width + 500) * 4, 256).ToArray(), part2.Bgra.AsSpan(0, 256).ToArray());
    }

    [Fact]
    public void UnknownNameIsNull()
    {
        if (!Installed) return;
        Assert.Null(GameArt.Load(Game, "NoSuchTexture"));
        Assert.Null(GameArt.Load(Game, "ShopCardAtlas#NoSuchSprite"));
        Assert.Empty(GameArt.Index(Path.GetTempPath()).Textures);
    }
}
