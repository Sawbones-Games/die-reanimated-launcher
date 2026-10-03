using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DieReanimated.Launcher;

/// <summary>
/// Starts the Crib exactly the way the retail launcher did. That launcher was a headless stub whose whole
/// logic is reproduced here, because the game's window mode depends on it:
///
/// <list type="number">
/// <item>Refuse to start on a primary screen narrower than 1024 px.</item>
/// <item>Read <c>HKCU\Software\Deep Silver\Dead Island: Epidemic</c>: a value starting with
///   <c>Options_CribDisplayMode</c> equal to 1 (borderless; also the game's default when unset) adds
///   <c>-popupwindow</c> — and so does 2 (fullscreen), which the Crib cannot run (see <see cref="WindowMode"/>); if no value starting with <c>Screenmanager</c> exists (the game never saved a
///   resolution) add <c>-screen-width W -screen-height H</c> of the primary screen.</item>
/// <item>Append our own command-line arguments, then start <c>Dead Island Epidemic - Crib.exe</c> from the
///   game folder, so Steam's environment (set on us by the Play button) is inherited and the overlay attaches.</item>
/// </list>
/// </summary>
public static class GameProcess
{
    public const string RegistryKey = @"Software\Deep Silver\Dead Island: Epidemic";

    /// <summary>The Crib display modes the game's own options menu offers: 0 windowed, 1 borderless (default).
    /// <c>PCSettings.WindowMode</c> also has 2 = fullscreen, but the Crib cannot run fullscreen —
    /// <c>CribMain.Update()</c> forces <c>Screen.fullScreen = false</c> every frame, so a fullscreen start drops
    /// to a screen-sized window with its title bar off-screen once the hub loads, and the next alt-tab crashes
    /// the D3D reset. Older launcher builds could write 2; it is launched as borderless (see
    /// <see cref="Normalize"/>).</summary>
    public enum WindowMode { Windowed = 0, Borderless = 1 }

    public sealed record LaunchPlan(string Arguments, WindowMode Mode, bool ResolutionSaved, int ScreenWidth, int ScreenHeight);

    /// <summary>Anything but an explicit windowed (0) is borderless — unset (the game's default) and the
    /// unsupported fullscreen value 2 alike.</summary>
    public static WindowMode Normalize(int? cribDisplayMode) =>
        cribDisplayMode == (int)WindowMode.Windowed ? WindowMode.Windowed : WindowMode.Borderless;

    /// <summary>Pure: what the retail stub would pass, given the registry facts and screen size.</summary>
    public static string BuildArguments(int? cribDisplayMode, bool screenmanagerSaved, int width, int height, IEnumerable<string>? extra = null)
    {
        var args = new List<string>();
        if (Normalize(cribDisplayMode) == WindowMode.Borderless) args.Add("-popupwindow");
        if (!screenmanagerSaved) { args.Add("-screen-width"); args.Add(width.ToString()); args.Add("-screen-height"); args.Add(height.ToString()); }
        if (extra != null) args.AddRange(extra);
        return string.Join(' ', args);
    }

    public static LaunchPlan Plan(IEnumerable<string>? extra = null)
    {
        (int? mode, bool saved) = ReadRegistry();
        (int w, int h) = PrimaryScreen();
        return new LaunchPlan(BuildArguments(mode, saved, w, h, extra), Normalize(mode), saved, w, h);
    }

    public static bool IsCribRunning() =>
        Process.GetProcessesByName("Dead Island Epidemic - Crib").Length > 0;

    /// <summary>Start the Crib. Throws <see cref="InvalidOperationException"/> on the retail resolution check.</summary>
    public static Process Start(GameInstall install, LaunchPlan plan)
    {
        if (plan.ScreenWidth < 1024)
            throw new InvalidOperationException($"Dead Island: Epidemic does not support the resolution {plan.ScreenWidth}x{plan.ScreenHeight}. Please change your primary monitor resolution.");
        var psi = new ProcessStartInfo(install.CribExe, plan.Arguments) { WorkingDirectory = install.Dir, UseShellExecute = false };
        return Process.Start(psi) ?? throw new InvalidOperationException("the game did not start");
    }

    /// <summary>Set the window mode the way the game's own options menu does (Settings → Display mode).</summary>
    public static void SetWindowMode(WindowMode mode)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var key = Registry.CurrentUser.CreateSubKey(RegistryKey);
        // Unity PlayerPrefs stores keys with a hash suffix ("Options_CribDisplayMode_h123456"); update every
        // matching value and, if none exists, write the plain name (the retail stub matches by prefix).
        string[] names = key.GetValueNames().Where(n => n.StartsWith("Options_CribDisplayMode", StringComparison.Ordinal)).ToArray();
        if (names.Length == 0) names = new[] { "Options_CribDisplayMode" };
        foreach (string n in names) key.SetValue(n, (int)mode, RegistryValueKind.DWord);
    }

    private static (int? mode, bool screenmanagerSaved) ReadRegistry()
    {
        if (!OperatingSystem.IsWindows()) return (null, false);
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            if (key is null) return (null, false);
            int? mode = null; bool saved = false;
            foreach (string name in key.GetValueNames())
            {
                if (name.StartsWith("Options_CribDisplayMode", StringComparison.Ordinal) && key.GetValue(name) is int v) mode = v;
                if (name.StartsWith("Screenmanager", StringComparison.Ordinal)) saved = true;
            }
            return (mode, saved);
        }
        catch { return (null, false); }
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    private static (int w, int h) PrimaryScreen()
    {
        if (!OperatingSystem.IsWindows()) return (1920, 1080);
        try { return (GetSystemMetrics(0), GetSystemMetrics(1)); } catch { return (1920, 1080); }
    }
}
