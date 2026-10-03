using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Media.Imaging;
using DieReanimated.Launcher.Patching;

namespace DieReanimated.Launcher.Ui.ViewModels;

public enum Tab { Home, News, Servers, Settings }

/// <summary>
/// Everything the window shows, derived from <see cref="LauncherSession"/> (the state machine), the server
/// status, and Steam's local files. The window binds to this and calls the few verbs; it decides nothing itself.
/// </summary>
public sealed class LauncherViewModel : ObservableObject
{
    private readonly LauncherSession _session;
    private readonly StatusClient _status;
    private readonly Settings _settings;
    private readonly LauncherPaths _paths;
    private readonly Log _log;
    private readonly StartOptions _opts;
    private readonly ContentStore _content;
    private readonly Art _art;
    private CancellationTokenSource? _poll;

    /// <summary>Raised when the window should close: the game has started, or a newer launcher took over.</summary>
    public event Action? RequestClose;

    public LauncherViewModel(LauncherSession session, StatusClient status, Settings settings, LauncherPaths paths, Log log, StartOptions opts,
        ContentStore content, Art art)
    {
        _session = session; _status = status; _settings = settings; _paths = paths; _log = log; _opts = opts; _content = content; _art = art;
        ReadIdentity();
        ReadWindowMode();
        _ = LoadArtAsync();
        if (opts.JustUpdated) _log.Line("update", $"running the updated launcher {Defaults.VersionText}");
        if (Enum.TryParse<Tab>(opts.Tab, ignoreCase: true, out var tab)) Tab = tab;
    }

    // ── tabs ──────────────────────────────────────────────────────────────────────────────────────────

    private Tab _tab = Tab.Home;
    public Tab Tab
    {
        get => _tab;
        set
        {
            if (!Set(ref _tab, value)) return;
            Raise(nameof(IsHome)); Raise(nameof(IsNews)); Raise(nameof(IsServers)); Raise(nameof(IsSettings));
            Raise(nameof(Hint));
            RestartPolling();
        }
    }
    public bool IsHome => _tab == Tab.Home;
    public bool IsNews => _tab == Tab.News;
    public bool IsServers => _tab == Tab.Servers;
    public bool IsSettings => _tab == Tab.Settings;

    /// <summary>The quiet line bottom-left, per screen.</summary>
    public string Hint => _tab switch
    {
        Tab.News => "ESC BACK TO HOME",
        Tab.Servers => "PLACEMENT IS DECIDED BY MATCHMAKING",
        Tab.Settings => "CHANGES APPLY ON THE NEXT PLAY",
        _ => "",
    };

    // ── identity (Steam's local files; the game does the real login) ──────────────────────────────

    private string _persona = "—";
    public string Persona { get => _persona; private set => Set(ref _persona, value); }
    private string _steamLine = "";
    public string SteamLine { get => _steamLine; private set => Set(ref _steamLine, value); }
    private Bitmap? _avatar;
    public Bitmap? Avatar { get => _avatar; private set => Set(ref _avatar, value); }
    private bool _steamRunning;
    public bool SteamRunning { get => _steamRunning; private set => Set(ref _steamRunning, value); }

    private void ReadIdentity()
    {
        var (persona, sid, avatarPath) = GameInstall.SteamPersona();
        SteamRunning = GameInstall.IsSteamRunning();
        Persona = persona ?? "No Steam account";
        SteamLine = !SteamRunning ? "STEAM IS NOT RUNNING" : sid != 0 ? $"STEAM · {sid}" : "STEAM";
        try { Avatar = avatarPath != null ? new Bitmap(avatarPath) : null; } catch { Avatar = null; }
    }

    // ── art (the game's own, read from the install) ─────────────────────────────────────────────────

    private Bitmap? _backdrop;
    /// <summary>The survivor of the day, behind everything. <c>null</c> until decoded, or when the install has none.</summary>
    public Bitmap? Backdrop { get => _backdrop; private set { if (Set(ref _backdrop, value)) Raise(nameof(HasBackdrop)); } }
    public bool HasBackdrop => _backdrop != null;
    private Bitmap? _liveNowArt;
    public Bitmap? LiveNowArt { get => _liveNowArt; private set => Set(ref _liveNowArt, value); }

    private async Task LoadArtAsync()
    {
        Backdrop = await _art.GameAsync(Art.SurvivorOfTheDay());
        LiveNowArt = await _art.GameAsync(Art.ModeArt("Scavenger"));
    }

    // ── server status ────────────────────────────────────────────────────────────────────────────────

    private ServerStatus? _serverStatus;
    public ServerStatus? ServerStatus
    {
        get => _serverStatus;
        private set
        {
            _serverStatus = value;
            Raise(); Raise(nameof(ServerReachable)); Raise(nameof(OnlineText)); Raise(nameof(CribUp)); Raise(nameof(CribStateText)); Raise(nameof(MmUp));
            Raise(nameof(CribDetail)); Raise(nameof(MmDetail)); Raise(nameof(InPlayText)); Raise(nameof(InPlayDetail));
            Raise(nameof(LiveNowText)); Raise(nameof(LiveNowRegions)); Raise(nameof(NoLiveText)); Raise(nameof(DiscordButton));
            Regions.Clear();
            foreach (var r in value?.Regions ?? new()) Regions.Add(new RegionVm(r));
            Live.Clear();
            foreach (var m in value?.Live ?? new()) { var vm = new LiveVm(m); Live.Add(vm); _ = LoadLiveArtAsync(vm); }
            Raise(nameof(HasLive)); Raise(nameof(HasRegions));
            RaiseTicks();
        }
    }
    public bool ServerReachable => _serverStatus?.Crib == true;
    public string OnlineText => _serverStatus is null ? "OFFLINE" : !_serverStatus.Crib ? "OFFLINE"
        : _serverStatus.Maintenance ? "MAINTENANCE" : $"ONLINE · {_serverStatus.Online}";
    public bool CribUp => _serverStatus?.Crib == true;
    /// <summary>The crib card's word: MAINTENANCE while the server is up but closed to logins.</summary>
    public string CribStateText => !CribUp ? "OFFLINE" : _serverStatus!.Maintenance ? "MAINTENANCE" : "ONLINE";
    public bool MmUp => _serverStatus?.Matchmaking == true;
    public string CribDetail => _serverStatus is null ? "unreachable"
        : _serverStatus.Maintenance ? $"logins closed · {_serverStatus.Online} signed in" : $"{_serverStatus.Online} signed in";
    public string MmDetail => _serverStatus is null ? "unreachable" : _serverStatus.Matchmaking ? "accepting queues" : "down";
    public string InPlayText => (_serverStatus?.InPlay ?? 0).ToString();
    public string InPlayDetail => Plural(_serverStatus?.Matches ?? 0, "match", "matches");
    public string LiveNowText => _serverStatus is null ? "Server unreachable"
        : _serverStatus.Matches == 0 ? "No matches right now" : $"{Plural(_serverStatus.Matches, "match", "matches")} · {_serverStatus.InPlay} in play";
    public string LiveNowRegions => string.Join(" · ", (_serverStatus?.Regions ?? new()).Where(r => r.Online).Select(r => ShortRegion(r.Code)));
    public bool HasLive => Live.Count > 0;
    public bool HasRegions => Regions.Count > 0;
    public string NoLiveText => _serverStatus is null ? "The server could not be reached." : "Nothing is being played right now. Queue up and be the first.";
    public ObservableCollection<RegionVm> Regions { get; } = new();
    public ObservableCollection<LiveVm> Live { get; } = new();

    private async Task LoadLiveArtAsync(LiveVm vm) => vm.Art = await _art.GameAsync(Art.ModeArt(vm.ModeName));

    public async Task PollStatusAsync(CancellationToken ct = default)
    {
        var s = await _status.FetchAsync(_settings.ServerUrl, ct);
        if (ct.IsCancellationRequested) return;
        ServerStatus = s;
        SteamRunning = GameInstall.IsSteamRunning();
        if (SteamRunning && SteamLine == "STEAM IS NOT RUNNING") ReadIdentity();
    }

    /// <summary>Servers polls every 15 s while open; the other screens leave the server alone.</summary>
    private void RestartPolling()
    {
        _poll?.Cancel();
        if (_tab != Tab.Servers) return;
        var cts = _poll = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(15), cts.Token); } catch (OperationCanceledException) { return; }
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => PollStatusAsync(cts.Token));
            }
        });
    }

    // ── launcher state ───────────────────────────────────────────────────────────────────────────────

    private LauncherState? _state;
    public LauncherState? State { get => _state; private set { _state = value; RaiseState(); } }
    private bool _busy;
    public bool Busy { get => _busy; private set { if (Set(ref _busy, value)) { Raise(nameof(PrimaryEnabled)); Raise(nameof(PrimaryText)); Raise(nameof(ShowPatchWrites)); Raise(nameof(CanRestore)); Raise(nameof(CanReapply)); } } }
    private string _busyText = "";
    public string BusyText { get => _busyText; private set { if (Set(ref _busyText, value)) Raise(nameof(PrimaryText)); } }
    private string _detail = "";
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    private double _progress;
    /// <summary>0–1 while a download runs; the bar under the button.</summary>
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    private bool _hasProgress;
    public bool HasProgress { get => _hasProgress; private set => Set(ref _hasProgress, value); }
    private string _progressText = "";
    /// <summary>"12.3 / 45.0 MB" beside the bar.</summary>
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    private bool _detailIsError;
    public bool DetailIsError { get => _detailIsError; private set => Set(ref _detailIsError, value); }
    private DateTime _checkedAt;

    public LauncherAction Action => _state?.Action ?? LauncherAction.NotInGameFolder;
    public string PrimaryText => Busy ? BusyText : Action switch
    {
        LauncherAction.Play => "PLAY",
        LauncherAction.Patch => "PATCH",
        LauncherAction.Update => "UPDATE",
        LauncherAction.Restart => "RESTART",
        LauncherAction.InGame => "IN GAME",
        LauncherAction.Maintenance => "MAINTENANCE",
        _ => "PLAY",
    };
    public bool PrimaryEnabled => !Busy && _state != null && Action is LauncherAction.Play or LauncherAction.Patch or LauncherAction.Update or LauncherAction.Restart;
    public bool ShowPatchWrites => Action == LauncherAction.Patch && !Busy;
    public IReadOnlyList<string> PatchWrites => LauncherSession.PatchWrites;

    // the three ticks
    public bool PatchOk => _state?.Patch?.State == PatchState.Patched && !(_state?.PatchOutdated ?? false);
    public string PatchTick => _state is null ? "CHECKING" : _state.Patch?.State switch
    {
        PatchState.Patched when _state.PatchOutdated => "PATCH OUTDATED",
        PatchState.Patched => "CLIENT PATCHED",
        PatchState.PatchedPayloadMissing => "PATCH INCOMPLETE",
        PatchState.AssemblyMissing => "CLIENT NOT FOUND",
        _ => "PATCH NEEDED",
    };
    public bool UpdateOk => _state != null && !_state.LauncherOutdated && !_state.LauncherBelowMin && !_state.PatchOutdated;
    public string UpdateTick => _state is null ? "CHECKING" : _state.LauncherBelowMin ? "UPDATE REQUIRED" : _state.LauncherOutdated || _state.PatchOutdated ? "UPDATE AVAILABLE" : "UP TO DATE";
    public bool ServerOk => _state?.ManifestSource is ManifestClient.Source.Network or ManifestClient.Source.NotModified;
    public string ServerTick => _state is null ? "CHECKING" : _state.ManifestSource switch
    {
        ManifestClient.Source.Network or ManifestClient.Source.NotModified => "SERVER REACHED",
        ManifestClient.Source.Cached => "OFFLINE · CACHED",
        _ => "NO SERVER",
    };

    // footer
    public string FooterLeft
    {
        get
        {
            string at = _checkedAt == default ? "" : $" · checked {_checkedAt:HH:mm}";
            return _state?.ManifestSource switch
            {
                ManifestClient.Source.Network or ManifestClient.Source.NotModified => (UpdateOk ? "Up to date" : "Update available") + at,
                ManifestClient.Source.Cached => "Server unreachable · using the last manifest" + at,
                _ => "Server unreachable · no manifest yet" + at,
            };
        }
    }
    public string VersionText => "v" + Defaults.VersionAndChannel;
    public string ServerName => _state?.Manifest?.Server.Name ?? "Reanimated";

    private void RaiseState()
    {
        Raise(nameof(State)); Raise(nameof(Action)); Raise(nameof(PrimaryText)); Raise(nameof(PrimaryEnabled));
        Raise(nameof(ShowPatchWrites)); Raise(nameof(FooterLeft)); Raise(nameof(ServerName)); Raise(nameof(DiscordUrl)); Raise(nameof(HasDiscord));
        Raise(nameof(InstallDir)); Raise(nameof(InstallDetail)); Raise(nameof(PatchLine)); Raise(nameof(PatchDetail)); Raise(nameof(CanRestore)); Raise(nameof(CanReapply));
        RaiseTicks();
        RebuildNews();
    }
    private void RaiseTicks()
    {
        Raise(nameof(PatchOk)); Raise(nameof(PatchTick)); Raise(nameof(UpdateOk)); Raise(nameof(UpdateTick));
        Raise(nameof(ServerOk)); Raise(nameof(ServerTick));
    }

    /// <summary>The start sequence: manifest → where am I → patch state → state; then the status, off the critical path.</summary>
    public async Task RefreshAsync()
    {
        Busy = true; BusyText = "CHECKING";
        DetailIsError = false; Detail = "";
        try
        {
            ApplyState(await Task.Run(() => _session.RefreshAsync()));
        }
        catch (Exception e)
        {
            Fail("refresh", e);
        }
        finally { Busy = false; }
        await PollStatusAsync();
    }

    /// <summary>Take a freshly computed state (from a refresh, or the one an action left behind).</summary>
    private void ApplyState(LauncherState s)
    {
        _checkedAt = DateTime.Now;
        State = s;
        Detail = s.Action switch
        {
            LauncherAction.Play => $"PLAYING AS {Persona.ToUpperInvariant()} · {ServerName.ToUpperInvariant()}",
            LauncherAction.Patch => "ONE CLICK · THE FILES BELOW ARE WRITTEN",
            LauncherAction.Update => s.Detail.ToUpperInvariant(),
            LauncherAction.Restart => $"LAUNCHER {_session.StagedVersion} DOWNLOADED · RESTART TO FINISH",
            LauncherAction.InGame => "THE GAME IS RUNNING",
            LauncherAction.Maintenance => s.Detail.Length > 0 ? s.Detail.ToUpperInvariant() : "THE SERVER IS DOWN FOR MAINTENANCE · CHECK BACK SOON",
            _ => s.Detail,
        };
        if (!SteamRunning && s.Action == LauncherAction.Play) Detail = "START STEAM, THEN PLAY";
        if (s.Action == LauncherAction.Maintenance) WatchMaintenance();
    }

    /// <summary>While MAINTENANCE shows, ask the server again every 30 s, so the button turns back into PLAY (or
    /// UPDATE) by itself when the maintenance ends — nobody has to restart the launcher.</summary>
    private CancellationTokenSource? _maintWatch;
    private void WatchMaintenance()
    {
        if (_maintWatch is { IsCancellationRequested: false }) return;
        var cts = _maintWatch = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(30), cts.Token); } catch (OperationCanceledException) { return; }
                LauncherState s;
                try { s = await _session.RefreshAsync(cts.Token); } catch { continue; }
                if (s.Action == LauncherAction.Maintenance) continue;
                cts.Cancel();
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => { if (!Busy) { ApplyState(s); await PollStatusAsync(); } });
            }
        });
    }

    /// <summary>The one primary button. What it does is the state's decision, not the screen's.</summary>
    public async Task PrimaryAsync()
    {
        if (!PrimaryEnabled) return;
        switch (Action)
        {
            case LauncherAction.Play: await PlayAsync(); break;
            case LauncherAction.Patch: await PatchAsync(); break;
            case LauncherAction.Update: await UpdateAsync(); break;
            case LauncherAction.Restart: Restart(); break;
        }
    }

    private async Task PlayAsync()
    {
        Busy = true; BusyText = "STARTING"; DetailIsError = false;
        try
        {
            // Ask once more right before starting: maintenance (or an update) may have begun since the screen was drawn.
            var fresh = await Task.Run(() => _session.RefreshAsync());
            if (fresh.Action != LauncherAction.Play)
            {
                ApplyState(fresh); Busy = false;
                await PollStatusAsync();   // the header's ONLINE/MAINTENANCE too, not only the button
                return;
            }
            await Task.Run(() => _session.PlayAsync(_opts.GameArgs));
            Detail = "CRIB RUNNING · LAUNCHER CLOSES";
            await Task.Delay(1200);
            RequestClose?.Invoke();
        }
        catch (Exception e) { Fail("launch", e); Busy = false; }
    }

    private async Task PatchAsync()
    {
        Busy = true; BusyText = "PATCHING"; DetailIsError = false;
        try
        {
            await Task.Run(() => _session.PatchAsync(default, progress: null));   // refreshes the session state itself
            ApplyState(_session.State!);
        }
        catch (Exception e) { Fail("patch", e); }
        finally { Busy = false; }
    }

    private async Task UpdateAsync()
    {
        Busy = true; BusyText = "UPDATING"; DetailIsError = false;
        var progress = new Progress<Releases.Progress>(p =>
        {
            HasProgress = true;
            Progress = p.Fraction ?? 0;
            ProgressText = p.Total > 0 ? $"{p.Done / 1048576.0:0.0} / {p.Total / 1048576.0:0.0} MB" : $"{p.Done / 1048576.0:0.0} MB";
            Detail = "DOWNLOADING · " + ProgressText;
        });
        try
        {
            Detail = "DOWNLOADING";
            ApplyState(await Task.Run(() => _session.UpdateAsync(progress)));
        }
        catch (Exception e) { Fail("update", e); }
        finally { Busy = false; HasProgress = false; Progress = 0; }
    }

    /// <summary>The RESTART button: the staged launcher is swapped in and started; this instance closes.</summary>
    private void Restart()
    {
        Busy = true; BusyText = "RESTARTING"; DetailIsError = false;
        try
        {
            _session.Restart(_opts.RestartArgs().Concat(new[] { "--updated" }));
            RequestClose?.Invoke();
        }
        catch (Exception e) { Fail("restart", e); Busy = false; }
    }

    private void Fail(string what, Exception e)
    {
        _log.Line(what, "FAILED: " + e.Message);
        Detail = e.Message.ToUpperInvariant();
        DetailIsError = true;
    }

    // ── news ─────────────────────────────────────────────────────────────────────────────────────────

    public ObservableCollection<NewsVm> News { get; } = new();
    private NewsVm? _headline;
    public NewsVm? Headline { get => _headline; private set { Set(ref _headline, value); Raise(nameof(HasHeadline)); } }
    public bool HasHeadline => _headline != null;
    public ObservableCollection<NewsVm> Tiles { get; } = new();
    private NewsVm? _selectedNews;
    public NewsVm? SelectedNews
    {
        get => _selectedNews;
        set
        {
            var old = _selectedNews;
            if (!Set(ref _selectedNews, value)) return;
            if (old != null) old.IsSelected = false;
            if (value != null) { value.IsSelected = true; _ = LoadBodyAsync(value); }
            Raise(nameof(HasSelectedNews)); Raise(nameof(CanNewer)); Raise(nameof(CanOlder));
        }
    }
    public bool HasSelectedNews => _selectedNews != null;
    public bool HasNews => News.Count > 0;
    public bool CanNewer => _selectedNews != null && News.IndexOf(_selectedNews) > 0;
    public bool CanOlder => _selectedNews != null && News.IndexOf(_selectedNews) < News.Count - 1;
    public void Newer() { if (CanNewer) SelectedNews = News[News.IndexOf(_selectedNews!) - 1]; }
    public void Older() { if (CanOlder) SelectedNews = News[News.IndexOf(_selectedNews!) + 1]; }
    public void OpenNews(NewsVm item) { SelectedNews = item; Tab = Tab.News; }

    private void RebuildNews()
    {
        string? keep = _selectedNews?.Item.Id;
        News.Clear(); Tiles.Clear();
        foreach (var n in _state?.Manifest?.News ?? new()) { var vm = new NewsVm(n); News.Add(vm); _ = LoadImageAsync(vm); }
        Headline = News.FirstOrDefault();
        foreach (var t in News.Skip(1).Take(2)) Tiles.Add(t);
        SelectedNews = News.FirstOrDefault(n => n.Item.Id == keep) ?? News.FirstOrDefault();
        Raise(nameof(HasNews));
    }

    /// <summary>A news image: from the content store (fetched once ever), decoded off the UI thread.</summary>
    private async Task LoadImageAsync(NewsVm vm)
    {
        if (ContentStore.IsHash(vm.Item.Image))
        {
            string? path = await _content.GetAsync(_settings.ServerUrl, vm.Item.Image!);
            if (path != null) { vm.Image = await _art.FileAsync(path); return; }
        }
        if (!string.IsNullOrEmpty(vm.Item.Art)) vm.Image = await _art.GameAsync(vm.Item.Art);
    }

    /// <summary>The reader's text: the markdown body when the item names one, else the summary.</summary>
    private async Task LoadBodyAsync(NewsVm vm)
    {
        if (vm.Body != null) return;
        string? text = ContentStore.IsHash(vm.Item.Body) ? await _content.ReadTextAsync(_settings.ServerUrl, vm.Item.Body!) : null;
        vm.Body = Markdown.Parse(text ?? vm.Summary);
    }

    // ── settings ─────────────────────────────────────────────────────────────────────────────────────

    public string InstallDir => _state?.Install?.Dir ?? _opts.GameDir ?? AppContext.BaseDirectory;
    public string InstallDetail => _state?.Install != null
        ? (_opts.GameDir != null ? "from --game" : "this launcher's own folder") + $" · {Defaults.CribExe} present"
        : _state?.Detail ?? "";

    private GameProcess.WindowMode _windowMode = GameProcess.WindowMode.Borderless;
    public GameProcess.WindowMode WindowMode
    {
        get => _windowMode;
        private set { if (Set(ref _windowMode, value)) { Raise(nameof(IsBorderless)); Raise(nameof(IsWindowed)); } }
    }
    public bool IsBorderless => _windowMode == GameProcess.WindowMode.Borderless;
    public bool IsWindowed => _windowMode == GameProcess.WindowMode.Windowed;
    private void ReadWindowMode() { try { WindowMode = GameProcess.Plan().Mode; } catch { } }
    public void SetWindowMode(GameProcess.WindowMode mode)
    {
        try { GameProcess.SetWindowMode(mode); WindowMode = mode; _log.Line("settings", $"display mode → {mode}"); }
        catch (Exception e) { Fail("settings", e); }
    }

    public string PatchLine => _state?.Patch?.State switch
    {
        PatchState.Patched when _state.PatchOutdated => "Applied · update available",
        PatchState.Patched => "Applied · up to date",
        PatchState.PatchedPayloadMissing => "Incomplete · DieAuth.dll missing",
        PatchState.AssemblyMissing => "Client not found",
        null => "Checking",
        _ => "Not applied",
    };
    public string PatchDetail
    {
        get
        {
            if (_state?.Patch is null) return "";
            string have = _state.Patch.PayloadVersion is { } v ? $"DieAuth {v}" : "no payload";
            string want = _state.Manifest is { } m ? $"manifest {m.Patch.Version}" : "manifest unavailable";
            return $"{have} · {want} · checked on every start";
        }
    }
    public bool CanRestore => !Busy && _state?.Patch?.BackupPresent == true && Action != LauncherAction.InGame;
    public bool CanReapply => !Busy && _state?.Install != null && Action != LauncherAction.InGame;
    public string LogPath => _paths.LogFile;
    public string ServerUrl => _settings.ServerUrl;

    public async Task ReapplyAsync() { if (CanReapply) await PatchAsync(); }

    public async Task RestoreAsync()
    {
        if (Busy || _state?.Install is null) return;
        Busy = true; BusyText = "RESTORING"; DetailIsError = false;
        try
        {
            string dir = _state.Install.Dir;
            await Task.Run(() => ClientPatcher.Restore(dir, log: l => _log.Line("restore", l)));
            await RefreshAsync();
        }
        catch (Exception e) { Fail("restore", e); }
        finally { Busy = false; }
    }

    public void OpenLog() => OpenExternal(_paths.LogFile);
    public void OpenSource() => OpenExternal("https://github.com/Sawbones-Games/die-reanimated-launcher");
    /// <summary>The server's Discord invite, when the manifest names one.</summary>
    public string? DiscordUrl => _state?.Manifest?.Link.Discord is { } u && Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme == "https" ? u : null;
    public bool HasDiscord => DiscordUrl != null;
    /// <summary>The Home action-row button: the invite with its live count when the server knows it.</summary>
    public string DiscordButton => _serverStatus?.Discord is { } d ? $"JOIN THE DISCORD · {d.Online} ONLINE" : "JOIN THE DISCORD";
    public void OpenDiscord() { if (DiscordUrl is { } u) OpenExternal(u); }
    private void OpenExternal(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception e) { _log.Line("open", $"{target}: {e.Message}"); }
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────────

    private static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
    private static string ShortRegion(string code) => code.ToLowerInvariant() switch { "eu" => "EU", "na" => "NA", "ap" or "apac" or "asia" => "AP", _ => code.ToUpperInvariant() };

    public sealed class RegionVm
    {
        public RegionVm(ServerStatus.Region r)
        {
            Name = ServerStatus.RegionName(r.Code); Online = r.Online;
            MatchesText = r.Online ? r.Matches.ToString() : "—"; PlayersText = r.Online ? r.Players.ToString() : "—";
            StatusText = r.Online ? "ONLINE" : "OFFLINE";
        }
        public string Name { get; }
        public bool Online { get; }
        public string MatchesText { get; }
        public string PlayersText { get; }
        public string StatusText { get; }
    }

    public sealed class LiveVm : ObservableObject
    {
        public LiveVm(ServerStatus.LiveMatch m)
        {
            ModeName = m.Mode;
            Mode = m.Mode.ToLowerInvariant() switch
            {
                "" => "MATCH", "scoutmission" => "SCOUT MISSION", "scoutlab" => "THE LAB", "scoutclub" => "PURPLE MOON CLUB",
                "scoutoutpost" => "CAMP BY NIGHT", _ => m.Mode.ToUpperInvariant(),
            };
            Detail = $"{ServerStatus.RegionName(m.Region).ToUpperInvariant()} · {Plural(m.Players, "PLAYER", "PLAYERS")}";
        }
        public string ModeName { get; }
        public string Mode { get; }
        public string Detail { get; }
        private Bitmap? _art;
        public Bitmap? Art { get => _art; set => Set(ref _art, value); }
    }

    public sealed class NewsVm : ObservableObject
    {
        public NewsVm(Manifest.NewsItem item)
        {
            Item = item;
            var (fx, fy) = item.Focus();
            Focus = new Avalonia.Point(fx, fy);
            Kind = item.Kind.ToLowerInvariant() switch
            {
                "event" => "EVENT", "roster" => "ROSTER", "community" => "COMMUNITY", _ => "PATCH NOTES",
            };
            KindIsRed = Kind == "PATCH NOTES";
            KindIsAmber = Kind == "EVENT";
            DateShort = (item.Kind.Equals("event", StringComparison.OrdinalIgnoreCase) && item.EventDate != null
                ? FormatDate(item.EventDate, "ddd d MMM") : FormatDate(item.Date, "d MMM")).ToUpperInvariant();
            DateLong = FormatDate(item.Date, "d MMM yyyy").ToUpperInvariant();
            Kicker = $"{Kind} · {DateShort}";
            ReaderKicker = $"{Kind} · {DateLong}";
        }
        public Manifest.NewsItem Item { get; }
        public string Title => Item.Title;
        public string Summary => Item.Summary;
        public string Kind { get; }
        public bool KindIsRed { get; }
        public bool KindIsAmber { get; }
        public string DateShort { get; }
        public string DateLong { get; }
        public string Kicker { get; }
        public string ReaderKicker { get; }
        private bool _selected;
        public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
        private Bitmap? _image;
        public Bitmap? Image { get => _image; set { if (Set(ref _image, value)) Raise(nameof(HasImage)); } }
        public bool HasImage => _image != null;
        /// <summary>Where the picture's interest is (0–1), for the crop.</summary>
        public Avalonia.Point Focus { get; }
        private IReadOnlyList<Markdown.Block>? _body;
        public IReadOnlyList<Markdown.Block>? Body { get => _body; set => Set(ref _body, value); }

        private static string FormatDate(string iso, string fmt) =>
            DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d.ToString(fmt, CultureInfo.InvariantCulture) : iso;
    }
}
