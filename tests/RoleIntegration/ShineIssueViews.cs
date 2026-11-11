using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using ShoujoKageki.Core.V1;
using ShoujoKagekiJunna.Combat;
using ShoujoKagekiJunna.Models;

namespace CoreRoleIntegration;

internal static class ShineIssueViews
{
    private static CardModel? _star;
    private static int _archives;
    internal static bool TryHandle(Player player, string action, out CmdResult result)
    {
        result = new(true, "Ready");
        CardModel Fresh(string id) => player.RunState.CreateCard(CardCatalog.Model(id), player);
        if (action == "view-reward")
        {
            var reward = new CardReward([Fresh("JK131B")], CardCreationSource.Other, player,
                new CardCreationOptions([], CardCreationSource.Other, CardRarityOddsType.Uniform));
            _ = reward.SelectUnsynchronized();
        }
        else if (action == "view-prepared") NGame.Instance!.GetInspectCardScreen().Open([_star ?? throw new InvalidOperationException("Prepare a card first.")], 0);
        else if (action == "view-detail") NGame.Instance!.GetInspectCardScreen().Open(Shine.GetDepletedModels(player).ToList(), 0);
        else if (action == "view-close")
        {
            NGame.Instance!.GetInspectCardScreen().Close();
            MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance?.Close();
        }
        else if (action == "inspect-view")
            result = new(true, System.Text.Json.JsonSerializer.Serialize(new { inspectVisible=NGame.Instance!.GetInspectCardScreen().Visible, cards=Descendants(NGame.Instance!.GetInspectCardScreen()).OfType<MegaCrit.Sts2.Core.Nodes.Cards.NCard>().Where(card=>card.IsVisibleInTree() && card.Model != null).Select(card=>new { id=card.Model!.Id.ToString(), state=Shine.GetState(card.Model), text=card.Model.GetDescriptionForPile(PileType.None) }).ToArray() }));
        else if (action == "view-archive") CoreApi.Get<IShinePileView>().Show(player);
        else if (action is "ui-wheel" or "ui-inspect" or "ui-back")
        {
            var root = NGame.Instance!;
            Control control = action == "ui-back"
                ? Descendants(root).OfType<Control>().First(node => node.IsVisibleInTree() && node.GetType().Name == "NBackButton")
                : Descendants(root).OfType<NGridCardHolder>().First(node => node.IsVisibleInTree() && (action != "ui-wheel" || node.CardModel?.Id.Entry == "JUNNA_JK131B")).Hitbox;
            Vector2 point = control.GetGlobalRect().GetCenter();
            var viewport = root.GetViewport();
            viewport.PushInput(new InputEventMouseMotion { Position=point, GlobalPosition=point }, true);
            MouseButton button = action == "ui-wheel" ? MouseButton.WheelDown : action == "ui-inspect" ? MouseButton.Right : MouseButton.Left;
            viewport.PushInput(new InputEventMouseButton { Position=point, GlobalPosition=point, ButtonIndex=button, Pressed=true }, true);
            viewport.PushInput(new InputEventMouseButton { Position=point, GlobalPosition=point, ButtonIndex=button, Pressed=false }, true);
        }
        else if (action == "prepare-play") result = new(Prepare(player), true, "Prepared legal deployment and kill actions.");
        else if (action == "inspect-play")
            result = new(true, System.Text.Json.JsonSerializer.Serialize(new { state=_star == null ? null : Shine.GetState(_star), inDeck=_star != null && player.Deck.Cards.Contains(_star), archiveDelta=Shine.GetDepletedCards(player).Count-_archives }));
        else return false;
        return true;
    }
    private static async Task Prepare(Player player)
    {
        foreach (var arrow in ArrowCombat.Deployed.ToArray()) ArrowCombat.Cancel(arrow.Card);
        foreach (var card in player.PlayerCombatState!.Hand.Cards.ToArray()) await CardPileCmd.RemoveFromCombat(card, skipVisuals:true);
        _star=player.RunState.CreateCard(CardCatalog.Model("JK133"),player);
        using (KnowledgeCombat.SingleCard()) await CardPileCmd.Add(_star,PileType.Deck);
        Shine.SetState(_star,1,6);
        var actual=CardCopies.ForCombat(_star,player,CardCopyPurpose.Transfer,_star);
        await CardPileCmd.AddGeneratedCardToCombat(actual,PileType.Hand,player);
        using (KnowledgeCombat.SingleCard()) await CardPileCmd.AddGeneratedCardToCombat(CardCatalog.Create("JB01",player),PileType.Hand,player);
        player.PlayerCombatState.Energy=9;
        foreach(var enemy in player.Creature.CombatState!.Enemies.Where(enemy=>enemy.IsAlive)) await CreatureCmd.SetCurrentHp(enemy,1);
        _archives=Shine.GetDepletedCards(player).Count;
    }
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach(var child in node.GetChildren()) { yield return child; foreach(var descendant in Descendants(child)) yield return descendant; }
    }
}
