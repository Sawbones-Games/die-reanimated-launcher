using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DieReanimated.Launcher;

/// <summary>The game folder. The launcher is meant to live IN it (it replaces the retail launcher), so the
/// normal answer is "the folder this exe is in". The Steam lookup exists only to tell a player who ran the exe
/// from somewhere else where to put it.</summary>
public sealed class GameInstall
{
    public string Dir { get; }
    public string CribExe => Path.Combine(Dir, Defaults.CribExe);
    public string LauncherExe => Path.Combine(Dir, Defaults.LauncherExe);
    public string IpCfg => Path.Combine(Dir, "ip.cfg");

    private GameInstall(string dir) => Dir = dir;

    /// <summary>True when <paramref name="dir"/> holds the game.</summary>
    public static bool IsGameFolder(string dir) => File.Exists(Path.Combine(dir, Defaults.CribExe));

    public static GameInstall? At(string dir) => IsGameFolder(dir) ? new GameInstall(Path.GetFullPath(dir)) : null;

    /// <summary>The folder the running executable is in, if it is the game folder.</summary>
    public static GameInstall? FromExeLocation() => At(AppContext.BaseDirectory);

    /// <summary>Where Steam says the game is, for the "put me here" hint. Reads Steam's own files only.</summary>
    public static string? SteamHint()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return null;
            string? steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
            if (steam is null) return null;
            var libraries = new List<string> { steam };
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            foreach (string lib in libraries)
            {
                string candidate = Path.Combine(lib, "steamapps", "common", "Dead Island Epidemic");
                if (IsGameFolder(candidate)) return Path.GetFullPath(candidate);
            }
        }
        catch { }
        return null;
    }

    /// <summary>Whether the Steam client is running. The game needs it (the session ticket comes from it); the
    /// launcher only says so — it does not start Steam.</summary>
    public static bool IsSteamRunning() =>
        System.Diagnostics.Process.GetProcessesByName("steam").Length > 0;

    /// <summary>The player's Steam persona and avatar — read from Steam's local files. The account is the one
    /// Steam is signed in as (<c>ActiveProcess\ActiveUser</c>); with Steam closed, the most recent login.</summary>
    public static (string? Persona, ulong SteamId, string? AvatarPath) SteamPersona()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return (null, 0, null);
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string? steam = key?.GetValue("SteamPath") as string;
            if (steam is null) return (null, 0, null);

            // "users" { "7656…" { "AccountName" "" "PersonaName" "" … "MostRecent" "1" / "Timestamp" "…" } … }
            var users = new List<(ulong Sid, string Persona, bool MostRecent, long Stamp)>();
            string vdf = Path.Combine(steam, "config", "loginusers.vdf");
            if (File.Exists(vdf))
                foreach (Match user in Regex.Matches(File.ReadAllText(vdf), @"""(\d{17})""\s*\{([^}]*)\}"))
                {
                    string block = user.Groups[2].Value;
                    string persona = Regex.Match(block, @"""PersonaName""\s+""([^""]*)""").Groups[1].Value;
                    bool recent = Regex.IsMatch(block, @"""MostRecent""\s+""1""");
                    long.TryParse(Regex.Match(block, @"""Timestamp""\s+""(\d+)""").Groups[1].Value, out long stamp);
                    users.Add((ulong.Parse(user.Groups[1].Value), persona, recent, stamp));
                }

            ulong sid = 0;
            using (var active = key!.OpenSubKey("ActiveProcess"))
                if (active?.GetValue("ActiveUser") is int accountId && accountId != 0) sid = 76561197960265728UL + (ulong)accountId;
            if (sid == 0 && users.Count > 0)
                sid = (users.FirstOrDefault(u => u.MostRecent) is { Sid: not 0 } r ? r : users.MaxBy(u => u.Stamp)).Sid;
            if (sid == 0) return (null, 0, null);

            string? name = users.FirstOrDefault(u => u.Sid == sid).Persona;
            string avatar = Path.Combine(steam, "config", "avatarcache", sid + ".png");
            return (string.IsNullOrEmpty(name) ? null : name, sid, File.Exists(avatar) ? avatar : null);
        }
        catch { }
        return (null, 0, null);
    }
}
