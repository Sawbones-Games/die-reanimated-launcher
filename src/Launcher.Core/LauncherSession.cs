using System.Reflection;
using DieReanimated.Launcher.Patching;

namespace DieReanimated.Launcher;

/// <summary>What the primary button does right now.</summary>
public enum LauncherAction
{
    /// <summary>The exe is not in the game folder: show where to put it.</summary>
    NotInGameFolder,
    /// <summary>The launcher or the client patch is behind the version the server names.</summary>
    Update,
    /// <summary>The client is not patched: one click, with the file list shown.</summary>
    Patch,
    /// <summary>Everything is current.</summary>
    Play,
    /// <summary>The Crib is already running.</summary>
    InGame,
    /// <summary>A newer launcher has been downloaded and verified; pressing the button swaps it in and restarts.</summary>
    Restart,
    /// <summary>The server is in maintenance and this account is not whitelisted: nothing to press (the crib would
    /// refuse the login anyway). Takes the place of PLAY, PATCH and UPDATE.</summary>
    Maintenance,
}

public sealed record LauncherState(
    LauncherAction Action,
    string Detail,
    GameInstall? Install,
    Manifest? Manifest,
    ManifestClient.Source ManifestSource,
    PatchStatus? Patch,
    bool LauncherOutdated,
    bool LauncherBelowMin,
    bool PatchOutdated);

/// <summary>
/// The launcher's whole non-UI behaviour, in the order it happens on every start:
/// manifest (cached, ETag) → where am I → patch status → state. Then one of three actions.
/// </summary>
public sealed class LauncherSession
{
    private readonly LauncherPaths _paths;
    private readonly Settings _settings;
    private readonly Log _log;
    private readonly ManifestClient _manifests;
    private readonly Releases _releases;
    private readonly string? _installOverride;
    /// <summary>A newer launcher, downloaded and verified this run, waiting for the player to press RESTART.</summary>
    private (string exe, string version)? _staged;

    public LauncherState? State { get; private set; }
    /// <summary>The version of the launcher staged for RESTART, if any.</summary>
    public string? StagedVersion => _staged?.version;

    public LauncherSession(LauncherPaths paths, Settings settings, Log log, string? installOverride = null,
        ManifestClient? manifests = null, Releases? releases = null)
    {
        _paths = paths; _settings = settings; _log = log; _installOverride = installOverride;
        _manifests = manifests ?? new ManifestClient(paths);
        _releases = releases ?? new Releases();
        SelfUpdate.CleanUp();
    }

    // ── state ────────────────────────────────────────────────────────────────────────────────────────

    public async Task<LauncherState> RefreshAsync(CancellationToken ct = default)
    {
        var fetched = await _manifests.FetchAsync(_settings.ServerUrl, ct, GameInstall.SteamPersona().SteamId);
        _log.Line("manifest", $"{_settings.ServerUrl} → {fetched.Source}: {fetched.Detail}");
        Manifest? m = fetched.Manifest;
        // Only a manifest the server answered NOW can say "maintenance" — a cached one from an earlier outage must not
        // keep the button locked once the server is unreachable (or back).
        bool live = fetched.Source is ManifestClient.Source.Network or ManifestClient.Source.NotModified;
        Manifest.MaintenanceInfo? maintenance = live && m?.Maintenance is { Allowed: false } mt ? mt : null;

        GameInstall? install = _installOverride != null ? GameInstall.At(_installOverride) : GameInstall.FromExeLocation();
        if (install is null)
        {
            string hint = GameInstall.SteamHint() ?? "your Dead Island Epidemic folder";
            return State = new LauncherState(LauncherAction.NotInGameFolder,
                $"Put this file in {hint}, replacing {Defaults.LauncherExe}", null, m, fetched.Source, null, false, false, false);
        }

        PatchStatus patch = ClientPatcher.Inspect(install.Dir);
        bool launcherOutdated = m != null && SelfUpdate.IsNewer(m.Launcher.Version, Defaults.Version);
        bool belowMin = m != null && SelfUpdate.IsOlderThanMin(m.Launcher.Min, Defaults.Version);
        bool patchOutdated = m != null && patch.State == PatchState.Patched
                             && patch.PayloadVersion != null && patch.PayloadVersion != m.Patch.Version;

        LauncherAction action; string detail;
        if (GameProcess.IsCribRunning()) { action = LauncherAction.InGame; detail = "the game is running"; }
        else if (_staged is { } staged && File.Exists(staged.exe) && SelfUpdate.IsNewer(staged.version, Defaults.Version))
            { action = LauncherAction.Restart; detail = $"launcher {staged.version} is ready"; }
        else if (maintenance != null)
            { action = LauncherAction.Maintenance; detail = maintenance.Message; }
        else if (belowMin || launcherOutdated)
            { action = LauncherAction.Update; detail = $"launcher {Defaults.VersionText} → {m!.Launcher.Version}" + (belowMin ? " (required)" : ""); }
        else if (patch.State is PatchState.NotPatched or PatchState.PatchedPayloadMissing)
            { action = LauncherAction.Patch; detail = patch.Detail; }
        else if (patch.State == PatchState.AssemblyMissing)
            { action = LauncherAction.NotInGameFolder; detail = patch.Detail; }
        else if (patchOutdated)
            { action = LauncherAction.Update; detail = $"client patch {patch.PayloadVersion} → {m!.Patch.Version}"; }
        else { action = LauncherAction.Play; detail = $"patch {patch.PayloadVersion} · {(m is null ? "server manifest unavailable" : m.Server.Name)}"; }

        _log.Line("state", $"{action}: {detail}");
        return State = new LauncherState(action, detail, install, m, fetched.Source, patch, launcherOutdated, belowMin, patchOutdated);
    }

    // ── actions ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The files PATCH will write, for the screen. Relative to the game folder.</summary>
    public static IReadOnlyList<string> PatchWrites { get; } = new[]
    {
        @"Dead Island Epidemic - Crib_Data\Managed\DieAuth.dll",
        @"Dead Island Epidemic - Crib_Data\Managed\DieGamepad.dll",
        @"Dead Island Epidemic - Crib_Data\Managed\Assembly-CSharp.dll   (original kept as .bak)",
        @"Dead Island Epidemic_Data\Managed\DieGamepad.dll",
        @"Dead Island Epidemic_Data\Managed\Assembly-CSharp.dll          (original kept as .bak)",
        @"ip.cfg",
    };

    /// <summary>Install the client patch. The payload of this launcher's own release is embedded, so a plain
    /// PATCH needs no network; a manifest naming a different patch version downloads that one first.</summary>
    public async Task<PatchStatus> PatchAsync(CancellationToken ct = default, IProgress<Releases.Progress>? progress = null)
    {
        var s = State ?? await RefreshAsync(ct);
        var install = s.Install ?? throw new InvalidOperationException("not in the game folder");
        if (GameProcess.IsCribRunning()) throw new InvalidOperationException("close the game first");

        string payload = await PayloadForAsync(s.Manifest?.Patch, ct, progress);
        string gamepad = await GamepadPatchPayloadAsync(s.Manifest?.Patch, ct, progress);
        var result = ClientPatcher.Apply(install.Dir, payload, gamepad, log: l => _log.Line("patch", l));
        if (s.Manifest != null) ApplyIpCfg(install, s.Manifest);
        _log.Line("patch", $"{result.State} payload={result.PayloadVersion}");
        await RefreshAsync(ct);
        return result;
    }

    /// <summary>Write ip.cfg from the manifest and start the Crib. Returns once the process has started.</summary>
    public async Task PlayAsync(IEnumerable<string>? extraArgs = null, CancellationToken ct = default)
    {
        var s = State ?? await RefreshAsync(ct);
        var install = s.Install ?? throw new InvalidOperationException("not in the game folder");
        if (s.Manifest != null) ApplyIpCfg(install, s.Manifest);
        else _log.Line("ip.cfg", "no manifest — left as is (" + (IpConfig.CurrentRequestHost(install.IpCfg) ?? "?") + ")");
        var plan = GameProcess.Plan(extraArgs);
        _log.Line("launch", $"\"{Defaults.CribExe}\" {plan.Arguments}   (mode={plan.Mode}, resolution saved={plan.ResolutionSaved})");
        GameProcess.Start(install, plan);
    }

    /// <summary>The UPDATE button — everything the manifest asks for, in one press. A newer <b>launcher</b> is
    /// downloaded and verified, then staged: the state becomes <see cref="LauncherAction.Restart"/> and nothing is
    /// swapped until the player presses it (<see cref="Restart"/>). A newer <b>client patch</b> is applied right
    /// away — with the launcher when both are due (its payload comes from the same release), so the restart lands
    /// on PLAY; alone, the state becomes PLAY with no restart. An unpatched client is not patched here: PATCH is
    /// its own press. Progress is reported per download.</summary>
    public async Task<LauncherState> UpdateAsync(IProgress<Releases.Progress>? progress = null, CancellationToken ct = default)
    {
        var s = State ?? await RefreshAsync(ct);
        var m = s.Manifest ?? throw new InvalidOperationException("no manifest — cannot tell what to update to");

        if (s.LauncherOutdated || s.LauncherBelowMin)
        {
            string tag = Releases.TagFor(m.Launcher.Version);
            _log.Line("update", $"launcher {Defaults.VersionText} → {m.Launcher.Version}: downloading {tag}");
            string exe = await _releases.DownloadVerifiedAsync(tag, Defaults.LauncherAsset, _paths.Updates, l => _log.Line("update", l), ct, progress);
            _staged = (exe, m.Launcher.Version);
            _log.Line("update", $"launcher {m.Launcher.Version} staged — restart to finish");
            if (s.PatchOutdated) { await PatchAsync(ct, progress); return State!; }   // the same press (PatchAsync refreshes); the restart then lands on PLAY
            return await RefreshAsync(ct);
        }

        if (s.PatchOutdated || s.Patch?.State is PatchState.NotPatched or PatchState.PatchedPayloadMissing)
            await PatchAsync(ct, progress);
        return State!;
    }

    /// <summary>The RESTART button: swap the staged launcher in and start it with the same arguments. Returns once
    /// the new instance is running; the caller must exit.</summary>
    public void Restart(IEnumerable<string> restartArgs)
    {
        var staged = _staged ?? throw new InvalidOperationException("no update is staged");
        _log.Line("update", $"swapping in launcher {staged.version} and restarting");
        SelfUpdate.Apply(staged.exe, restartArgs);
    }

    /// <summary>Download + swap + restart in one go — the headless driver's <c>update</c>. Returns <c>true</c> when the
    /// launcher itself was replaced and the caller must exit.</summary>
    public async Task<bool> UpdateAndRestartAsync(IEnumerable<string> restartArgs, CancellationToken ct = default)
    {
        var s = await UpdateAsync(null, ct);
        if (s.Action != LauncherAction.Restart) return false;
        Restart(restartArgs);
        return true;
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    private void ApplyIpCfg(GameInstall install, Manifest m)
    {
        if (string.IsNullOrWhiteSpace(m.Server.RequestHost)) { _log.Line("ip.cfg", "manifest names no server — left as is"); return; }
        IpConfig.Apply(install.IpCfg, m.Server, out string detail);
        _log.Line("ip.cfg", detail);
    }

    /// <summary>The DieAuth.dll to install: the embedded one when its version matches what the manifest wants (or
    /// there is no manifest), else the one from the named release.</summary>
    private async Task<string> PayloadForAsync(Manifest.PatchInfo? want, CancellationToken ct, IProgress<Releases.Progress>? progress = null)
    {
        string embedded = ExtractEmbeddedPayload();
        string embeddedVersion = PayloadVersion(embedded);
        if (want is null || string.IsNullOrEmpty(want.Version) || want.Version == embeddedVersion)
            return embedded;
        string tag = want.Release ?? Releases.TagFor(want.Version);
        _log.Line("patch", $"manifest wants payload {want.Version} (have {embeddedVersion} embedded): downloading from {tag}");
        return await _releases.DownloadVerifiedAsync(tag, Defaults.PayloadAsset, _paths.Updates, l => _log.Line("patch", l), ct, progress);
    }

    /// <summary>The gamepad payload for the same patch version. It ships from the same release and carries
    /// the same version as DieAuth, so the manifest names one version for both and this follows it.</summary>
    private async Task<string> GamepadPatchPayloadAsync(Manifest.PatchInfo? want, CancellationToken ct, IProgress<Releases.Progress>? progress = null)
    {
        string embedded = ExtractEmbedded(Defaults.GamepadAsset);
        string embeddedVersion = PayloadVersion(ExtractEmbedded(Defaults.PayloadAsset));
        if (want is null || string.IsNullOrEmpty(want.Version) || want.Version == embeddedVersion)
            return embedded;
        string tag = want.Release ?? Releases.TagFor(want.Version);
        _log.Line("patch", $"manifest wants payload {want.Version}: downloading {Defaults.GamepadAsset} from {tag}");
        return await _releases.DownloadVerifiedAsync(tag, Defaults.GamepadAsset, _paths.Updates, l => _log.Line("patch", l), ct, progress);
    }

    private string ExtractEmbeddedPayload() => ExtractEmbedded(Defaults.PayloadAsset);

    private string ExtractEmbedded(string assetName)
    {
        var asm = typeof(LauncherSession).Assembly;
        string name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(assetName, StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidOperationException("this build carries no embedded " + assetName);
        string dest = Path.Combine(_paths.Cache, assetName);
        using var src = asm.GetManifestResourceStream(name)!;
        using var dst = File.Create(dest);
        src.CopyTo(dst);
        return dest;
    }

    private static string PayloadVersion(string dll)
    {
        try { return AssemblyName.GetAssemblyName(dll).Version?.ToString(3) ?? "0.0.0"; }
        catch { return "0.0.0"; }
    }
}
