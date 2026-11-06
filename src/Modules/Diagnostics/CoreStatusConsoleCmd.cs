using System.Text.Json;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using ShoujoKageki.Core.Runtime.Modules.Shine;

namespace ShoujoKageki.Core.Runtime.Modules.Diagnostics;

public sealed class CoreStatusConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "core_status";
    public override string Args => "";
    public override string Description => "Read Core release, modules, diagnostics and the current player's shared state.";
    public override bool IsNetworked => false;
    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var context = RuntimeEntry.Context;
        var report = new
        {
            release = context.ReleaseVersion,
            game = context.GameVersion,
            fingerprint = RuntimeEntry.Fingerprint,
            apiMajor = CoreApi.ApiMajor,
            mods = context.Discovery.Mods,
            models = context.Discovery.Models.Count,
            rules = issuingPlayer == null ? null : ShineArchive.GetRules(issuingPlayer),
            depleted = issuingPlayer == null ? null : V1.Shine.GetDepletedCards(issuingPlayer),
            diagnostics = context.Diagnostics.Snapshot()
        };
        return new CmdResult(true, JsonSerializer.Serialize(report));
    }
}
