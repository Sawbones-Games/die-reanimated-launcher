using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DieReanimated.Launcher.Ui;

/// <summary>
/// Bitmaps for the window: the game's key art (decoded from the install by <see cref="GameArt"/>) and news
/// images (files from the <see cref="ContentStore"/>). Everything is decoded once per run, off the UI thread,
/// and kept; a missing or undecodable image is simply <c>null</c> and the screen shows its gradient instead.
/// </summary>
public sealed class Art
{
    private readonly string? _gameDir;
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _cache = new();
    private GameArt.Catalogue? _index;

    public Art(string? gameDir) { _gameDir = gameDir; }

    /// <summary>The survivors' key art, one per day so the Home screen changes but does not flicker between starts.</summary>
    public static readonly string[] Survivors =
    {
        "BergSurvivor_Large", "IsysSurvivor_Large", "AmberSurvivor_Large", "BryceSurvivor_Large",
        "SamB_Large", "Fuse_Large", "MrWhite_Large", "Septian_Large",
    };
    public static string SurvivorOfTheDay() => Survivors[DateTime.Now.DayOfYear % Survivors.Length];

    /// <summary>The mode card the game itself uses for a match's mode name (as the status reports it).</summary>
    public static string ModeArt(string mode) => mode.ToLowerInvariant() switch
    {
        "scavenger" => "GameMode_Big_Scavenger",
        "scoutmission" or "scoutlab" => "GameMode_Big_Scout_Lab",
        "scoutclub" => "GameMode_Big_Scout_Club",
        "scoutoutpost" => "GameMode_Big_Scout_Outpost",
        _ => "Welcome_Loading",
    };

    /// <summary>Art from the install: a texture name, or <c>Atlas#Sprite</c> (see <see cref="GameArt"/>).</summary>
    public Task<Bitmap?> GameAsync(string spec) =>
        _cache.GetOrAdd("game:" + spec, _ => Task.Run(() =>
        {
            if (_gameDir is null) return null;
            _index ??= GameArt.Index(_gameDir);
            var t = GameArt.Load(_index, spec);
            return t is null ? null : FromBgra(t.Bgra, t.Width, t.Height);
        }));

    /// <summary>An image file (PNG/JPG/WebP) the content store already holds.</summary>
    public Task<Bitmap?> FileAsync(string path) =>
        _cache.GetOrAdd("file:" + path, _ => Task.Run(() =>
        {
            try { return (Bitmap?)new Bitmap(path); } catch { return null; }
        }));

    private static Bitmap FromBgra(byte[] bgra, int w, int h)
    {
        var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var fb = bmp.Lock();
        int stride = w * 4;
        for (int y = 0; y < h; y++)
            Marshal.Copy(bgra, y * stride, fb.Address + y * fb.RowBytes, stride);
        return bmp;
    }
}
