using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;

using System;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace ShoujoKageki.Core.Exports
{
    /// <summary>
    /// 调试控制台命令：导出所有已发现卡牌的卡图，同时导出游戏内所有卡牌、遗物、怪物和药水的 ID 与中文名映射。
    /// 用法：<c>core exportcards [path] [scale]</c>
    /// </summary>
    public sealed class CoreCardExportConsoleCmd : AbstractConsoleCmd
    {
        private static readonly string[] Actions = ["exportcards", "exportjson"];
        private static int _running;

        public override string CmdName => "core";
        public override string Args => "exportcards|exportjson [path] [scale]";
        public override string Description => "Export all card images and game info (cards/relics/monsters/potions). Default path: user://CoreCardExports/, default scale: 1.";
        public override bool IsNetworked => false;

        public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
        {
            if (args.Length <= 1)
            {
                var partial = args.Length == 0 ? string.Empty : args[0];
                return CompleteArgument(Actions, [], partial, CompletionType.Subcommand);
            }
            return base.GetArgumentCompletions(player, args);
        }

        public override CmdResult Process(Player? issuingPlayer, string[] args)
        {
            if (args.Length == 0 || !Actions.Contains(args[0], StringComparer.OrdinalIgnoreCase))
                return new(false, "Usage: core exportcards [path] [scale]");

            var path = "user://CoreCardExports/";
            var scale = 1f;

            if (args.Length >= 2)
                path = args[1];

            if (args.Length >= 3 && float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                scale = s;

            if (!float.IsFinite(scale) || scale < 0.25f || scale > 4f) return new(false, "Scale must be between 0.25 and 4.");

            if (!CardPngExportUtil.CanExport(out var err))
                return new(false, $"Cannot export: {err}");

            if (Interlocked.Exchange(ref _running, 1) != 0) return new(false, "Export already running.");
            _ = RunExportAsync(path, scale, args[0].Equals("exportjson", StringComparison.OrdinalIgnoreCase));
            return new(true, $"card export started (includes game info). Path: {path}, Scale: {scale}. Check game log for progress.");
        }

        private static async System.Threading.Tasks.Task RunExportAsync(string path, float scale, bool jsonOnly)
        {
            try
            {
                void Log(string message) => ExportsModule.Context.Diagnostics.Report(ShoujoKageki.Core.V1.DiagnosticSeverity.Info, "exports", "progress", message);
                if (jsonOnly) await CardPngExportUtil.ExportJsonOnlyAsync(path, Log);
                else await CardPngExportUtil.ExportAllCardsAsync(path, scale, Log);
            }
            catch (Exception error)
            {
                ExportsModule.Context.Diagnostics.Report(ShoujoKageki.Core.V1.DiagnosticSeverity.Warning, "exports", "failed", error.GetType().Name);
            }
            finally { Volatile.Write(ref _running, 0); }
        }
    }
}
