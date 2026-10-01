using Avalonia;
using Avalonia.Media;
using DieReanimated.Launcher.Ui;

// DIE-Reanimated-Launcher.exe [--game <dir>] [--server <url>] [--tab <screen>] [--updated] [<args forwarded to the game>]
//
// Steam starts this file from the game folder (it replaced the retail launcher). Any argument the launcher does
// not recognise is passed through to the game, exactly as the retail stub forwarded its own.
static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var opts = StartOptions.Parse(args);
        return BuildAvaloniaApp(opts).StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp(StartOptions? opts = null) =>
        AppBuilder.Configure(() => new App(opts ?? new StartOptions()))
            .UsePlatformDetect()
            .With(new FontManagerOptions { DefaultFamilyName = Fonts.Body })
            .LogToTrace();
}
