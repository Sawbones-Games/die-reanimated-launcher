using DieReanimated.Launcher;
using DieReanimated.Launcher.Patching;

// die-launcher <command> [--game <dir>] [--server <url>]
//   state     what the launcher would do now (exit code = the action: 0 play · 10 patch · 20 update · 30 not in game folder · 40 in game · 50 maintenance)
//   patch     install the client patch + write ip.cfg
//   play      write ip.cfg and start the game
//   update    bring the launcher / the client patch to the version the server names (restarts if the launcher itself updates)
//   status    inspect the client patch only, no network
//   restore   put the original client back
//   art-index the game's textures and atlas sprites (names, sizes, rects) as JSON — the catalogue the server's
//             dashboard offers when an item's art is picked; names only, no pixels
// Same code as the desktop launcher; this is the scriptable face of it.

static int Usage()
{
    Console.Error.WriteLine("usage: die-launcher state|patch|play|update|status|restore|art-index [--game <dir>] [--server <url>]");
    return 2;
}

try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
if (args.Length == 0) return Usage();
string cmd = args[0];
string? game = null, server = null;
var passthrough = new List<string>();
for (int i = 1; i < args.Length; i++)
{
    if (args[i] == "--game" && i + 1 < args.Length) game = args[++i];
    else if (args[i] == "--server" && i + 1 < args.Length) server = args[++i];
    else if (args[i] == "--updated") { /* we are the freshly swapped-in binary */ }
    else passthrough.Add(args[i]);
}

var paths = new LauncherPaths();
var settings = Settings.Load(paths);
if (server != null) settings.ServerUrl = server;
var log = new Log(paths, Console.WriteLine);
var session = new LauncherSession(paths, settings, log, installOverride: game, releases: new Releases(token: settings.ReleaseToken));

try
{
    switch (cmd)
    {
        case "status":
        {
            string dir = game ?? AppContext.BaseDirectory;
            var s = ClientPatcher.Inspect(dir);
            Console.WriteLine($"{s.State}  payload={s.PayloadVersion ?? "-"}  backup={(s.BackupPresent ? "yes" : "no")}  ({s.Detail})");
            return s.State == PatchState.Patched ? 0 : 1;
        }
        case "restore":
            ClientPatcher.Restore(game ?? AppContext.BaseDirectory, log: Console.WriteLine);
            return 0;
        case "art-index":
        {
            var cat = GameArt.Index(game ?? AppContext.BaseDirectory);
            var doc = new
            {
                textures = cat.Textures.Where(t => t.Format is 3 or 4 or 5 or 10 or 12).Select(t => new { name = t.Name, width = t.Width, height = t.Height }).OrderBy(t => t.name),
                atlases = cat.Atlases.Select(a => new { name = a.Name, sprites = a.Sprites.Select(s => new { s.Name, s.Width, s.Height, s.Rotated }).OrderBy(s => s.Name) }),
            };
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(doc, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
            return cat.Textures.Count > 0 ? 0 : 1;
        }
        case "state":
        {
            var st = await session.RefreshAsync();
            Console.WriteLine($"launcher {Defaults.VersionAndChannel} · {st.Action}: {st.Detail}");
            if (st.Action == LauncherAction.Patch) foreach (string f in LauncherSession.PatchWrites) Console.WriteLine("   writes " + f);
            return st.Action switch { LauncherAction.Play => 0, LauncherAction.Patch => 10, LauncherAction.Update or LauncherAction.Restart => 20, LauncherAction.NotInGameFolder => 30, LauncherAction.Maintenance => 50, _ => 40 };
        }
        case "patch":
        {
            await session.RefreshAsync();
            var r = await session.PatchAsync();
            return r.State == PatchState.Patched ? 0 : 1;
        }
        case "play":
        {
            var st = await session.RefreshAsync();
            if (st.Action == LauncherAction.Maintenance)
            {
                Console.Error.WriteLine("The server is down for maintenance" + (st.Detail.Length > 0 ? ": " + st.Detail : "."));
                return 50;
            }
            await session.PlayAsync(passthrough);
            return 0;
        }
        case "update":
        {
            await session.RefreshAsync();
            bool restarted = await session.UpdateAndRestartAsync(args.Skip(1));
            if (restarted) { Console.WriteLine("updated launcher started — exiting"); return 0; }
            return 0;
        }
        default:
            return Usage();
    }
}
catch (IOException e) when (e.Message.Contains("being used by another process"))
{
    Console.Error.WriteLine("The game is running — close it and retry. (" + e.Message + ")");
    return 3;
}
catch (Exception e)
{
    Console.Error.WriteLine(e.GetType().Name + ": " + e.Message);
    return 1;
}
