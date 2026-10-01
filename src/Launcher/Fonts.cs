namespace DieReanimated.Launcher.Ui;

/// <summary>The three brand families, embedded under Assets/Fonts (SIL Open Font License; the licence texts sit
/// next to the files). The same families as the project website, so the launcher and the site read as one thing.</summary>
public static class Fonts
{
    private const string Root = "avares://DIE-Reanimated-Launcher/Assets/Fonts";
    /// <summary>Oswald — wordmark, headlines, the PLAY button.</summary>
    public const string Display = Root + "#Oswald";
    /// <summary>Barlow Semi Condensed — kickers, labels, tabs, status lines.</summary>
    public const string Label = Root + "#Barlow Semi Condensed";
    /// <summary>Source Sans 3 — body text.</summary>
    public const string Body = Root + "#Source Sans 3";
}
