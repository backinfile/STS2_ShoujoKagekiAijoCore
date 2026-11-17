using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.Players;
using ShoujoKageki.Core.V1;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ShoujoKageki.Core.Runtime;
using ShoujoKageki.Core.Runtime.Modules.Execution;

int failures=0;
void Run(string name,Action< Harmony,Harmony> test){
 var a=new Harmony("Core.InitializationChecks.A");var b=new Harmony("Core.InitializationChecks.B");
 try{test(a,b);Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+e);}
 finally{a.UnpatchAll(a.Id);b.UnpatchAll(b.Id);}
}
#if STS2_BETA
var shuffle=typeof(RuntimeEntry).Assembly.GetType("ShoujoKageki.Core.Runtime.Patches.ShuffleMoveOrderPatch")!;
Run("OBS-034 repeated Core shuffle transpiler",(a,b)=>{a.CreateClassProcessor(shuffle).Patch();b.CreateClassProcessor(shuffle).Patch();});
Run("OBS-034 generic batch Add forwarder",(a,b)=>{
 var target=AccessTools.AsyncMoveNext(AccessTools.Method(typeof(CardPileCmd),"Shuffle"));
 a.Patch(target,transpiler:new HarmonyMethod(typeof(BatchForwarder),"Transpiler"){priority=int.MaxValue});
 b.CreateClassProcessor(shuffle).Patch();
});
Run("OBS-034 forwarding results stay intact until Shuffle",(a,b)=>{
 var add=AccessTools.Method(typeof(CardPileCmd),"Add",[typeof(IEnumerable<CardModel>),typeof(CardPile),typeof(CardPilePosition),typeof(AbstractModel),typeof(bool),typeof(bool)]);
 a.Patch(add,prefix:new HarmonyMethod(typeof(BatchResults),"Prefix"){priority=int.MaxValue});
 BatchForwarder.ObservedResults=-1;
 var task=BatchForwarder.Add([],null!,CardPilePosition.Bottom,null,true,false);
 var result=task.GetAwaiter().GetResult();
 if(result.Count!=1||BatchForwarder.ObservedResults!=1||BatchResults.SkipVisuals)throw new Exception($"Forwarder result or visual ordering changed: returned={result.Count}, observed={BatchForwarder.ObservedResults}, skip={BatchResults.SkipVisuals}");
 var discarded=(Task<IReadOnlyList<CardPileAddResult>>)AccessTools.Method(shuffle,"DiscardVisualResults").Invoke(null,[task])!;
 if(discarded.GetAwaiter().GetResult().Count!=0)throw new Exception("Shuffle retained duplicate visual results");
});
#endif
Run("OBS-046 repeated Core native dispatcher transpiler",(a,b)=>{
 var target=AccessTools.AsyncMoveNext(AccessTools.Method(typeof(CardModel),"OnPlayWrapper"));
 var boundary=typeof(CardPlayRoutingModule).GetNestedType("NativeBoundary",BindingFlags.NonPublic)!;
 a.Patch(target,transpiler:new HarmonyMethod(boundary,"Transpiler"));b.Patch(target,transpiler:new HarmonyMethod(boundary,"Transpiler"));
});
Run("OBS-046 repeated Core Ritsu delegate transpiler",(a,b)=>{
 var target=AccessTools.AsyncMoveNext(AccessTools.Method(typeof(STS2RitsuLib.Cards.CardOnPlayHook),"RunCardOnPlayHooks"));
 var boundary=typeof(CardPlayRoutingModule).GetNestedType("RitsuBoundary",BindingFlags.NonPublic)!;
 a.Patch(target,transpiler:new HarmonyMethod(boundary,"Transpiler"));b.Patch(target,transpiler:new HarmonyMethod(boundary,"Transpiler"));
});
Run("OBS-046 compatible delegate keeps before/effect/after once",(a,b)=>{
 var services=new CoreServices();var context=new CoreContext(services,"test","test","");var routing=new CardPlayRoutingModule();routing.Initialize(context);
 var order=new List<string>();
 using var registration=routing.Register((CardPayloadMiddleware)((card,choice,play,next)=>{order.Add("middleware");return next.Bind(card)(choice,play);}),_=>null);
 CompatibleDelegateHook.Before=()=>order.Add("before");CompatibleDelegateHook.After=()=>order.Add("after");CompatibleDelegateHook.Original=(_,_,_)=>{order.Add("effect");return Task.CompletedTask;};
 var boundary=typeof(CardPlayRoutingModule).GetNestedType("RitsuBoundary",BindingFlags.NonPublic)!;
 a.Patch(AccessTools.AsyncMoveNext(AccessTools.Method(typeof(CompatibleDelegateHook),"Run")),transpiler:new HarmonyMethod(boundary,"Transpiler"));
 CompatibleDelegateHook.Run(new BoundaryCard(),new ThrowingPlayerChoiceContext(),null!).GetAwaiter().GetResult();
 if(!order.SequenceEqual(new[]{"before","middleware","effect","after"}))throw new Exception(string.Join(",",order));
});
Run("OBS-052 generated callbacks are not automatic model entries",(a,b)=>{
 var services=new CoreServices();services.Add<ICoreDiscovery>(new CallbackDiscovery());services.Add<ICoreDiagnostics>(new CallbackDiagnostics());
 var context=new CoreContext(services,"test","test","");typeof(RuntimeEntry).GetProperty("Context")!.SetValue(null,context);var module=new ModelExecutionModule();module.Initialize(context);
 AccessTools.Method(typeof(ModelExecutionModule),"Install").Invoke(module,null);
 var patched=(HashSet<MethodInfo>)AccessTools.Field(typeof(ModelExecutionModule),"_patched").GetValue(module)!;
 var generated=typeof(CallbackModel).GetMethods(BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly).Where(m=>m.IsPrivate&&!m.IsVirtual&&m.IsDefined(typeof(CompilerGeneratedAttribute),false)&&m.ReturnType==typeof(Task)).ToArray();
 if(generated.Length==0||generated.Any(patched.Contains))throw new Exception("Implementation callback was patched");
 if(!patched.Contains(AccessTools.Method(typeof(CallbackModel),"AfterModifyingGoldGained")))throw new Exception("Generated native override lost");
 if(!patched.Contains(AccessTools.Method(typeof(CallbackModel),"GeneratedEntry")))throw new Exception("Generated public entry lost");
 if(!patched.Contains(typeof(CallbackModel).GetInterfaceMap(typeof(IGeneratedHook)).TargetMethods.Single()))throw new Exception("Generated interface hook lost");
 int entries=0;using var observer=module.Register((AbstractModel _,CardPlay? __)=>{entries++;return null;});
 var model=new CallbackModel();model.Entry().GetAwaiter().GetResult();if(entries!=1||model.CallbackRuns!=1)throw new Exception($"entries={entries}, callback={model.CallbackRuns}");
 try{model.ThrowsInvalid().GetAwaiter().GetResult();throw new Exception("Business exception swallowed");}catch(InvalidProgramException e)when(e.Message=="model-body"){}
});
Run("OBS-046 ambiguous delegates still reject with patch evidence",(a,b)=>{
 var target=AccessTools.AsyncMoveNext(AccessTools.Method(typeof(AmbiguousDelegateHook),"Run"));
 var boundary=typeof(CardPlayRoutingModule).GetNestedType("RitsuBoundary",BindingFlags.NonPublic)!;
 try{a.Patch(target,transpiler:new HarmonyMethod(boundary,"Transpiler"));}
 catch(InvalidOperationException e)when(e.Message.Contains("effects=2")&&e.Message.Contains("transpilers=[")&&e.Message.Contains("mvid=")){return;}
 throw new Exception("Ambiguous effect boundary accepted or evidence missing");
});
Environment.ExitCode=failures==0?0:1;
#if STS2_BETA
public static class BatchForwarder {
 public static int ObservedResults;
 public static async Task<IReadOnlyList<CardPileAddResult>> Add(IEnumerable<CardModel> cards,CardPile pile,CardPilePosition position,AbstractModel? source,bool skipVisuals,bool changingOwners){var result=await CardPileCmd.Add(cards,pile,position,source,skipVisuals,changingOwners);ObservedResults=result.Count;return result;}
 public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions){
 var add=AccessTools.Method(typeof(CardPileCmd),"Add",[typeof(IEnumerable<CardModel>),typeof(CardPile),typeof(CardPilePosition),typeof(AbstractModel),typeof(bool),typeof(bool)]);
 foreach(var i in instructions){if(i.Calls(add))i.operand=AccessTools.Method(typeof(BatchForwarder),nameof(Add));yield return i;}
 }
}
#endif
namespace STS2RitsuLib.Cards {
 public static class CardOnPlayHook {
 public delegate Task CardOnPlayDelegate(CardModel card,PlayerChoiceContext context,CardPlay play);
 public static CardOnPlayDelegate Original=(_,_,_)=>Task.CompletedTask;
 public static async Task RunCardOnPlayHooks(CardModel card,PlayerChoiceContext context,CardPlay play){await Original(card,context,play);}
 }
}

public static class CompatibleDelegateHook {
 public static Func<CardModel,PlayerChoiceContext,CardPlay,Task> Original=(_,_,_)=>Task.CompletedTask;
 public static Action Before=()=>{};public static Action After=()=>{};
 public static async Task Run(CardModel card,PlayerChoiceContext choice,CardPlay play){Before();await Original(card,choice,play);After();}
}
public sealed class BoundaryCard():CardModel(0,CardType.Skill,CardRarity.Token,TargetType.Self){protected override Task OnPlay(PlayerChoiceContext context,CardPlay play)=>Task.CompletedTask;}
public interface IGeneratedHook {Task Execute();}
public sealed class CallbackModel:AbstractModel,IGeneratedHook {
 public override bool ShouldReceiveCombatHooks=>true;public int CallbackRuns;
 private Func<Player,decimal,Task> Callback=>async delegate {CallbackRuns++;await Task.CompletedTask;};
 public Task Entry()=>Callback(null!,1m);
 public Task ThrowsInvalid()=>Task.FromException(new InvalidProgramException("model-body"));
 [CompilerGenerated]public Task GeneratedEntry()=>Task.CompletedTask;
 [CompilerGenerated]Task IGeneratedHook.Execute()=>Task.CompletedTask;
 [CompilerGenerated]public override Task AfterModifyingGoldGained(Player player,decimal amount)=>Task.CompletedTask;
}
public sealed class CallbackDiscovery:ICoreDiscovery{public IReadOnlyList<CoreModInfo> Mods=>[];public IReadOnlyList<CoreModelInfo> Models=>[new("callback","model","Fixture","1",typeof(CallbackModel))];public event Action? ModelsChanged{add{}remove{}}public void Refresh(){}}
public sealed class CallbackDiagnostics:ICoreDiagnostics{public IReadOnlyList<CoreDiagnostic> Snapshot()=>[];public event Action<CoreDiagnostic>? Reported{add{}remove{}}public void Report(DiagnosticSeverity severity,string source,string code,string message,Exception? error=null)=>Console.WriteLine(code+" "+message);}

public static class AmbiguousDelegateHook {
 public static Func<CardModel,PlayerChoiceContext,CardPlay,Task> First=(_,_,_)=>Task.CompletedTask,Second=(_,_,_)=>Task.CompletedTask;
 public static async Task Run(CardModel card,PlayerChoiceContext context,CardPlay play){await First(card,context,play);await Second(card,context,play);}
}

#if STS2_BETA
public static class BatchResults {
 public static bool SkipVisuals;
 public static bool Prefix(bool skipVisuals,ref Task<IReadOnlyList<CardPileAddResult>> __result){SkipVisuals=skipVisuals;__result=Task.FromResult<IReadOnlyList<CardPileAddResult>>([default]);return false;}
}
#endif
