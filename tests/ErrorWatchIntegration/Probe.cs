using System.Reflection;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Entities.Cards;
using ShoujoKageki.Core.V1;

[MegaCrit.Sts2.Core.Modding.ModInitializer(nameof(Initialize))]
public static class ErrorWatchFixture
{
    public static void Initialize()
    {
        OS.AddLogger(new PolygonErrorLogger());
        var tree = (SceneTree)Engine.GetMainLoop();
        RenderingServer.ViewportSetUpdateMode(tree.Root.GetViewportRid(), RenderingServer.ViewportUpdateMode.Always);
        tree.ProcessFrame += () => RenderingServer.ForceDraw(false);
    }
}
public sealed class ErrorWatchCmd : AbstractConsoleCmd
{
    public override string CmdName => "error_watch";
    public override string Args => "check|report";
    public override string Description => "Directed regression checks for OBS-045 through OBS-053.";
    public override bool IsNetworked => false;
    static string Report = "{}";
    static Type Karen(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name)).OfType<Type>().Single();
    private static async Task PrepareSunlight(Player player)
    {
        try
        {
            foreach(var card in player.PlayerCombatState!.AllCards.ToArray()) await MegaCrit.Sts2.Core.Commands.CardPileCmd.RemoveFromCombat(card);
            foreach(var enemy in player.Creature.CombatState!.HittableEnemies) enemy.SetCurrentHpInternal(1);
            var source=ModelDb.AllCards.First(c=>c.Id.Entry=="KAREN_SUNLIGHT");
            var permanent=player.RunState.CreateCard(source,player);permanent.UpgradeInternal();
            await MegaCrit.Sts2.Core.Commands.CardPileCmd.Add(permanent,PileType.Deck);
            var combat=CardCopies.ForCombat(permanent,player,CardCopyPurpose.Transfer,permanent);
            await MegaCrit.Sts2.Core.Commands.CardPileCmd.AddGeneratedCardToCombat(combat,PileType.Hand,player);
            Shine.SetState(permanent,1,Shine.GetState(permanent)!.Maximum);
            Shine.SetState(combat,1,Shine.GetState(combat)!.Maximum);
            await ShineRewards.Add(player);
            Report=JsonSerializer.Serialize(new{status="prepared",card=combat.Id.Entry,upgrades=combat.CurrentUpgradeLevel});
        }
        catch(Exception error){Report=JsonSerializer.Serialize(new{status="failed",error=error.ToString()});}
    }
    private static async Task Geometry()
    {
        Report="{\"status\":\"running\"}";
        var tree=(SceneTree)Engine.GetMainLoop();
        var type=Karen("ShoujoKagekiAijoKaren.src.Core.Vfx.NKarenBaseballVfx");
        var node=(Node2D)Activator.CreateInstance(type)!;
        var rows=new List<object>(); int errors=PolygonErrorLogger.Errors;
        void Set(string name,object value)=>AccessTools.Field(type,name).SetValue(node,value);
        try
        {
            node.ProcessMode=Node.ProcessModeEnum.Disabled;
            Set("_impact",true); Set("_unit",.35f); Set("_target",new Vector2(1620,620));
            tree.Root.AddChild(node);
            var light=(Node2D)AccessTools.Field(type,"_light").GetValue(node)!;
            foreach(float age in new[]{.12f,.2f,.26f,.3f,.33f,.35f,.36f,.37f,.38f})
            {
                Set("_age",age);light.QueueRedraw();
                await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);
                await tree.ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
                var polygon=(Vector2[])AccessTools.Field(type,"_shard").GetValue(node)!;
                rows.Add(new{age,triangles=Geometry2D.TriangulatePolygon(polygon).Length,points=polygon.Select(v=>new{v.X,v.Y})});
            }
        }
        catch(Exception ex) { rows.Add(new{error=ex.ToString()}); }
        finally { node.QueueFree(); }
        await tree.ToSignal(tree,SceneTree.SignalName.ProcessFrame);
        Report=JsonSerializer.Serialize(new{status="done",nativePolygonErrors=PolygonErrorLogger.Errors-errors,freed=!GodotObject.IsInstanceValid(node),rows});
        File.WriteAllText(Path.Combine(OS.GetUserDataDir(),"error-watch-geometry.json"),Report);
    }
    public override CmdResult Process(Player? player, string[] args)
    {
        if (args.FirstOrDefault() == "report") return new(true,Report);
        if (args.FirstOrDefault() == "inspect" && player != null) return new(true,JsonSerializer.Serialize(new{
            deck=player.Deck.Cards.Select(c=>new{id=c.Id.Entry,upgrades=c.CurrentUpgradeLevel,state=Shine.GetState(c),registered=player.RunState.ContainsCard(c)}),
            archive=Shine.GetDepletedCards(player)}));
        if (args.FirstOrDefault() == "prepare-sunlight" && player != null) return new(PrepareSunlight(player),true,"preparing");
        if (args.FirstOrDefault() == "geometry") return new(Geometry(),true,"running");
        if (player == null) return new(false,"Run required");
        var rows=new List<object>();
        void Check(string name, Action action) { try { action(); rows.Add(new{name,passed=true}); } catch(Exception ex) { rows.Add(new{name,passed=false,error=ex.ToString()}); } }
        var sunlight=ModelDb.AllCards.First(c=>c.Id.Entry=="KAREN_SUNLIGHT");
        Check("OBS-047 null outgoing node",()=>{
            var manager=Karen("ShoujoKagekiAijoKaren.src.Core.PromisePileSystem.Vfx.KarenPromiseVfxStarManager");
            if(AccessTools.Method(manager,"TrackOutgoingCard").Invoke(null,[null])!=null) throw new Exception("Expected no visual position");
        });
        Check("OBS-047 factory postfix accepts no visual",()=>{
            var patch=Karen("ShoujoKagekiAijoKaren.src.Core.PromisePileSystem.Patches.PromisePileCardCreationPositionPatch");
            var card=player.RunState.CreateCard(sunlight,player);
#if STS2_BETA
            var enumType=Karen("ShoujoKagekiAijoKaren.src.Core.KarenCustomEnum");
            var pile=(PileType)AccessTools.Field(enumType,"PromisePile").GetValue(null)!;
            AccessTools.Method(patch,"Postfix").Invoke(null,[card,(PileType?)pile,null]);
#else
            AccessTools.Method(patch,"Postfix").Invoke(null,[card,null]);
#endif
        });
        Check("OBS-047 unbound and freed nodes",()=>{
            var track=AccessTools.Method(Karen("ShoujoKagekiAijoKaren.src.Core.PromisePileSystem.Vfx.KarenPromiseVfxStarManager"),"TrackOutgoingCard");
            var card=new MegaCrit.Sts2.Core.Nodes.Cards.NCard();
            try { if(track.Invoke(null,[card])!=null) throw new Exception("Unbound node tracked"); } finally { card.Free(); }
            if(track.Invoke(null,[card])!=null) throw new Exception("Freed node tracked");
        });
        Check("OBS-049 reacquisition preview reward registration and identity",()=>{
            var source=player.RunState.CreateCard(sunlight,player);
            var preview=CardCopies.Preview(source,CardCopyPurpose.Reacquire);
            if(player.RunState.ContainsCard(preview)) throw new Exception("Fixture preview was already registered");
            var reward=ShineRewards.Create(preview,player);
            if(!player.RunState.ContainsCard(preview)||preview.Owner!=player) throw new Exception("Reward card missing from RunState");
            var field=AccessTools.Field(typeof(SpecialCardReward),"_card");
            if(!ReferenceEquals(preview,field.GetValue(reward))) throw new Exception("Reward copied the supplied card");
            var second=ShineRewards.Create(preview,player);
            if(!ReferenceEquals(preview,field.GetValue(second))) throw new Exception("Repeated creation changed identity");
            var restored=Reward.FromSerializable(reward.ToSerializable(),player);
            var loaded=(CardModel)field.GetValue(restored)!;
            if(!player.RunState.ContainsCard(loaded)||loaded.Id!=preview.Id) throw new Exception("Restored reward missing registration");
        });
        Check("OBS-051 canonical and ownerless descriptions",()=>{
            var canonical=ModelDb.AllCards.First(c=>c.Id.Entry=="KAREN_CARRYING_GUILT");
            var add=AccessTools.Method(canonical.GetType(),"AddExtraArgsToDescription");
            add.Invoke(canonical,[new LocString("gameplay_ui","KAREN_SHINE_CARD_REWARD")]);
            var preview=(CardModel)canonical.MutableClone();
            add.Invoke(preview,[new LocString("gameplay_ui","KAREN_SHINE_CARD_REWARD")]);
            preview.DynamicVars.CalculatedDamage.UpdateCardPreview(preview,CardPreviewMode.Normal,null,false);
            var owned=player.RunState.CreateCard(canonical,player);
            add.Invoke(owned,[new LocString("gameplay_ui","KAREN_SHINE_CARD_REWARD")]);
            owned.DynamicVars.CalculatedDamage.UpdateCardPreview(owned,CardPreviewMode.Normal,null,false);
        });
        Report=JsonSerializer.Serialize(new{rows});
        File.WriteAllText(Path.Combine(OS.GetUserDataDir(),"error-watch-results.json"),Report);
        return new(true,Report);
    }
}

public partial class PolygonErrorLogger : Godot.Logger
{
    public static int Errors;
    public override void _LogError(string function,string file,int line,string code,string rationale,bool editorNotify,int errorType,Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        if((code+rationale).Contains("triangulation failed",StringComparison.OrdinalIgnoreCase)) Errors++;
    }
}
