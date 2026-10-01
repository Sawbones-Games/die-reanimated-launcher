namespace DieReanimated.Launcher;

/// <summary>Everything the launcher keeps outside the game folder lives under one directory in
/// <c>%LocalAppData%\DIE Reanimated\</c>: it survives Steam's verify integrity and launcher updates.</summary>
public sealed class LauncherPaths
{
    public string Root { get; }
    public string Cache => Path.Combine(Root, "cache");
    public string Content => Path.Combine(Cache, "content");
    public string Updates => Path.Combine(Root, "updates");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string LogFile => Path.Combine(Root, "launcher.log");
    public string ManifestFile => Path.Combine(Cache, "manifest.json");
    public string ManifestEtagFile => Path.Combine(Cache, "manifest.etag");
    /// <summary>The server URL the cached manifest came from; a different server never reads another's cache.</summary>
    public string ManifestSourceFile => Path.Combine(Cache, "manifest.source");

    public LauncherPaths(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DIE Reanimated");
        Directory.CreateDirectory(Content);
        Directory.CreateDirectory(Updates);
    }
}

/// <summary>Player settings. Deliberately tiny: the launcher is not configurable beyond what a player can act on.</summary>
public sealed class Settings
{
    public string ServerUrl { get; set; } = Defaults.ServerUrl;
    /// <summary>Development channel only: a GitHub token with read access to the private dev repository, so a dev
    /// build can download its releases (a private repository's assets are not public). Never needed by a public
    /// build; never sent anywhere but api.github.com.</summary>
    public string? ReleaseToken { get; set; }

    public static Settings Load(LauncherPaths paths)
    {
        try
        {
            if (File.Exists(paths.SettingsFile))
                return System.Text.Json.JsonSerializer.Deserialize<Settings>(File.ReadAllText(paths.SettingsFile)) ?? new Settings();
        }
        catch { /* a broken settings file is not worth failing over */ }
        return new Settings();
    }

    public void Save(LauncherPaths paths) =>
        File.WriteAllText(paths.SettingsFile, System.Text.Json.JsonSerializer.Serialize(this, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>One append-only log for what the launcher did, in the open. Mirrored to the console by the CLI.</summary>
public sealed class Log
{
    private readonly string _file;
    private readonly Action<string>? _echo;
    public Log(LauncherPaths paths, Action<string>? echo = null) { _file = paths.LogFile; _echo = echo; }

    public void Line(string category, string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss}  {category,-9} {message}";
        _echo?.Invoke(line);
        try { File.AppendAllText(_file, line + Environment.NewLine); } catch { }
    }
}
