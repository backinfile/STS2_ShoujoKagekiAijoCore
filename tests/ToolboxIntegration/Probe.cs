using MegaCrit.Sts2.Core.Modding;
using System.Text.Json;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;

namespace CoreToolboxProbe;

[ModInitializer(nameof(Initialize))]
public static class Startup
{
    public static void Initialize() { }
}

public sealed class ToolboxCheckCommand : AbstractConsoleCmd
{
    public override string CmdName => "core_toolbox_check";
    public override string Args => "";
    public override string Description => "Check the actual Toolbox source pool and Core empty-shell localization.";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? player, string[] args)
    {
        if (player == null) return new(false, "Run required.");
        var runtime = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name?.StartsWith("ShoujoKagekiAijoCore.Runtime.", StringComparison.Ordinal) == true);
        var shellType = runtime.GetType("ShoujoKageki.Core.Runtime.Modules.Shine.CoreEmptyShell", true)!;
        var shell = ModelDb.GetById<CardModel>(ModelDb.GetId(shellType));
        // This is Toolbox.BeforeHandDraw's exact candidate source, including unlock and multiplayer filtering.
        var candidates = ModelDb.CardPool<ColorlessCardPool>().GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint).ToArray();
        var title = shell.Title;
        var description = shell.Description.GetFormattedText();
        bool inToolbox = candidates.Any(c => c.Id == shell.Id);
        var comparisonTitle = ModelDb.GetById<CardModel>(new ModelId("CARD", "KAREN_SHINE_REPRODUCE")).Title;
        var packagedLocalizations = new[] { "zhs", "eng", "jpn" }.Select(language =>
        {
            using var document = JsonDocument.Parse(Godot.FileAccess.GetFileAsString($"res://ShoujoKagekiAijoCore/localization/{language}/cards.json"));
            var entries = document.RootElement;
            bool present = entries.TryGetProperty(shell.Id.Entry + ".title", out var localizedTitle)
                && entries.TryGetProperty(shell.Id.Entry + ".description", out var localizedDescription)
                && localizedTitle.GetString() == entries.GetProperty("CORE_EMPTY_SHELL.title").GetString()
                && localizedDescription.GetString() == entries.GetProperty("CORE_EMPTY_SHELL.description").GetString();
            return new { language, present };
        }).ToArray();
        bool localized = !title.StartsWith("cards.", StringComparison.Ordinal) && !description.StartsWith("cards.", StringComparison.Ordinal);
        bool valid = !inToolbox && localized && shell.Pool is TokenCardPool
            && shell.VisualCardPool is ColorlessCardPool
            && shell.Id.Entry == "SHOUJOKAGEKI-CORE_EMPTY_SHELL"
            && packagedLocalizations.All(entry => entry.present);
        return new(valid, JsonSerializer.Serialize(new {
            shellId = shell.Id.ToString(), pool = shell.Pool.Id.ToString(),
            visualPool = shell.VisualCardPool.Id.ToString(), title, description, comparisonTitle,
            toolboxContainsShell = inToolbox, localized, candidateCount = candidates.Length,
            isTokenPool = shell.Pool is TokenCardPool, packagedLocalizations
        }));
    }
}
