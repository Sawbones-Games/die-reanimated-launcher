namespace DieReanimated.Launcher.Ui;

/// <summary>Command-line options. <c>--game</c> and <c>--server</c> exist for development (running the launcher
/// from outside the install, against a local server); players never pass anything. Everything unrecognised is
/// forwarded to the game.</summary>
public sealed class StartOptions
{
    public string? GameDir { get; private set; }
    public string? ServerUrl { get; private set; }
    /// <summary>Set by <c>SelfUpdate.Apply</c> on the freshly swapped-in binary.</summary>
    public bool JustUpdated { get; private set; }
    /// <summary>Development: open on this screen (<c>home|news|servers|settings</c>).</summary>
    public string? Tab { get; private set; }
    public IReadOnlyList<string> GameArgs => _gameArgs;
    private readonly List<string> _gameArgs = new();

    /// <summary>The arguments to hand to a replacement binary so it restarts the same way.</summary>
    public IEnumerable<string> RestartArgs()
    {
        if (GameDir != null) { yield return "--game"; yield return GameDir; }
        if (ServerUrl != null) { yield return "--server"; yield return ServerUrl; }
        foreach (string a in _gameArgs) yield return a;
    }

    public static StartOptions Parse(string[] args)
    {
        var o = new StartOptions();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--game" && i + 1 < args.Length) o.GameDir = args[++i];
            else if (args[i] == "--server" && i + 1 < args.Length) o.ServerUrl = args[++i];
            else if (args[i] == "--updated") o.JustUpdated = true;
            else if (args[i] == "--tab" && i + 1 < args.Length) o.Tab = args[++i];
            else o._gameArgs.Add(args[i]);
        }
        return o;
    }
}
