using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using ShoujoKageki.Core.Runtime.Modules.Execution;
using ShoujoKageki.Core.Runtime.Modules.Shine;

var runtime = typeof(CardExecutionModule).Assembly;
Console.WriteLine($"Game: {typeof(CardModel).Assembly.GetName()}; Harmony: {typeof(Harmony).Assembly.GetName()}");
int failed = 0;
void Run(string name, Action test)
{
    try { test(); Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
}
var patcher = new Harmony("Core.ObservationChecks");
Run("OBS-045 native automatic-play boundaries", () => patcher.CreateClassProcessor(typeof(CardExecutionModule).GetNestedType("NativeInvocation",BindingFlags.NonPublic)!).Patch());
Run("OBS-046 native effect and result boundaries", () => patcher.Patch(AccessTools.AsyncMoveNext(AccessTools.Method(typeof(CardModel),"OnPlayWrapper")),transpiler:new HarmonyMethod(typeof(CardPlayRoutingModule).GetNestedType("NativeBoundary",BindingFlags.NonPublic)!,"Transpiler")));
Run("OBS-045 forwarded automatic-play boundary", () => patcher.Patch(AccessTools.AsyncMoveNext(AccessTools.Method(typeof(CardCmd),"AutoPlay")),transpiler:new HarmonyMethod(typeof(ForwardedCalls),"AutoPlay") { priority=int.MaxValue }));
Run("OBS-046 forwarded Ritsu delegate boundary", () => {
    var nested=typeof(CardPlayRoutingModule).GetNestedType("RitsuBoundary",BindingFlags.NonPublic)!;
    patcher.Patch(AccessTools.AsyncMoveNext(AccessTools.Method(typeof(STS2RitsuLib.Cards.CardOnPlayHook),"RunCardOnPlayHooks")),transpiler:new HarmonyMethod(nested,"Transpiler"));
});
Run("OBS-046 forwarded native dispatcher boundary", () => {
    var code=PatchProcessor.GetOriginalInstructions(AccessTools.AsyncMoveNext(AccessTools.Method(typeof(CardModel),"OnPlayWrapper")));
    foreach(var instruction in code)
        if(instruction.operand is MethodInfo method && method.DeclaringType==typeof(CardModel) && method.Name=="OnPlay")
        { instruction.opcode=System.Reflection.Emit.OpCodes.Call;instruction.operand=AccessTools.Method(typeof(ForwardedCalls),"Effect"); }
    ((IEnumerable<CodeInstruction>)AccessTools.Method(typeof(CardPlayRoutingModule).GetNestedType("NativeBoundary",BindingFlags.NonPublic)!,"Transpiler").Invoke(null,[code])!).ToArray();
});
Run("OBS-046 BWPxing retained Ritsu delegate dispatcher", () => {
    var code=PatchProcessor.GetOriginalInstructions(AccessTools.AsyncMoveNext(AccessTools.Method(typeof(CardModel),"OnPlayWrapper")));
    foreach(var i in code)
        if(i.operand is MethodInfo m && m.DeclaringType==typeof(CardModel) && m.Name=="OnPlay")
        { i.opcode=System.Reflection.Emit.OpCodes.Call;i.operand=AccessTools.Method(typeof(BWPxing.Compatibility.CardPlayContext),"PlayWithRitsuHooks"); }
    ((IEnumerable<CodeInstruction>)AccessTools.Method(typeof(CardPlayRoutingModule).GetNestedType("NativeBoundary",BindingFlags.NonPublic)!,"Transpiler").Invoke(null,[code])!).ToArray();
});
Run("OBS-046 forwarded middleware and terminal execute once", () => {
    var services=new ShoujoKageki.Core.Runtime.CoreServices();
    var context=new ShoujoKageki.Core.Runtime.CoreContext(services,"test","test","");
    var routing=new CardPlayRoutingModule();routing.Initialize(context);
    int middleware=0,terminal=0;
    using var registration=routing.Register((ShoujoKageki.Core.V1.CardPayloadMiddleware)((card,choice,play,next)=>{ middleware++;return next.Bind(card)(choice,play); }),_=>null);
    STS2RitsuLib.Cards.CardOnPlayHook.Original=(_,_,_)=>{terminal++;return Task.CompletedTask;};
    BWPxing.Compatibility.CardPlayContext.PlayWithRitsuHooks(new DeferredCard(),new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext(),null!).GetAwaiter().GetResult();
    if(middleware!=1 || terminal!=1) throw new Exception($"middleware={middleware}, terminal={terminal}");
});
Run("OBS-045 opaque dispatcher retains boundary rejection", () => {
    var code=PatchProcessor.GetOriginalInstructions(AccessTools.AsyncMoveNext(AccessTools.Method(typeof(CardCmd),"AutoPlay")));
    foreach(var instruction in code)
        if(instruction.operand is MethodInfo method && method.DeclaringType==typeof(CardModel) && method.Name=="OnPlayWrapper")
        {instruction.opcode=System.Reflection.Emit.OpCodes.Call;instruction.operand=AccessTools.Method(typeof(ForwardedCalls),"OpaqueNativePlay");}
    try {
        ((IEnumerable<CodeInstruction>)AccessTools.Method(typeof(CardExecutionModule).GetNestedType("NativeInvocation",BindingFlags.NonPublic)!,"Transpiler").Invoke(null,[code])!).ToArray();
    } catch(InvalidOperationException e) when(e.Message.Contains("entries=0")) {return;}
    throw new Exception("Opaque boundary was accepted");
});
Run("OBS-048 missing Prefix state preserves result", () => {
    var task = Task.FromResult<IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.CardPileAddResult>>([]);
    object?[] values = [task,null];
    AccessTools.Method(runtime.GetType("ShoujoKageki.Core.Runtime.Modules.Shine.ShineResultMovement"),"Postfix").Invoke(null,values);
    if (!ReferenceEquals(values[0],task)) throw new Exception("Native task replaced with no state");
});
Run("OBS-048 earlier Prefix skips Shine Prefix", () => {
    var movement=runtime.GetType("ShoujoKageki.Core.Runtime.Modules.Shine.ShineResultMovement")!;
    patcher.CreateClassProcessor(movement).Patch();
    var target=AccessTools.Method(typeof(CardPileCmd),"Add",[typeof(IEnumerable<CardModel>),typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPile),typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPilePosition),typeof(AbstractModel),typeof(bool)
#if STS2_BETA
        ,typeof(bool)
#endif
    ]);
    var prefix=AccessTools.Method(typeof(SkipMovePatch),"Prefix");
    patcher.Patch(target,prefix:new HarmonyMethod(prefix){priority=int.MaxValue});
    try {
        var result=CardPileCmd.Add([], (MegaCrit.Sts2.Core.Entities.Cards.CardPile)null!);
        if(!ReferenceEquals(result,SkipMovePatch.Result)) throw new Exception("Skipped Prefix result changed");
        result.GetAwaiter().GetResult();
    } finally {patcher.Unpatch(target,prefix);}
});
Run("OBS-050 native Exhaust patch installation", () => patcher.CreateClassProcessor(runtime.GetType("ShoujoKageki.Core.Runtime.Modules.Shine.ShineResultExhaust")!).Patch());
#if STS2_BETA
Run("OBS-050 exact typed result coexistence", () => patcher.Patch(AccessTools.Method(typeof(CardCmd), "Exhaust", [typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),typeof(CardModel),typeof(bool),typeof(bool)]), postfix: new HarmonyMethod(typeof(ExactResultPatch), "Postfix")));
#endif
Run("OBS-050 deferred result preserves native return type", () => {
    var services=new ShoujoKageki.Core.Runtime.CoreServices();
    services.Add<ShoujoKageki.Core.V1.ICoreDiscovery>(new ShoujoKageki.Core.Runtime.CoreDiscovery());
    var context=new ShoujoKageki.Core.Runtime.CoreContext(services,"test","test","");
    new ModelExecutionModule().Initialize(context);
    var shine=new ShineService(); shine.Initialize(context);
    ModelDb.Inject(typeof(DeferredCard));
    var card=(CardModel)ModelDb.Card<DeferredCard>().MutableClone();
    shine.SetState(card,0,1);
    var batch=AccessTools.Method(typeof(ShineService),"BeginBatch").Invoke(null,[null,null])!;
    ((HashSet<CardModel>)AccessTools.Field(batch.GetType(),"Executed").GetValue(batch)!).Add(card);
    try {
        object?[] values=[card,null];
        var run=(bool)AccessTools.Method(runtime.GetType("ShoujoKageki.Core.Runtime.Modules.Shine.ShineResultExhaust"),"Prefix").Invoke(null,values)!;
        if(run) throw new Exception("Deferred card was not deferred");
        var resultType=AccessTools.Method(typeof(CardCmd),"Exhaust",[typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),typeof(CardModel),typeof(bool),typeof(bool)]).ReturnType;
        if(!resultType.IsInstanceOfType(values[1])) throw new Exception("Deferred task is incompatible with "+resultType);
    } finally { AccessTools.Method(batch.GetType(),"Detach").Invoke(batch,null); }
});
if (args.Length > 0)
{
    Run("OBS-052 invalid model isolation and retry bookkeeping", () => {
        var legacy=Assembly.LoadFrom(args[0]).GetType("LegacyModel")!;
        var services=new ShoujoKageki.Core.Runtime.CoreServices();
        var diagnostics=new TestDiagnostics();
        services.Add<ShoujoKageki.Core.V1.ICoreDiscovery>(new TestDiscovery(legacy));
        services.Add<ShoujoKageki.Core.V1.ICoreDiagnostics>(diagnostics);
        var context=new ShoujoKageki.Core.Runtime.CoreContext(services,"test","test","");
        typeof(ShoujoKageki.Core.Runtime.RuntimeEntry).GetProperty("Context")!.SetValue(null,context);
        var module=new ModelExecutionModule(); module.Initialize(context);
        var install=AccessTools.Method(typeof(ModelExecutionModule),"Install");
        install.Invoke(module,null);
        var patched=(HashSet<MethodInfo>)AccessTools.Field(typeof(ModelExecutionModule),"_patched").GetValue(module)!;
        if(patched.Contains(legacy.GetMethod("BrokenHook")!)) throw new Exception("Invalid method recorded as patched");
        if(!patched.Contains(typeof(ValidModel).GetMethod("Execute")!)) throw new Exception("Valid model skipped after invalid model");
        if(!diagnostics.Errors.Any(e=>e is MissingMethodException)) throw new Exception("Original missing method exception not reported");
        install.Invoke(module,null);
        try { new ValidModel().Execute().GetAwaiter().GetResult(); throw new Exception("Model exception swallowed"); }
        catch (IOException e) when(e.Message=="model-body") { }
    });
}
Environment.ExitCode = failed == 0 ? 0 : 1;
public static class ExactResultPatch
{
    public static void Postfix(ref Task<MegaCrit.Sts2.Core.Entities.Cards.CardPileAddResult?> __result) { }
}

public sealed class ValidModel : MegaCrit.Sts2.Core.Models.AbstractModel
{
    public override bool ShouldReceiveCombatHooks => true;
    public Task Execute() => Task.FromException(new IOException("model-body"));
}
public sealed class TestDiscovery(Type legacy) : ShoujoKageki.Core.V1.ICoreDiscovery
{
    public IReadOnlyList<ShoujoKageki.Core.V1.CoreModInfo> Mods => [];
    public IReadOnlyList<ShoujoKageki.Core.V1.CoreModelInfo> Models => [new("legacy","model","LegacyFixture","0",legacy),new("valid","model","Test","1",typeof(ValidModel))];
    public event Action? ModelsChanged { add{} remove{} }
    public void Refresh() { }
}
public sealed class TestDiagnostics : ShoujoKageki.Core.V1.ICoreDiagnostics
{
    public readonly List<Exception> Errors=[];
    public IReadOnlyList<ShoujoKageki.Core.V1.CoreDiagnostic> Snapshot()=>[];
    public event Action<ShoujoKageki.Core.V1.CoreDiagnostic>? Reported { add{} remove{} }
    public void Report(ShoujoKageki.Core.V1.DiagnosticSeverity severity,string source,string code,string message,Exception? error=null) {if(error!=null) Errors.Add(error);Console.WriteLine(message);}
}

[ShoujoKageki.Core.V1.ShineCard(1)]
public sealed class DeferredCard() : CardModel(0,MegaCrit.Sts2.Core.Entities.Cards.CardType.Skill,MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Token,MegaCrit.Sts2.Core.Entities.Cards.TargetType.Self)
{
    protected override Task OnPlay(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext context,MegaCrit.Sts2.Core.Entities.Cards.CardPlay play)=>Task.CompletedTask;
}

public static class ForwardedCalls
{
    public static IEnumerable<CodeInstruction> AutoPlay(IEnumerable<CodeInstruction> instructions)
    {
        foreach(var instruction in instructions)
        {
            if(instruction.operand is MethodInfo method && method.DeclaringType==typeof(CardModel) && method.Name=="OnPlayWrapper")
            { instruction.opcode=System.Reflection.Emit.OpCodes.Call;instruction.operand=AccessTools.Method(typeof(ForwardedCalls),nameof(NativePlay)); }
            yield return instruction;
        }
    }
    public static Task NativePlay(CardModel card,MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext context,MegaCrit.Sts2.Core.Entities.Creatures.Creature? target,bool auto,MegaCrit.Sts2.Core.Entities.Cards.ResourceInfo resources,bool skip)
        =>card.OnPlayWrapper(context,target,auto,resources,skip);
    public static Task OpaqueNativePlay(CardModel card,MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext context,MegaCrit.Sts2.Core.Entities.Creatures.Creature? target,bool auto,MegaCrit.Sts2.Core.Entities.Cards.ResourceInfo resources,bool skip)=>Task.CompletedTask;
    public static Task Effect(CardModel card,MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext context,MegaCrit.Sts2.Core.Entities.Cards.CardPlay play)
        =>STS2RitsuLib.Cards.CardOnPlayHook.RunCardOnPlayHooks(card,context,play);
    public static Task DelegateEffect(STS2RitsuLib.Cards.CardOnPlayHook.CardOnPlayDelegate original,CardModel card,MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext context,MegaCrit.Sts2.Core.Entities.Cards.CardPlay play)
        =>original(card,context,play);
}
namespace STS2RitsuLib.Cards
{
    public static class CardOnPlayHook
    {
        public delegate Task CardOnPlayDelegate(CardModel card,MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext context,MegaCrit.Sts2.Core.Entities.Cards.CardPlay play);
        public static CardOnPlayDelegate Original = (_,_,_)=>Task.CompletedTask;
        public static async Task RunCardOnPlayHooks(CardModel card,MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext context,MegaCrit.Sts2.Core.Entities.Cards.CardPlay play)
        { await ForwardedCalls.DelegateEffect(Original,card,context,play); }
    }
}

public static class SkipMovePatch
{
    public static Task<IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.CardPileAddResult>> Result=Task.FromResult<IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.CardPileAddResult>>([]);
    public static bool Prefix(ref Task<IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.CardPileAddResult>> __result){__result=Result;return false;}
}

namespace BWPxing.Compatibility
{
    public static class CardPlayContext
    {
        private static Func<CardModel,MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext,MegaCrit.Sts2.Core.Entities.Cards.CardPlay,Task> _ritsuHooks = STS2RitsuLib.Cards.CardOnPlayHook.RunCardOnPlayHooks;
        public static Task PlayWithRitsuHooks(CardModel card,MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext context,MegaCrit.Sts2.Core.Entities.Cards.CardPlay play) => _ritsuHooks(card,context,play);
    }
}
