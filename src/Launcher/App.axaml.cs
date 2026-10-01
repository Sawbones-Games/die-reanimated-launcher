using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DieReanimated.Launcher.Ui.ViewModels;

namespace DieReanimated.Launcher.Ui;

public sealed class App : Application
{
    private readonly StartOptions _opts;
    public App(StartOptions opts) { _opts = opts; }
    public App() : this(new StartOptions()) { }   // for the XAML previewer

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The same wiring as the CLI (src/Launcher.Cli/Program.cs): paths → settings → log → session.
            var paths = new LauncherPaths();
            var settings = Settings.Load(paths);
            if (_opts.ServerUrl != null) settings.ServerUrl = _opts.ServerUrl;
            var log = new Log(paths);
            var session = new LauncherSession(paths, settings, log, installOverride: _opts.GameDir, releases: new Releases(token: settings.ReleaseToken));
            var content = new ContentStore(paths, log: l => log.Line("content", l));
            var install = _opts.GameDir != null ? GameInstall.At(_opts.GameDir) : GameInstall.FromExeLocation();
            var vm = new LauncherViewModel(session, new StatusClient(), settings, paths, log, _opts, content, new Art(install?.Dir));
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
