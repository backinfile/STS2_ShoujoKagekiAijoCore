using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using ShoujoKageki.Core.V1;
using STS2RitsuLib.Cards;

[MegaCrit.Sts2.Core.Modding.ModInitializer(nameof(Initialize))]
public static class BoundaryFixture
{
    public static void Initialize()
    {
        CardOnPlayHook.RegisterGlobalListener(new PlayListener());
        var tree=(SceneTree)Engine.GetMainLoop();
        RenderingServer.ViewportSetUpdateMode(tree.Root.GetViewportRid(),RenderingServer.ViewportUpdateMode.Always);
        tree.ProcessFrame+=()=>RenderingServer.ForceDraw(false);
    }
}
public sealed class PlayListener : ICardOnPlayHookListener
{
    public Task<bool> BeforeCardOnPlay(BeforeCardOnPlayContext context)
    {if(BoundaryCommand.Active&&context.CardPlay.Card==BoundaryCommand.Card)BoundaryCommand.Order.Add("before");return Task.FromResult(false);}
    public Task AfterCardOnPlay(AfterCardOnPlayContext context)
    {if(BoundaryCommand.Active&&context.CardPlay.Card==BoundaryCommand.Card)BoundaryCommand.Order.Add("after");return Task.CompletedTask;}
}
public sealed class BoundaryCommand : AbstractConsoleCmd
{
    public override string CmdName=>"core_boundary";
    public override string Args=>"arm|report|shuffle|status";
    public override string Description=>"Directed Core initialization boundary checks.";
    public override bool IsNetworked=>false;
    public static bool Active;
    public static CardModel? Card;
    public static readonly List<string> Order=[];
    private static IDisposable? _route,_effect;
    private static string _shuffleReport="{}";
    internal static bool Shuffling,HookDrew;
    internal static int BatchCalls,ForwarderResults;
    private static readonly Harmony Patcher=new("Core.InitializationBoundaryIntegration");
    public override CmdResult Process(Player? player,string[] args)
    {
        var action=args.FirstOrDefault();
        if(action=="report")
        {
            Active=false;_route?.Dispose();_effect?.Dispose();
            return new(true,JsonSerializer.Serialize(new{order=Order,shuffle=JsonSerializer.Deserialize<JsonElement>(_shuffleReport)}));
        }
        if(action=="status")
        {
            var scopes=CoreApi.Get<IModelMethodScopes>();
            var installed=(HashSet<MethodInfo>)AccessTools.Field(scopes.GetType(),"_patched").GetValue(scopes)!;
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("YuWanCard.Powers.InflationPower")).OfType<Type>().Single();
            return new(true,JsonSerializer.Serialize(new{generatedCallbackPatched=installed.Any(m=>m.DeclaringType==type&&m.Name=="<get_GoldGuard>b__6_2"),namedHookPatched=installed.Contains(AccessTools.Method(type,"AfterModifyingGoldGained")),installed=installed.Count}));
        }
        if(player==null)return new(false,"Run required");
        if(action=="shuffle")return new(Shuffle(player),true,"running");
        if(action!="arm")return new(false,"Unknown action");
        Card=PileType.Hand.GetPile(player).Cards.First(c=>c.Id.Entry=="KAREN_DEFEND");Order.Clear();Active=true;
        _route=CoreApi.Get<ICardPayloadRouting>().Register((CardPayloadMiddleware)(async(card,choice,play,next)=>{
            if(Active&&card==Card)Order.Add("middleware-before");
            await next.Bind(card)(choice,play);
            if(Active&&card==Card)Order.Add("middleware-after");
        }),_=>null);
        _effect=CoreApi.Get<IModelMethodScopes>().Register(AccessTools.Method(Card.GetType(),"OnPlay",[typeof(PlayerChoiceContext),typeof(CardPlay)]),model=>{if(Active&&ReferenceEquals(model,Card))Order.Add("effect");return null;});
        return new(true,JsonSerializer.Serialize(new{card=Card.Id.Entry}));
    }
    private static async Task Shuffle(Player player)
    {
        _shuffleReport="{\"status\":\"running\"}";
        try
        {
            var target=AccessTools.AsyncMoveNext(AccessTools.Method(typeof(CardPileCmd),"Shuffle"));
            Patcher.Patch(target,transpiler:new HarmonyMethod(typeof(BatchBridge),"Transpiler"){priority=int.MaxValue});
            Patcher.Patch(AccessTools.Method(typeof(MegaCrit.Sts2.Core.Hooks.Hook),"AfterCardChangedPiles"),postfix:new HarmonyMethod(typeof(DrawFromPileHook),"Postfix"));
            var draw=PileType.Draw.GetPile(player);var discard=PileType.Discard.GetPile(player);
            if(draw.Cards.Count>0)await CardPileCmd.Add(draw.Cards.ToArray(),discard);
            int hand=PileType.Hand.GetPile(player).Cards.Count;
            BatchCalls=0;ForwarderResults=0;HookDrew=false;Shuffling=true;
            await CardPileCmd.Shuffle(new ThrowingPlayerChoiceContext(),player);
            _shuffleReport=JsonSerializer.Serialize(new{status="done",batchCalls=BatchCalls,forwarderResults=ForwarderResults,hookDrew=HookDrew,handBefore=hand,handAfter=PileType.Hand.GetPile(player).Cards.Count});
        }
        catch(Exception error){_shuffleReport=JsonSerializer.Serialize(new{status="failed",error=error.ToString()});}
        finally{Shuffling=false;}
    }
}
public static class BatchBridge
{
    public static async Task<IReadOnlyList<CardPileAddResult>> Add(IEnumerable<CardModel> cards,CardPile pile,CardPilePosition position,AbstractModel? source,bool skipVisuals,bool changingOwners)
    {
        BoundaryCommand.BatchCalls++;
        var result=await CardPileCmd.Add(cards,pile,position,source,skipVisuals,changingOwners);
        BoundaryCommand.ForwarderResults=result.Count;
        return result;
    }
    public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var add=AccessTools.Method(typeof(CardPileCmd),"Add",[typeof(IEnumerable<CardModel>),typeof(CardPile),typeof(CardPilePosition),typeof(AbstractModel),typeof(bool),typeof(bool)]);
        foreach(var instruction in instructions){if(instruction.Calls(add))instruction.operand=AccessTools.Method(typeof(BatchBridge),nameof(Add));yield return instruction;}
    }
}
public static class DrawFromPileHook
{
    public static void Postfix(CardModel __2,PileType __3,ref Task __result)
    {
        if(!BoundaryCommand.Shuffling||BoundaryCommand.HookDrew||__3!=PileType.Discard||__2.Pile?.Type!=PileType.Draw)return;
        BoundaryCommand.HookDrew=true;__result=Draw(__result,__2.Owner);
    }
    private static async Task Draw(Task original,Player player){await original;await CardPileCmd.Draw(new ThrowingPlayerChoiceContext(),1m,player);}
}
