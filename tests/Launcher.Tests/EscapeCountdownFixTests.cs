using DieReanimated.Launcher.Patching;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Launcher.Tests;

/// <summary>
/// The escape countdown fix against the REAL match client. The retail assembly cannot live in this repository,
/// so these run where a game install is present (<c>DIE_GAME_DIR</c>, or the default Steam path) and pass
/// vacuously elsewhere — CI included. Each test works on its own copy; the install is only ever read.
/// </summary>
public sealed class EscapeCountdownFixTests : IDisposable
{
    private const string MatchData = "Dead Island Epidemic_Data";
    private readonly string? _game;

    public EscapeCountdownFixTests()
    {
        string? install = Environment.GetEnvironmentVariable("DIE_GAME_DIR");
        if (string.IsNullOrEmpty(install)) install = @"D:\Games\Steam\steamapps\common\Dead Island Epidemic";
        string managed = Path.Combine(install, MatchData, "Managed");
        // The pristine assembly: the .bak when the install is patched, else the file itself.
        string pristine = Path.Combine(managed, "Assembly-CSharp.dll.bak");
        if (!File.Exists(pristine)) pristine = Path.Combine(managed, "Assembly-CSharp.dll");
        if (!File.Exists(pristine)) return;

        _game = Path.Combine(Path.GetTempPath(), "die-escape-fix-" + Guid.NewGuid().ToString("N"));
        string dst = Path.Combine(_game, MatchData, "Managed");
        Directory.CreateDirectory(dst);
        foreach (string f in Directory.GetFiles(managed, "*.dll"))
            if (!Path.GetFileName(f).Equals("Assembly-CSharp.dll", StringComparison.OrdinalIgnoreCase))
                File.Copy(f, Path.Combine(dst, Path.GetFileName(f)));
        File.Copy(pristine, Path.Combine(dst, "Assembly-CSharp.dll"));
    }

    public void Dispose()
    {
        if (_game != null) try { Directory.Delete(_game, recursive: true); } catch { }
    }

    private string Asm => EscapeCountdownPatcher.AssemblyPath(_game!);

    [Fact]
    public void AppliesOnceAndIsIdempotent()
    {
        if (_game is null) return;
        Assert.Equal(FixState.NotApplied, EscapeCountdownPatcher.Inspect(_game, out _));

        Assert.Equal(FixState.Applied, EscapeCountdownPatcher.Apply(_game));
        Assert.Equal(FixState.Applied, EscapeCountdownPatcher.Inspect(_game, out _));
        Assert.True(File.Exists(Asm + ".bak"));

        byte[] once = File.ReadAllBytes(Asm);
        Assert.Equal(FixState.Applied, EscapeCountdownPatcher.Apply(_game));
        Assert.Equal(once, File.ReadAllBytes(Asm));
    }

    [Fact]
    public void RewritesTheTestAndReleasesThePauseFlag()
    {
        if (_game is null) return;
        EscapeCountdownPatcher.Apply(_game);

        using var module = ModuleDefinition.ReadModule(Asm);
        var binding = module.GetType("UI_AnnouncementsBindings");
        var update = binding.Methods.Single(m => m.Name == "UpdateCountdown");
        var ins = update.Body.Instructions;

        // The dead `_CountdownStart < 0` test is gone, replaced by the call.
        Assert.DoesNotContain(ins, i => i.OpCode == OpCodes.Ldc_R4 && i.Next?.OpCode.FlowControl == FlowControl.Cond_Branch);
        var call = ins.Single(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference mr && mr.Name == EscapeCountdownPatcher.StaleMethod);
        Assert.Equal(Code.Ldarg_0, call.Previous.OpCode.Code);
        Assert.Equal(FlowControl.Cond_Branch, call.Next.OpCode.FlowControl);

        // The clock counts from the newest escape (the added field), not from the banner's own start time.
        Assert.Equal(Code.Ldfld, ins[1].OpCode.Code);                    // _CountdownStart
        Assert.Equal(EscapeCountdownPatcher.SinceField, ((FieldReference)ins[3].Operand).Name);
        Assert.DoesNotContain(ins, i => i.OpCode == OpCodes.Ldarg_2);

        // In the removal branch, RemoveAnnouncement = true and PauseOnScreen = false (libgui ignores a removal
        // whose pause flag does not change). The other branch still keeps the countdown paused on screen.
        var pause = ins.Where(i => i.OpCode == OpCodes.Stfld && ((FieldReference)i.Operand).Name == "PauseOnScreen").ToList();
        Assert.Equal(2, pause.Count);
        Assert.Equal(Code.Ldc_I4_0, pause[0].Previous.OpCode.Code);
        Assert.Equal(Code.Ldc_I4_1, pause[1].Previous.OpCode.Code);

        var show = binding.Methods.Single(m => m.Name == "DoGameplayAnnaouncements");
        Assert.Single(show.Body.Instructions, i => i.OpCode == OpCodes.Stfld && ((FieldReference)i.Operand).Name == EscapeCountdownPatcher.SinceField);
        Assert.Single(binding.Fields, f => f.Name == EscapeCountdownPatcher.SinceField);

        // One banner: the ShowAnnouncement call site is guarded by the live flag, and the guard is what the
        // earlier branches (team colour or not) now land on.
        var sins = show.Body.Instructions;
        var showCall = sins.Single(i => i.OpCode == OpCodes.Callvirt && ((MethodReference)i.Operand).Name == "ShowAnnouncement");
        var guardRead = sins.Single(i => i.OpCode == OpCodes.Ldfld && ((FieldReference)i.Operand).Name == EscapeCountdownPatcher.LiveField);
        Assert.True(sins.IndexOf(guardRead) < sins.IndexOf(showCall));
        Assert.Equal(showCall.Next.Next, (Instruction)guardRead.Next.Operand);   // live → skip past ShowAnnouncement's pop
        var guardStart = guardRead.Previous.Previous.Previous;                   // ldloc updateFunc
        Assert.All(sins.Where(i => i.OpCode.FlowControl == FlowControl.Cond_Branch && sins.IndexOf(i) < sins.IndexOf(guardStart)
                                   && sins.IndexOf((Instruction)i.Operand) > sins.IndexOf(guardStart)
                                   && sins.IndexOf((Instruction)i.Operand) <= sins.IndexOf(showCall)),
                   i => Assert.Same(guardStart, i.Operand));
    }

    [Fact]
    public void LeavesAnUnrecognisedBuildAlone()
    {
        if (_game is null) return;
        // A build whose dead test is not the retail one: `ldc.r4 0` → `ldc.r4 1`.
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Asm)!);
        using (var module = ModuleDefinition.ReadModule(Asm, new ReaderParameters { InMemory = true, AssemblyResolver = resolver }))
        {
            var update = module.GetType("UI_AnnouncementsBindings").Methods.Single(m => m.Name == "UpdateCountdown");
            update.Body.Instructions.Single(i => i.OpCode == OpCodes.Ldc_R4 && (float)i.Operand == 0f
                                                 && i.Next.OpCode.FlowControl == FlowControl.Cond_Branch).Operand = 1f;
            module.Write(Asm);
        }
        byte[] before = File.ReadAllBytes(Asm);

        Assert.Equal(FixState.NotApplicable, EscapeCountdownPatcher.Inspect(_game, out _));
        Assert.Equal(FixState.NotApplicable, EscapeCountdownPatcher.Apply(_game));
        Assert.Equal(before, File.ReadAllBytes(Asm));
        Assert.False(File.Exists(Asm + ".bak"));
    }

    [Fact]
    public void AbsentMatchClientIsNotApplicable()
    {
        string empty = Path.Combine(Path.GetTempPath(), "die-escape-fix-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(empty);
        try { Assert.Equal(FixState.NotApplicable, EscapeCountdownPatcher.Apply(empty)); }
        finally { Directory.Delete(empty, true); }
    }
}
