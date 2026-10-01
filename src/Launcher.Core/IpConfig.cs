using System.Xml.Linq;

namespace DieReanimated.Launcher;

/// <summary>
/// <c>ip.cfg</c> in the game folder tells the game where its servers are. The game reads the branch whose
/// <c>name</c> matches its Steam beta branch (<c>public</c> for everyone). The launcher rewrites the four
/// addresses it owns from the manifest and leaves everything else in the file exactly as it was.
/// </summary>
public static class IpConfig
{
    public const string Branch = "public";

    /// <summary>Returns true when the file was changed.</summary>
    public static bool Apply(string ipCfgPath, Manifest.ServerInfo server, out string detail)
    {
        XDocument doc;
        try { doc = File.Exists(ipCfgPath) ? XDocument.Load(ipCfgPath) : NewDocument(); }
        catch { doc = NewDocument(); }

        XElement root = doc.Root ?? new XElement("root");
        if (doc.Root is null) doc.Add(root);
        XElement? branch = root.Elements("branch").FirstOrDefault(b => (string?)b.Attribute("name") == Branch);
        if (branch is null) { branch = new XElement("branch", new XAttribute("name", Branch)); root.Add(branch); }

        bool changed = false;
        changed |= Set(branch, "RequestServerIP", server.RequestHost);
        changed |= Set(branch, "RequestServerPort", server.RequestPort.ToString());
        changed |= Set(branch, "MatchmakingServerIPEU", server.MatchmakingHostEU.Length > 0 ? server.MatchmakingHostEU : server.RequestHost);
        changed |= Set(branch, "MatchmakingServerIPNA", server.MatchmakingHostNA.Length > 0 ? server.MatchmakingHostNA : server.RequestHost);
        changed |= Set(branch, "MatchmakingServerPort", server.MatchmakingPort.ToString());

        if (changed) doc.Save(ipCfgPath);
        detail = changed ? $"{server.RequestHost}:{server.RequestPort} written" : "unchanged";
        return changed;
    }

    public static string? CurrentRequestHost(string ipCfgPath)
    {
        try
        {
            return XDocument.Load(ipCfgPath).Root?.Elements("branch")
                .FirstOrDefault(b => (string?)b.Attribute("name") == Branch)?.Element("RequestServerIP")?.Value;
        }
        catch { return null; }
    }

    private static bool Set(XElement branch, string name, string value)
    {
        XElement? e = branch.Element(name);
        if (e is null) { branch.Add(new XElement(name, value)); return true; }
        if (e.Value == value) return false;
        e.Value = value; return true;
    }

    private static XDocument NewDocument() =>
        new(new XDeclaration("1.0", "utf-8", null), new XElement("root", new XElement("branch", new XAttribute("name", Branch))));
}
