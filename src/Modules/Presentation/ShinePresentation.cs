using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Runs;
using ShoujoKageki.Core.Runtime.Modules.Shine;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

public sealed class ShinePresentation : ICoreModule, IShinePresentation
{
    public string Id => "shine-presentation";
    public int Order => 0;
    public void Initialize(CoreContext context)
    {
        context.Services.Add<IShinePresentation>(this);
        context.Configuration.Changed += (previous, current) =>
        {
            if (previous.VfxLevel != current.VfxLevel) KarenVfxQuality.ApplyLevel();
            if (previous.ShowDerivedCardLabel != current.ShowDerivedCardLabel && Engine.GetMainLoop() is SceneTree tree)
                NCardDerivedLabelPatch.RefreshAll(tree.Root);
        };
        ShineArchive.Changed += ShinePileManager.Refresh;
    }
    public bool IsDepletionAnimating(CardModel card) => NKarenShineDepleteVfx.IsAnimating(card);
    internal void PlayDepletion(CardModel card)
    {
        if (CoreApi.Configuration.Current.EnableShineDepletionAnimation) NKarenShineDepleteVfx.Play(card, NCard.FindOnTable(card));
    }
}
internal static class ShineExtension
{
    public static bool IsShineCard(this CardModel card) => V1.Shine.IsShineCard(card);
    public static bool HasShine(this CardModel card) => V1.Shine.GetState(card)?.Remaining > 0;
    public static int GetShineValue(this CardModel card) => V1.Shine.GetState(card)?.Remaining ?? -1;
    public static int GetShineMaxValue(this CardModel card) => V1.Shine.GetState(card)?.Maximum ?? -1;
    public static bool ShouldEnterShinePileAfterPlay(this CardModel card) => V1.Shine.GetState(card)?.PendingDepletion == true;
}
internal static class ShinePileManager
{
    private static readonly ConditionalWeakTable<Player, CardPile> Piles = new();
    public static CardPile GetShinePile(Player player) => Piles.GetValue(player, p =>
    {
        var pile = new CardPile(PileType.None);
        foreach (var card in ShineArchive.Models(p)) pile.AddInternal(card);
        return pile;
    });
    internal static void Refresh(Player player)
    {
        if (!Piles.TryGetValue(player, out var pile)) return;
        pile.Clear();
        foreach (var card in ShineArchive.Models(player)) pile.AddInternal(card);
    }
    public static int GetShinePileCount(Player player) => ShineArchive.Snapshots(player).Count;
    public static NCardPileScreen ShowScreen(Player player)
    {
        var pile = new CardPile(PileType.None);
        foreach (var card in ShineArchive.Models(player)) pile.AddInternal(card);
        var screen = NCardPileScreen.ShowScreen(pile, Array.Empty<string>());
        screen.Name = "NCardPileScreen-ShinePile";
        NKarenPileViewVfx.Attach(screen, false);
        return screen;
    }
}
internal static class NTopBarShinePileButtonHelper
{
    public static NTopBarShinePileButton? GetOrCreate(NTopBar bar)
    {
        var existing = bar.GetNodeOrNull<NTopBarShinePileButton>("RightAlignedStuff/ShinePile");
        if (existing != null) return existing;
        var scene = ResourceLoader.Load<PackedScene>(CoreApi.ResourceRoot + "scenes/shine_pile_button.tscn");
        var button = scene.Instantiate<NTopBarShinePileButton>();
        button.Name = "ShinePile";
        var parent = bar.GetNode<Control>("RightAlignedStuff");
        var index = bar.Map.GetIndex();
        parent.AddChild(button);
        parent.MoveChild(button, index);
        return button;
    }
}
[HarmonyPatch(typeof(NTopBar), nameof(NTopBar.Initialize))]
internal static class ShineButtonPatch
{
    private static void Postfix(NTopBar __instance, IRunState runState)
    {
        var player = LocalContext.GetMe(runState);
        if (player != null) NTopBarShinePileButtonHelper.GetOrCreate(__instance)?.Initialize(player);
    }
}
[HarmonyPatch]
internal static class ShineDescriptionPatch
{
    private static System.Reflection.MethodBase TargetMethod() => AccessTools.GetDeclaredMethods(typeof(CardModel)).Single(x => x.Name == "GetDescriptionForPile" && x.GetParameters().Length == 3);
    private static void Postfix(CardModel __instance, ref string __result)
    {
        if (V1.Shine.GetState(__instance) is not { } state) return;
        var label = new LocString("gameplay_ui", "CORE_SHINE_LABEL").GetFormattedText();
        var suffix = new LocString("gameplay_ui", "CORE_SHINE_SUFFIX").GetFormattedText();
        var number = state.Remaining < state.Maximum ? $"[red]{state.Remaining}[/red]" : state.Remaining.ToString();
        __result += "\n" + label + number + suffix;
    }
}
[HarmonyPatch(typeof(CardModel), nameof(CardModel.ShouldGlowRed), MethodType.Getter)]
internal static class ShineGlowPatch
{
    private static void Postfix(CardModel __instance, ref bool __result) => __result |= ShineVfx.WillDeplete(__instance);
}
