using System.Text.Json;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Modding;
using ShoujoKagekiAijoKaren.src.Models.Cards;

namespace CoreReviewIntegration;

[ModInitializer(nameof(Initialize))]
public static class KarenStartup { public static void Initialize() { } }

public sealed class KarenOnlyFixture : AbstractConsoleCmd
{
    private static string Report = "{}";
    public override string CmdName => "karen_flows_fixture";
    public override string Args => "run|report";
    public override string Description => "Karen-only loading, settlement, preview and effect regression.";
    public override bool IsNetworked => false;
    public override CmdResult Process(Player? player, string[] args) =>
        args.FirstOrDefault() == "report" ? new(true, Report) :
        player?.PlayerCombatState == null ? new(false, "Combat required") : new(Run(player), true, "running");

    private static async Task Run(Player player)
    {
        var rows = new List<object>();
        Report = JsonSerializer.Serialize(new { status = "running" });
        void Check(string name, bool passed)
        {
            rows.Add(new { name, passed });
            if (!passed) throw new InvalidOperationException(name);
        }
        try
        {
            Check("Karen-only installation has no Junna assembly",
                !AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name!.StartsWith("ShoujoKagekiJunna")));
            Check("Karen models discovered without Junna", ReflectionHelper.ModTypes.Contains(typeof(KarenFall)));
            Check("one active embedded Core", ModManager.Mods.Count(mod => mod.manifest?.id == "ShoujoKagekiAijoCore" && mod.state == ModLoadState.Loaded) == 1);
            foreach (var card in player.PlayerCombatState!.AllCards.ToArray())
                await CardPileCmd.RemoveFromCombat(card);
            await KarenFlowChecks.Run(player, Check);
            Report = JsonSerializer.Serialize(new { status = "done", passed = true, rows });
        }
        catch (Exception error)
        {
            Report = JsonSerializer.Serialize(new { status = "done", passed = false, rows, error = error.ToString() });
        }
    }
}
