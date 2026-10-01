using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DieReanimated.Launcher.Patching;

/// <summary>One "call the payload first" hook: the client whose assembly it lives in, the method that gets
/// the call, and the static method to call. These belong to ONE client build, exactly like
/// <see cref="HookSpec"/>, and this is the single place in the repository they appear.</summary>
public sealed record PrologueSpec(
    string ClientDataDir,
    string TypeName,
    string MethodName,
    string PayloadType = "DieGamepad.Bootstrap",
    string PayloadMethod = "Init");

/// <summary>
/// The gamepad half of the client patch.
///
/// <para>Different shape from <see cref="ClientPatcher"/>, which REPLACES a getter's body. This one
/// PREPENDS a call to <c>DieGamepad.Bootstrap.Init()</c> at the top of a method that already runs, and
/// leaves everything after it untouched. That is why several independent payloads can hook one method and
/// coexist: each inserts its own call and none replaces a body.</para>
///
/// <para>Both clients are patched, not just the Crib: the pad has to work in a match as well as in the hub,
/// and they are separate executables with separate assemblies.</para>
/// </summary>
public static class GamepadPatcher
{
    public const string PayloadFile = "DieGamepad.dll";

    /// <summary>Where the call goes in each client: the earliest managed code in each, so the pad is live
    /// from the first screen rather than only once a session is under way.</summary>
    public static readonly IReadOnlyList<PrologueSpec> Hooks = new[]
    {
        new PrologueSpec("Dead Island Epidemic - Crib_Data", "CribStart",   "Awake"),
        new PrologueSpec("Dead Island Epidemic_Data",        "UnityClient", "Awake"),
    };

    public static string ManagedDir(string gameDir, PrologueSpec spec) =>
        Path.Combine(gameDir, spec.ClientDataDir, "Managed");

    /// <summary>True when every hook is in place and the payload sits beside each patched assembly.</summary>
    public static bool IsInstalled(string gameDir, out string detail)
    {
        foreach (var spec in Hooks)
        {
            string managed = ManagedDir(gameDir, spec);
            string asmPath = Path.Combine(managed, ClientPatcher.AssemblyFile);
            if (!File.Exists(asmPath)) { detail = asmPath + " not found"; return false; }
            if (!File.Exists(Path.Combine(managed, PayloadFile)))
            { detail = PayloadFile + " missing from " + spec.ClientDataDir; return false; }

            using var module = ModuleDefinition.ReadModule(asmPath, Reader(managed));
            var method = Find(module, spec);
            if (method is null) { detail = spec.TypeName + "." + spec.MethodName + " not found"; return false; }
            if (!IsHooked(method, spec)) { detail = spec.TypeName + "." + spec.MethodName + " not hooked"; return false; }
        }
        detail = "gamepad hooks present in both clients";
        return true;
    }

    /// <summary>Install the payload into both clients and insert the calls. Safe to re-run: an already
    /// hooked method is left alone.</summary>
    public static void Apply(string gameDir, string payloadSource, Action<string>? log = null)
    {
        log ??= _ => { };
        if (!File.Exists(payloadSource)) throw new FileNotFoundException("gamepad payload not found", payloadSource);

        foreach (var spec in Hooks)
        {
            string managed = ManagedDir(gameDir, spec);
            string asmPath = Path.Combine(managed, ClientPatcher.AssemblyFile);
            if (!File.Exists(asmPath))
            {
                // The match client's folder is part of every install we know of, but a partial install
                // should degrade rather than abort the whole patch.
                log("skipped " + spec.ClientDataDir + ": no " + ClientPatcher.AssemblyFile);
                continue;
            }

            string payloadPath = Path.Combine(managed, PayloadFile);
            if (!File.Exists(payloadPath) || !SameBytes(payloadPath, payloadSource))
            {
                File.Copy(payloadSource, payloadPath, overwrite: true);
                log("payload → " + payloadPath);
            }

            // Whoever touches the assembly first makes the pristine backup; both halves of the patch check
            // before writing, so the .bak is always the retail file.
            string backupPath = asmPath + ".bak";
            if (!File.Exists(backupPath)) { File.Copy(asmPath, backupPath); log("backup → " + backupPath); }

            string tempPath = asmPath + ".tmp";
            bool changed;
            using (var module = ModuleDefinition.ReadModule(asmPath, Reader(managed)))
            {
                var method = Find(module, spec)
                             ?? throw new InvalidOperationException(spec.TypeName + "." + spec.MethodName + " not found in " + asmPath);
                if (IsHooked(method, spec))
                {
                    changed = false;
                    log(spec.ClientDataDir + " already hooked");
                }
                else
                {
                    Prepend(module, method, spec, payloadPath);
                    module.Write(tempPath);
                    changed = true;
                }
            }
            if (changed)
            {
                File.Move(tempPath, asmPath, overwrite: true);   // once the reader's handle is closed
                log("hooked the gamepad start-up call in " + spec.ClientDataDir);
            }
        }
    }

    /// <summary>Put both assemblies back and remove the payload. The Crib's backup is shared with the
    /// identity patch, so restoring it undoes both — which is what "restore the original client" means.</summary>
    public static void Restore(string gameDir, Action<string>? log = null)
    {
        log ??= _ => { };
        foreach (var spec in Hooks)
        {
            string managed = ManagedDir(gameDir, spec);
            string asmPath = Path.Combine(managed, ClientPatcher.AssemblyFile);
            string backupPath = asmPath + ".bak";
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, asmPath, overwrite: true);
                log("restored " + asmPath + " from .bak");
            }
            string payloadPath = Path.Combine(managed, PayloadFile);
            if (File.Exists(payloadPath)) { File.Delete(payloadPath); log("removed " + payloadPath); }
        }
    }

    // ── the rewrite ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>Insert `call DieGamepad.Bootstrap.Init()` before the method's first instruction.</summary>
    private static void Prepend(ModuleDefinition module, MethodDefinition method, PrologueSpec spec, string payloadPath)
    {
        using var payload = ModuleDefinition.ReadModule(payloadPath);
        var bootstrap = payload.GetType(spec.PayloadType)
                        ?? throw new InvalidOperationException(spec.PayloadType + " not in " + payloadPath);
        var init = bootstrap.Methods.SingleOrDefault(m => m.Name == spec.PayloadMethod && m.IsStatic && m.Parameters.Count == 0)
                   ?? throw new InvalidOperationException(spec.PayloadType + "." + spec.PayloadMethod + "() not in " + payloadPath);

        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Call, module.ImportReference(init)));
    }

    private static MethodDefinition? Find(ModuleDefinition module, PrologueSpec spec)
    {
        var type = module.GetType(spec.TypeName);
        var method = type?.Methods.SingleOrDefault(m => m.Name == spec.MethodName && !m.IsStatic && m.Parameters.Count == 0);
        return method is { HasBody: true } ? method : null;
    }

    private static bool IsHooked(MethodDefinition method, PrologueSpec spec) =>
        method.Body.Instructions.Any(i => i.OpCode == OpCodes.Call
                                       && i.Operand is MethodReference mr
                                       && mr.DeclaringType.FullName == spec.PayloadType);

    private static ReaderParameters Reader(string managedDir)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);
        return new ReaderParameters { ReadWrite = false, AssemblyResolver = resolver };
    }

    private static bool SameBytes(string a, string b)
    {
        var fa = new FileInfo(a); var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}
