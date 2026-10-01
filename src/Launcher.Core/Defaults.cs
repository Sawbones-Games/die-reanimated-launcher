using System.Reflection;

namespace DieReanimated.Launcher;

/// <summary>The few values baked into a build. Everything else comes from the server manifest or the player's
/// settings file. The server URL is a *default*, overridable in settings — it is not a secret and not a constant
/// the code depends on.</summary>
public static class Defaults
{
    /// <summary>Where the manifest and status are fetched from. Public, read-only endpoints; see docs/reference.</summary>
    public const string ServerUrl = "https://crib-die-admin.sawbonesgames.com";

    /// <summary>The GitHub repository whose Releases this build updates from — <b>the repository that built it</b>:
    /// CI stamps <c>owner/name</c> into the assembly (<c>-p:ReleaseRepository=…</c>, see Launcher.Core.csproj and
    /// .github/workflows/release.yml), so a build from the public repository pulls public releases and a build from
    /// the private dev repository pulls dev releases. A local build defaults to the public one. The server never
    /// serves binaries; it only names a version.</summary>
    public static string ReleaseRepository =>
        typeof(Defaults).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "ReleaseRepository")?.Value
        is { Length: > 0 } r ? r : "Sawbones-Games/die-reanimated-launcher";

    /// <summary>Where releases (the launcher binary, the patch payload, SHA256SUMS) are downloaded from.</summary>
    public static string ReleaseBaseUrl => $"https://github.com/{ReleaseRepository}/releases/download";

    /// <summary>"dev" for a build of the private development repository, "public" otherwise. Shown in the window so a
    /// build is never mistaken for the other channel.</summary>
    public static string Channel => ReleaseRepository.EndsWith("-dev", StringComparison.OrdinalIgnoreCase) ? "dev" : "public";

    public const string LauncherAsset = "DIE-Reanimated-Launcher.exe";
    public const string PayloadAsset  = "DieAuth.dll";
    /// <summary>The gamepad half of the client patch; ships from the same release, same version.</summary>
    public const string GamepadAsset  = "DieGamepad.dll";
    public const string SumsAsset     = "SHA256SUMS";

    /// <summary>The game's own executables; their presence is what makes a folder "the install".</summary>
    public const string CribExe     = "Dead Island Epidemic - Crib.exe";
    public const string LauncherExe = "Dead Island Epidemic - Launcher.exe";

    /// <summary>This build's version, from the assembly (stamped by the release tag).</summary>
    public static Version Version =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? typeof(Defaults).Assembly.GetName().Version ?? new Version(0, 0, 0);

    public static string VersionText => Version.ToString(3);
    /// <summary>The version with the channel when it is not the public one: <c>0.1.0 · dev</c>.</summary>
    public static string VersionAndChannel => Channel == "public" ? VersionText : $"{VersionText} · {Channel}";
}
