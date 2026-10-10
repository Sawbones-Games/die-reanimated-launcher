using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace DieReanimated.Launcher.Patching;

/// <summary>Where the escape-countdown fix goes. Like <see cref="HookSpec"/>, these names belong to ONE build of
/// the match client and this record is the single place they appear. <see cref="EscapingTeamField"/> is the
/// obfuscated name of <c>ScavengerHuntData.EscapingTeam</c>; every other name survived the obfuscator.</summary>
public sealed record EscapeCountdownSpec(
    string ClientDataDir       = "Dead Island Epidemic_Data",
    string BindingType         = "UI_AnnouncementsBindings",
    string UpdateMethod        = "UpdateCountdown",
    string ShowMethod          = "DoGameplayAnnaouncements",
    string CountdownStartField = "_CountdownStart",
    string GameManagerField    = "GameManager",
    string AnnouncementsField  = "Announcements",
    string EscapingTeamField   = "#dtb");

public enum FixState { Applied, NotApplied, NotApplicable }

/// <summary>
/// A fix for a bug in the game itself, not a payload: the Scavenger "Team X is winning in N…" countdown never
/// leaves the screen when the escape is cancelled.
///
/// <para>Up to 0.8.3 the game cleared it: the "Team denied" announcement was shown with <c>overwrite: true</c>,
/// which zeroed the old HUD's escape timer. 0.8.4 moved the announcement to the libgui queue and lost that
/// path — the countdown's update callback only asks to be removed when <c>_CountdownStart &lt; 0</c>, a value
/// set to the escape time and never changed. So a cancelled countdown stays on screen for the rest of the match,
/// and, because it pauses on screen, every later general announcement queues behind it for good.</para>
///
/// <para>The binding already treats the countdown as ONE thing — its text and start value are single fields, so
/// two countdown banners would show the same text anyway. The fix makes that explicit:</para>
/// <list type="bullet">
/// <item><b>One banner.</b> <c>DoGameplayAnnaouncements</c> raises a countdown banner only when none is alive;
///   a later countdown (the escape re-armed, or passed straight to another team) updates the live banner's text
///   and clock instead of queueing a second banner behind it.</item>
/// <item><b>One clock.</b> The time left counts from when the client learned of the newest escape, not from when
///   libgui happened to show the banner — a banner that waited in libgui's queue would otherwise start late.</item>
/// <item><b>Removed when no team is escaping.</b> <c>UpdateCountdown</c> asks a new method instead of the dead
///   test, and its removal branch now also releases <c>PauseOnScreen</c>: libgui only acts on a removal when that
///   flag changes. Removing the banner frees the slot for the next countdown.</item>
/// </list>
/// <para>A countdown that ends in a win is unchanged — the escaping team stays set through the end of the match.
/// The fix is cosmetic, so it never blocks a patch: an assembly whose methods are not the exact expected shape is
/// <see cref="FixState.NotApplicable"/> and is left alone.</para>
/// </summary>
public static class EscapeCountdownPatcher
{
    /// <summary>The method this fix adds. Its presence, called from <c>UpdateCountdown</c>, is the detection.</summary>
    public const string StaleMethod = "DieCountdownIsStale";
    /// <summary>Added field: when the client learned of the newest escape — the countdown's clock.</summary>
    public const string SinceField = "_DieCountdownSince";
    /// <summary>Added field: a countdown banner is alive (on screen or waiting in libgui's queue).</summary>
    public const string LiveField = "_DieCountdownLive";

    public static string AssemblyPath(string gameDir, EscapeCountdownSpec? spec = null) =>
        Path.Combine(gameDir, (spec ?? new()).ClientDataDir, "Managed", ClientPatcher.AssemblyFile);

    public static FixState Inspect(string gameDir, out string detail, EscapeCountdownSpec? spec = null)
    {
        spec ??= new EscapeCountdownSpec();
        string asmPath = AssemblyPath(gameDir, spec);
        if (!File.Exists(asmPath)) { detail = asmPath + " not found"; return FixState.NotApplicable; }
        try
        {
            using var module = ModuleDefinition.ReadModule(asmPath, Reader(Path.GetDirectoryName(asmPath)!));
            return Classify(module, spec, out detail, out _);
        }
        catch (Exception ex) when (ex is not IOException)
        {
            detail = "escape countdown fix could not read the assembly: " + ex.Message;
            return FixState.NotApplicable;
        }
    }

    /// <summary>Apply the fix to the match client. Safe to re-run; an applied or unrecognised assembly is left
    /// alone, and so is one the fix fails on for any other reason — a cosmetic fix must never abort a patch.
    /// Throws <see cref="IOException"/> if the client is running.</summary>
    public static FixState Apply(string gameDir, Action<string>? log = null, EscapeCountdownSpec? spec = null)
    {
        log ??= _ => { };
        spec ??= new EscapeCountdownSpec();
        try { return ApplyCore(gameDir, log, spec); }
        catch (Exception ex) when (ex is not IOException)
        {
            // The original is only ever replaced by a finished .tmp, so a failure leaves it untouched.
            try { File.Delete(AssemblyPath(gameDir, spec) + ".tmp"); } catch { }
            log("escape countdown fix skipped: " + ex.GetType().Name + ": " + ex.Message);
            return FixState.NotApplicable;
        }
    }

    private static FixState ApplyCore(string gameDir, Action<string> log, EscapeCountdownSpec spec)
    {
        string asmPath = AssemblyPath(gameDir, spec);
        if (!File.Exists(asmPath)) { log("escape countdown fix skipped: " + asmPath + " not found"); return FixState.NotApplicable; }

        string tempPath = asmPath + ".tmp";
        FixState state;
        string detail;
        using (var module = ModuleDefinition.ReadModule(asmPath, Reader(Path.GetDirectoryName(asmPath)!)))
        {
            state = Classify(module, spec, out detail, out Targets? t);
            if (state == FixState.NotApplied)
            {
                // Whoever touches the assembly first makes the pristine backup — same rule as both other halves.
                string backupPath = asmPath + ".bak";
                if (!File.Exists(backupPath)) { File.Copy(asmPath, backupPath); log("backup → " + backupPath); }
                Rewrite(module, t!);
                module.Write(tempPath);
            }
        }
        if (state == FixState.NotApplied)
        {
            File.Move(tempPath, asmPath, overwrite: true);   // once the reader's handle is closed
            log("fixed the escape countdown in " + spec.ClientDataDir);
            return FixState.Applied;
        }
        log(state == FixState.Applied ? "escape countdown already fixed" : "escape countdown fix skipped: " + detail);
        return state;
    }

    // ── recognising the build ────────────────────────────────────────────────────────────────────────

    /// <summary>Everything the rewrite needs, found in the original methods rather than named where possible.</summary>
    private sealed record Targets(
        TypeDefinition Binding, MethodDefinition Update, MethodDefinition Show,
        Instruction ClockStartTime, Instruction StaleTest, Instruction RemovePauseFlag,
        Instruction CountdownStartStore, Instruction ShowStart, Instruction ShowPop, VariableDefinition UpdateFuncLocal,
        FieldDefinition GameManager, MethodReference GetGameMode, MethodReference GetTime,
        MethodDefinition GetAsScavengerHunt, MethodDefinition GetGameModeData, FieldDefinition EscapingTeam);

    private static FixState Classify(ModuleDefinition module, EscapeCountdownSpec spec, out string detail, out Targets? targets)
    {
        targets = null;
        var binding = module.GetType(spec.BindingType);
        if (binding is null) { detail = spec.BindingType + " not found"; return FixState.NotApplicable; }
        var update = binding.Methods.SingleOrDefault(m => m.Name == spec.UpdateMethod && !m.IsStatic && m.Parameters.Count == 3 && m.HasBody);
        var show = binding.Methods.SingleOrDefault(m => m.Name == spec.ShowMethod && !m.IsStatic && m.Parameters.Count == 0 && m.HasBody);
        if (update is null || show is null) { detail = spec.UpdateMethod + "/" + spec.ShowMethod + " not found"; return FixState.NotApplicable; }

        bool calls = update.Body.Instructions.Any(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference mr
                                                       && mr.Name == StaleMethod && mr.DeclaringType.FullName == binding.FullName);
        bool stamps = show.Body.Instructions.Any(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference fr && fr.Name == SinceField);
        bool guards = show.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldfld && i.Operand is FieldReference fr && fr.Name == LiveField);
        bool added = binding.Methods.Any(m => m.Name == StaleMethod) || binding.Fields.Any(f => f.Name == SinceField || f.Name == LiveField);
        if (calls && stamps && guards) { detail = "escape countdown fixed"; return FixState.Applied; }
        if (calls || stamps || guards || added)
        { detail = "escape countdown fix partly present (an older build of it?) — restore the original client"; return FixState.NotApplicable; }

        if (!TryFindTargets(binding, update, show, spec, out targets, out detail)) return FixState.NotApplicable;
        detail = "escape countdown not fixed";
        return FixState.NotApplied;
    }

    private static bool TryFindTargets(TypeDefinition binding, MethodDefinition update, MethodDefinition show,
                                       EscapeCountdownSpec spec, out Targets? t, out string why)
    {
        t = null;
        var countdownStart = binding.Fields.SingleOrDefault(f => f.Name == spec.CountdownStartField && f.FieldType.MetadataType == MetadataType.Single);
        var gameManager = binding.Fields.SingleOrDefault(f => f.Name == spec.GameManagerField);
        if (countdownStart is null || gameManager is null) { why = "binding fields not found"; return false; }
        bool IsStart(Instruction i) => i.Operand is FieldReference f && f.Name == countdownStart.Name;

        // UpdateCountdown opens with its clock: `ldarg.0; ldfld _CountdownStart; ldarg.2 (startTime); call Time.get_time`.
        var ins = update.Body.Instructions;
        if (ins.Count < 4 || ins[0].OpCode != OpCodes.Ldarg_0 || ins[1].OpCode != OpCodes.Ldfld || !IsStart(ins[1])
            || ins[2].OpCode != OpCodes.Ldarg_2 || ins[3].OpCode != OpCodes.Call || ins[3].Operand is not MethodReference getTime
            || getTime.Name != "get_time" || getTime.DeclaringType.FullName != "UnityEngine.Time")
        { why = "UpdateCountdown is not the expected shape (clock)"; return false; }

        // Its dead test: `ldarg.0; ldfld _CountdownStart; ldc.r4 0; bge.un <keep>` — then the removal branch, whose
        // only `PauseOnScreen` store follows `ldc.i4.1`. Exactly one of each, or this is not the build.
        var tests = Enumerable.Range(0, Math.Max(0, ins.Count - 3)).Where(k =>
            ins[k].OpCode == OpCodes.Ldarg_0
            && ins[k + 1].OpCode == OpCodes.Ldfld && IsStart(ins[k + 1])
            && ins[k + 2].OpCode == OpCodes.Ldc_R4 && ins[k + 2].Operand is float z && z == 0f
            && (ins[k + 3].OpCode == OpCodes.Bge_Un || ins[k + 3].OpCode == OpCodes.Bge_Un_S)).ToList();
        if (tests.Count != 1) { why = "UpdateCountdown is not the expected shape (stale test)"; return false; }
        int at = tests[0];
        int keepIx = ins.IndexOf((Instruction)ins[at + 3].Operand);
        var removeBranch = ins.Skip(at + 4).Take(Math.Max(0, keepIx - at - 4)).ToList();
        var removeStores = removeBranch.Where(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference fr && fr.Name == "RemoveAnnouncement").ToList();
        var pauseStores = removeBranch.Where(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference fr && fr.Name == "PauseOnScreen").ToList();
        if (keepIx <= at || removeStores.Count != 1 || pauseStores.Count != 1 || pauseStores[0].Previous.OpCode != OpCodes.Ldc_I4_1)
        { why = "UpdateCountdown is not the expected shape (removal branch)"; return false; }

        // DoGameplayAnnaouncements: the one `stfld _CountdownStart` (the countdown branch), the local the update
        // callback is stored in (null for every other announcement), and the one ShowAnnouncement call site,
        // `ldarg.0; ldfld Announcements; ldloc data; ldnull; callvirt ShowAnnouncement; pop`.
        var sins = show.Body.Instructions;
        var startStores = sins.Where(i => i.OpCode == OpCodes.Stfld && IsStart(i)).ToList();
        var funcCtors = sins.Where(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference mr
                                        && mr.DeclaringType.Name == "AnnouncementUpdatFunc").ToList();
        var showCalls = sins.Where(i => i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference mr && mr.Name == "ShowAnnouncement").ToList();
        if (startStores.Count != 1 || funcCtors.Count != 1 || showCalls.Count != 1) { why = spec.ShowMethod + " is not the expected shape"; return false; }
        var funcLocal = StoredLocal(funcCtors[0].Next, show.Body);
        var call = showCalls[0];
        var showStart = call.Previous?.Previous?.Previous?.Previous;
        if (funcLocal is null || call.Next?.OpCode != OpCodes.Pop || call.Previous?.OpCode != OpCodes.Ldnull
            || showStart is null || showStart.OpCode != OpCodes.Ldarg_0
            || showStart.Next.OpCode != OpCodes.Ldfld || ((FieldReference)showStart.Next.Operand).Name != spec.AnnouncementsField)
        { why = spec.ShowMethod + " is not the expected shape (show call)"; return false; }

        // GameManager.GameMode → AsScavengerHunt → GameModeData.EscapingTeam. The getter reference is lifted from
        // the method that already makes this call, so it is exactly what the game itself uses.
        var getGameMode = sins.Select(i => i.Operand).OfType<MethodReference>()
                              .FirstOrDefault(m => m.Name == "get_GameMode" && m.Parameters.Count == 0);
        var gameModeType = getGameMode?.ReturnType.Resolve();
        var getAsScav = gameModeType?.Properties.SingleOrDefault(p => p.Name == "AsScavengerHunt")?.GetMethod;
        var scavType = getAsScav?.ReturnType.Resolve();
        var getData = scavType?.Properties.SingleOrDefault(p => p.Name == "GameModeData")?.GetMethod;
        var dataType = getData?.ReturnType.Resolve();
        var escaping = dataType?.Fields.SingleOrDefault(f => f.Name == spec.EscapingTeamField);
        if (getGameMode is null || getAsScav is null || getData is null || dataType is null || !dataType.IsValueType
            || escaping is null || escaping.FieldType.Name != "TeamId")
        { why = "the escaping-team path (GameMode.AsScavengerHunt.GameModeData." + spec.EscapingTeamField + ") not found"; return false; }

        t = new Targets(binding, update, show, ins[2], ins[at], pauseStores[0].Previous,
                        startStores[0], showStart, call.Next, funcLocal,
                        gameManager, getGameMode, getTime, getAsScav, getData, escaping);
        why = "";
        return true;
    }

    private static VariableDefinition? StoredLocal(Instruction? i, MethodBody body) => i?.OpCode.Code switch
    {
        Code.Stloc_0 => body.Variables[0], Code.Stloc_1 => body.Variables[1],
        Code.Stloc_2 => body.Variables[2], Code.Stloc_3 => body.Variables[3],
        Code.Stloc or Code.Stloc_S => (VariableDefinition)i.Operand,
        _ => null,
    };

    // ── the rewrite ──────────────────────────────────────────────────────────────────────────────────

    private static void Rewrite(ModuleDefinition module, Targets t)
    {
        var since = new FieldDefinition(SinceField, FieldAttributes.Private, module.TypeSystem.Single);
        var live = new FieldDefinition(LiveField, FieldAttributes.Private, module.TypeSystem.Boolean);
        t.Binding.Fields.Add(since);
        t.Binding.Fields.Add(live);
        var stale = BuildStaleMethod(module, t, live);
        t.Binding.Methods.Add(stale);

        // ── UpdateCountdown ──
        var body = t.Update.Body;
        body.SimplifyMacros();
        var il = body.GetILProcessor();
        // The clock: `_CountdownStart + (startTime - Time.time)` → `_CountdownStart + (_DieCountdownSince - Time.time)`.
        var clockLoad = il.Create(OpCodes.Ldarg_0);
        il.Replace(t.ClockStartTime, clockLoad);
        il.InsertAfter(clockLoad, il.Create(OpCodes.Ldfld, since));
        // The test: `ldarg.0; ldfld _CountdownStart; ldc.r4 0; bge.un keep` → `ldarg.0; call DieCountdownIsStale;
        // brfalse keep`. The leading ldarg.0 stays — it is a branch target — and becomes the call's `this`.
        var load = t.StaleTest.Next;
        var zero = load.Next;
        var branch = zero.Next;
        il.Replace(load, il.Create(OpCodes.Call, stale));
        il.Remove(zero);
        il.Replace(branch, il.Create(OpCodes.Brfalse, (Instruction)branch.Operand));
        // The removal branch hands back PauseOnScreen = false, so libgui honours RemoveAnnouncement.
        il.Replace(t.RemovePauseFlag, il.Create(OpCodes.Ldc_I4_0));
        body.OptimizeMacros();

        // ── DoGameplayAnnaouncements ──
        var show = t.Show.Body;
        show.SimplifyMacros();
        var sil = show.GetILProcessor();
        // After `stfld _CountdownStart`: `this._DieCountdownSince = Time.time;` — every countdown restarts the clock.
        var a = sil.Create(OpCodes.Ldarg_0);
        var b = sil.Create(OpCodes.Call, t.GetTime);
        var c = sil.Create(OpCodes.Stfld, since);
        sil.InsertAfter(t.CountdownStartStore, a);
        sil.InsertAfter(a, b);
        sil.InsertAfter(b, c);
        // Before the ShowAnnouncement call site:
        //     if (updateFunc != null) { if (_DieCountdownLive) goto next; _DieCountdownLive = true; }
        // Everything that branched to the call site now branches to the guard.
        var afterShow = t.ShowPop.Next;
        var g0 = sil.Create(OpCodes.Ldloc, t.UpdateFuncLocal);
        var guard = new[]
        {
            g0,
            sil.Create(OpCodes.Brfalse, t.ShowStart),
            sil.Create(OpCodes.Ldarg_0),
            sil.Create(OpCodes.Ldfld, live),
            sil.Create(OpCodes.Brtrue, afterShow),
            sil.Create(OpCodes.Ldarg_0),
            sil.Create(OpCodes.Ldc_I4_1),
            sil.Create(OpCodes.Stfld, live),
        };
        foreach (var i in show.Instructions)
            if (i.Operand == t.ShowStart) i.Operand = g0;
            else if (i.Operand is Instruction[] targets)
                for (int k = 0; k < targets.Length; k++) if (targets[k] == t.ShowStart) targets[k] = g0;
        foreach (var h in show.ExceptionHandlers)
        {
            if (h.TryStart == t.ShowStart) h.TryStart = g0;
            if (h.HandlerStart == t.ShowStart) h.HandlerStart = g0;
        }
        var prev = t.ShowStart.Previous;
        foreach (var i in guard) { sil.InsertAfter(prev, i); prev = i; }
        show.OptimizeMacros();
    }

    /// <summary>
    /// <code>
    /// private bool DieCountdownIsStale()
    /// {
    ///     var gm = GameManager;                     if (gm == null) return false;
    ///     var mode = gm.GameMode;                   if (mode == null) return false;
    ///     var scav = mode.AsScavengerHunt;          if (scav == null) return false;
    ///     if (scav.GameModeData.EscapingTeam != TeamId.Neutral) return false;
    ///     _DieCountdownLive = false;                // the slot is free for the next countdown
    ///     return true;                              // the escape was cancelled
    /// }
    /// </code>
    /// </summary>
    private static MethodDefinition BuildStaleMethod(ModuleDefinition module, Targets t, FieldDefinition live)
    {
        var m = new MethodDefinition(StaleMethod, MethodAttributes.Private | MethodAttributes.HideBySig, module.TypeSystem.Boolean);
        var vGm = new VariableDefinition(module.ImportReference(t.GameManager.FieldType));
        var vMode = new VariableDefinition(module.ImportReference(t.GetGameMode.ReturnType));
        var vScav = new VariableDefinition(module.ImportReference(t.GetAsScavengerHunt.ReturnType));
        var vData = new VariableDefinition(module.ImportReference(t.GetGameModeData.ReturnType));
        foreach (var v in new[] { vGm, vMode, vScav, vData }) m.Body.Variables.Add(v);
        m.Body.InitLocals = true;

        var il = m.Body.GetILProcessor();
        var no = il.Create(OpCodes.Ldc_I4_0);

        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldfld, t.GameManager));
        il.Append(il.Create(OpCodes.Stloc, vGm));
        il.Append(il.Create(OpCodes.Ldloc, vGm));
        il.Append(il.Create(OpCodes.Brfalse, no));
        il.Append(il.Create(OpCodes.Ldloc, vGm));
        il.Append(il.Create(OpCodes.Callvirt, module.ImportReference(t.GetGameMode)));
        il.Append(il.Create(OpCodes.Stloc, vMode));
        il.Append(il.Create(OpCodes.Ldloc, vMode));
        il.Append(il.Create(OpCodes.Brfalse, no));
        il.Append(il.Create(OpCodes.Ldloc, vMode));
        il.Append(il.Create(OpCodes.Callvirt, module.ImportReference(t.GetAsScavengerHunt)));
        il.Append(il.Create(OpCodes.Stloc, vScav));
        il.Append(il.Create(OpCodes.Ldloc, vScav));
        il.Append(il.Create(OpCodes.Brfalse, no));
        il.Append(il.Create(OpCodes.Ldloc, vScav));
        il.Append(il.Create(OpCodes.Callvirt, module.ImportReference(t.GetGameModeData)));
        il.Append(il.Create(OpCodes.Stloc, vData));
        il.Append(il.Create(OpCodes.Ldloca, vData));
        il.Append(il.Create(OpCodes.Ldfld, module.ImportReference(t.EscapingTeam)));
        il.Append(il.Create(OpCodes.Brtrue, no));                 // != TeamId.Neutral (0)
        il.Append(il.Create(OpCodes.Ldarg_0));
        il.Append(il.Create(OpCodes.Ldc_I4_0));
        il.Append(il.Create(OpCodes.Stfld, live));
        il.Append(il.Create(OpCodes.Ldc_I4_1));
        il.Append(il.Create(OpCodes.Ret));

        il.Append(no);
        il.Append(il.Create(OpCodes.Ret));
        m.Body.OptimizeMacros();
        return m;
    }

    private static ReaderParameters Reader(string managedDir)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);
        return new ReaderParameters { ReadWrite = false, AssemblyResolver = resolver };
    }
}
