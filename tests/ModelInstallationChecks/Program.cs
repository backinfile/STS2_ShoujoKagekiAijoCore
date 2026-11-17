using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using ShoujoKageki.Core.Runtime;
using ShoujoKageki.Core.Runtime.Modules.Execution;
using ShoujoKageki.Core.V1;

var services = new CoreServices();
services.Add<ICoreDiscovery>(new Discovery(args.Length==0 ? null : Assembly.LoadFrom(args[0]).GetType("SignatureModel")!));
services.Add<ICoreDiagnostics>(new Diagnostics());
var context = new CoreContext(services,"test","test","");
typeof(RuntimeEntry).GetProperty("Context")!.SetValue(null,context);
var module = new ModelExecutionModule(); module.Initialize(context);
try {
 AccessTools.Method(typeof(ModelExecutionModule),"Install").Invoke(module,null);
 try{await new GenericModel().Throws();throw new Exception("Model exception swallowed");}
 catch(IOException e)when(e.Message=="model-body"){Console.WriteLine("PASS model business exception preserved");}
 var installed=(HashSet<MethodInfo>)AccessTools.Field(typeof(ModelExecutionModule),"_patched").GetValue(module)!;
 if(!installed.Contains(AccessTools.DeclaredMethod(typeof(CardEffectBase),"OnPlay")))throw new Exception("Inherited card effect not installed");
 Console.WriteLine("PASS inherited card effect resolves its declaring method");
 int result=await new GenericModel().Result();
 if(result!=42)throw new Exception("Generic result changed");
 Console.WriteLine("PASS Task<int> model installation and result");
 if(args.Length>0){
  var patched=(HashSet<MethodInfo>)AccessTools.Field(typeof(ModelExecutionModule),"_patched").GetValue(module)!;
  var fixture=Assembly.LoadFrom(args[0]).GetType("SignatureModel")!;
  if(!patched.Contains(fixture.GetMethod("ValidHook")!))throw new Exception("Valid hook skipped after broken signature");
  if(patched.Any(m=>m.Name=="BrokenSignature"))throw new Exception("Broken signature recorded as patched");
  if(!Diagnostics.Errors.Any(e=>e is TypeLoadException))throw new Exception("Original TypeLoadException not reported");
  Console.WriteLine("PASS external missing signature isolated; valid hooks preserved");
 }
#if STS2_BETA
 var shuffle=typeof(RuntimeEntry).Assembly.GetType("ShoujoKageki.Core.Runtime.Patches.ShuffleMoveOrderPatch")!;
 new Harmony("Core.ModelInstallationChecks.Shuffle").CreateClassProcessor(shuffle).Patch();
 Console.WriteLine("PASS v0.113.1 native Shuffle patch installation");
#endif
} catch(Exception error) {Console.WriteLine("FAIL Task<int> model installation: "+error);Environment.ExitCode=1;}

public sealed class GenericModel : AbstractModel { public override bool ShouldReceiveCombatHooks => true;public Task<int> Result()=>Task.FromResult(42);public Task Throws()=>Task.FromException(new IOException("model-body")); }
public sealed class Discovery(Type? external) : ICoreDiscovery {
 public IReadOnlyList<CoreModInfo> Mods=>[];
 public IReadOnlyList<CoreModelInfo> Models=> (external==null ? Array.Empty<CoreModelInfo>() : new[]{new CoreModelInfo("signature","model","Fixture","1",external)}).Concat(new CoreModelInfo[]{new("generic","model","Fixture","1",typeof(GenericModel)),new("inherited","card","Fixture","1",typeof(InheritedCard))}).ToArray();
 public event Action? ModelsChanged{add{}remove{}}public void Refresh(){}
}
public sealed class Diagnostics : ICoreDiagnostics {
 public IReadOnlyList<CoreDiagnostic> Snapshot()=>[];public event Action<CoreDiagnostic>? Reported{add{}remove{}}
 public static readonly List<Exception> Errors=[];
 public void Report(DiagnosticSeverity severity,string source,string code,string message,Exception? error=null){if(error!=null)Errors.Add(error);Console.WriteLine(code+" "+message+" "+error?.GetType().Name);}
}

public class CardEffectBase() : CardModel(0,CardType.Skill,CardRarity.Token,TargetType.Self)
{
 protected override Task OnPlay(PlayerChoiceContext context,CardPlay play)=>Task.CompletedTask;
}
public sealed class InheritedCard : CardEffectBase { }
