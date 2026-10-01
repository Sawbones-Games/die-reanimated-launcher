using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DieReanimated.Launcher.Patching;

/// <summary>Where the hook goes. The names belong to ONE client build and are pinned in the server
/// manifest, so a future build is a data change rather than a launcher release. This record is the single
/// place they appear — nothing else in the launcher, and nothing in the docs, restates them.</summary>
public sealed record HookSpec(
    string TypeName    = "ConductorGameLogic.SteamWrapperClient",
    string GetterName  = "get_SteamAuthKeyBuffer",
    string PayloadFile = "DieAuth.dll",
    string PayloadType = "DieAuth.Bootstrap",
    string PayloadMethod = "Ticket");

public enum PatchState { NotPatched, Patched, PatchedPayloadMissing, AssemblyMissing }

public sealed record PatchStatus(PatchState State, string? PayloadVersion, bool BackupPresent, string Detail);

/// <summary>
/// Replaces one property getter's body so that it returns what the payload gives it instead of its own
/// value — <c>return DieAuth.Bootstrap.Ticket(this, original);</c> — which is what makes login present a
/// Steam session ticket the server can validate. Everything else in the assembly is untouched.
///
/// <para>The field to pass along is read out of the ORIGINAL body rather than being named, so the rewrite
/// does not depend on a name that is not stable between builds; a getter that is not the exact expected
/// shape is refused rather than rewritten.</para>
/// </summary>
public static class ClientPatcher
{
    public const string AssemblyFile = "Assembly-CSharp.dll";

    public static string ManagedDir(string gameDir) =>
        Path.Combine(gameDir, "Dead Island Epidemic - Crib_Data", "Managed");

    // ── inspect ──────────────────────────────────────────────────────────────────────────────────────

    public static PatchStatus Inspect(string gameDir, HookSpec? spec = null)
    {
        spec ??= new HookSpec();
        string managed = ManagedDir(gameDir);
        string asmPath = Path.Combine(managed, AssemblyFile);
        string payloadPath = Path.Combine(managed, spec.PayloadFile);
        bool backup = File.Exists(asmPath + ".bak");
        if (!File.Exists(asmPath))
            return new PatchStatus(PatchState.AssemblyMissing, null, backup, asmPath + " not found");

        bool hooked;
        using (var module = ModuleDefinition.ReadModule(asmPath, Reader(managed)))
        {
            var getter = FindGetter(module, spec, out string why);
            if (getter is null) return new PatchStatus(PatchState.NotPatched, null, backup, why);
            hooked = IsHooked(getter, spec);
        }

        if (!hooked) return new PatchStatus(PatchState.NotPatched, null, backup, "getter is the game's original");
        if (!File.Exists(payloadPath))
            return new PatchStatus(PatchState.PatchedPayloadMissing, null, backup, "hook present but " + spec.PayloadFile + " missing");

        // The client patch is one thing with two halves: identity (this getter) and gamepad support. An
        // install carrying only the first is not up to date, so it reports NotPatched and PATCH installs
        // the rest. The version the manifest compares against stays DieAuth's — both payloads ship from the
        // same release and carry the same version.
        if (!GamepadPatcher.IsInstalled(gameDir, out string gamepadDetail))
            return new PatchStatus(PatchState.NotPatched, PayloadVersion(payloadPath), backup, gamepadDetail);

        return new PatchStatus(PatchState.Patched, PayloadVersion(payloadPath), backup, "hook present");
    }

    // ── apply ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Install <paramref name="payloadSource"/> into Managed/ and hook the getter. Safe to call
    /// every launch: an up-to-date install is a no-op. Throws <see cref="IOException"/> if the Crib is
    /// running (the assembly is locked) — callers must not retry blindly.</summary>
    public static PatchStatus Apply(string gameDir, string payloadSource, string? gamepadPayloadSource = null,
                                    HookSpec? spec = null, Action<string>? log = null)
    {
        spec ??= new HookSpec();
        log ??= _ => { };
        string managed = ManagedDir(gameDir);
        string asmPath = Path.Combine(managed, AssemblyFile);
        string payloadPath = Path.Combine(managed, spec.PayloadFile);
        if (!File.Exists(asmPath)) throw new FileNotFoundException("Crib assembly not found", asmPath);
        if (!File.Exists(payloadSource)) throw new FileNotFoundException("payload not found", payloadSource);

        // 1. the payload: copy when absent or different
        if (!File.Exists(payloadPath) || !SameBytes(payloadPath, payloadSource))
        {
            File.Copy(payloadSource, payloadPath, overwrite: true);
            log("payload → " + payloadPath);
        }

        // 2. the hook
        string backupPath = asmPath + ".bak";
        if (!File.Exists(backupPath))
        {
            File.Copy(asmPath, backupPath);
            log("backup → " + backupPath);
        }

        string tempPath = asmPath + ".tmp";
        bool changed;
        using (var module = ModuleDefinition.ReadModule(asmPath, Reader(managed)))
        {
            var getter = FindGetter(module, spec, out string why) ?? throw new InvalidOperationException(why);
            if (IsHooked(getter, spec))
            {
                changed = false;
                log("getter already hooked");
            }
            else
            {
                Rewrite(module, getter, spec, payloadPath);
                module.Write(tempPath);
                changed = true;
            }
        }
        if (changed)
        {
            File.Move(tempPath, asmPath, overwrite: true);   // after the reader's handle is closed
            log("hooked the identity getter");
        }

        // The gamepad half, in both clients. Second so the identity patch has already made the pristine
        // .bak of the Crib assembly.
        if (gamepadPayloadSource != null) GamepadPatcher.Apply(gameDir, gamepadPayloadSource, log);

        return Inspect(gameDir, spec);
    }

    /// <summary>Put the pristine assembly back and remove the payload.</summary>
    public static void Restore(string gameDir, HookSpec? spec = null, Action<string>? log = null)
    {
        spec ??= new HookSpec();
        log ??= _ => { };
        string managed = ManagedDir(gameDir);
        string asmPath = Path.Combine(managed, AssemblyFile);
        string backupPath = asmPath + ".bak";
        if (!File.Exists(backupPath)) throw new FileNotFoundException("no backup to restore", backupPath);
        File.Copy(backupPath, asmPath, overwrite: true);
        log("restored " + asmPath + " from .bak");
        string payloadPath = Path.Combine(managed, spec.PayloadFile);
        if (File.Exists(payloadPath)) { File.Delete(payloadPath); log("removed " + payloadPath); }

        // …and the gamepad half, including the match client, which the identity patch never touches.
        GamepadPatcher.Restore(gameDir, log);
    }

    // ── the rewrite ──────────────────────────────────────────────────────────────────────────────────

    private static void Rewrite(ModuleDefinition module, MethodDefinition getter, HookSpec spec, string payloadPath)
    {
        // The original getter is `ldarg.0; ldfld <buffer>; ret` (possibly with a stloc/ldloc pair). We need
        // the field it returns; everything else is replaced.
        var ldfld = getter.Body.Instructions.SingleOrDefault(i => i.OpCode == OpCodes.Ldfld)
                    ?? throw new InvalidOperationException(spec.GetterName + " does not have exactly one ldfld — not the getter this patch expects");
        var field = (FieldReference)ldfld.Operand;

        using var payload = ModuleDefinition.ReadModule(payloadPath);
        var bootstrap = payload.GetType(spec.PayloadType)
                        ?? throw new InvalidOperationException(spec.PayloadType + " not in " + payloadPath);
        var ticket = bootstrap.Methods.SingleOrDefault(m => m.Name == spec.PayloadMethod && m.IsStatic && m.Parameters.Count == 2)
                     ?? throw new InvalidOperationException(spec.PayloadType + "." + spec.PayloadMethod + "(object, byte[]) not in " + payloadPath);
        var ticketRef = module.ImportReference(ticket);

        var il = getter.Body.GetILProcessor();
        getter.Body.Instructions.Clear();
        getter.Body.Variables.Clear();
        getter.Body.ExceptionHandlers.Clear();
        il.Append(il.Create(OpCodes.Ldarg_0));          // this            → Ticket's `client`
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, field));     // this.<buffer>   → Ticket's `original`
        il.Append(il.Create(OpCodes.Call, ticketRef));
        il.Append(il.Create(OpCodes.Ret));
        getter.Body.MaxStackSize = 2;
    }

    /// <summary>Cecil must be able to resolve the assembly's references (UnityEngine, SteamworksManaged, …)
    /// when it writes; they all live next to it in Managed/.</summary>
    private static ReaderParameters Reader(string managedDir)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);
        return new ReaderParameters { ReadWrite = false, AssemblyResolver = resolver };
    }

    private static MethodDefinition? FindGetter(ModuleDefinition module, HookSpec spec, out string why)
    {
        var type = module.GetType(spec.TypeName);
        if (type is null) { why = spec.TypeName + " not found in " + module.Name; return null; }
        var getter = type.Methods.SingleOrDefault(m => m.Name == spec.GetterName && !m.IsStatic && m.Parameters.Count == 0);
        if (getter is null || !getter.HasBody) { why = spec.GetterName + " not found on " + spec.TypeName; return null; }
        why = "";
        return getter;
    }

    private static bool IsHooked(MethodDefinition getter, HookSpec spec) =>
        getter.Body.Instructions.Any(i => i.OpCode == OpCodes.Call
                                       && i.Operand is MethodReference mr
                                       && mr.DeclaringType.FullName == spec.PayloadType);

    private static string? PayloadVersion(string payloadPath)
    {
        try
        {
            using var m = ModuleDefinition.ReadModule(payloadPath);
            return m.Assembly.Name.Version.ToString(3);
        }
        catch { return null; }
    }

    private static bool SameBytes(string a, string b)
    {
        var fa = new FileInfo(a); var fb = new FileInfo(b);
        if (fa.Length != fb.Length) return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }
}
