using System;
using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Timeline;

namespace ShoujoKageki.Core.Runtime.Modules.Configuration;

/// <summary>One explicit, local-profile action. Existing run/achievement/statistics data is retained.</summary>
public static class CoreUnlockAll
{
    public static string Apply()
    {
        var save = SaveManager.Instance;
        if (!save.IsProfileInitialized)
            throw new InvalidOperationException("No active save profile.");

        // Keep the pre-action progress separately from the game's rotating backup.
        save.SaveProgressFile();
        string progressPath = ProjectSettings.GlobalizePath(save.GetProfileScopedPath(
            Path.Combine(UserDataPathProvider.SavesDir, "progress.save")));
        string backupPath = progressPath + ".core-unlock-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".backup";
        File.Copy(progressPath, backupPath, overwrite: false);

        var progress = save.Progress;
        foreach (var card in ModelDb.AllCards) progress.MarkCardAsSeen(card.Id);
        foreach (var relic in ModelDb.AllRelics) progress.MarkRelicAsSeen(relic.Id);
        foreach (var potion in ModelDb.AllPotions) progress.MarkPotionAsSeen(potion.Id);
        foreach (string id in EpochModel.AllEpochIds)
            save.ObtainEpochOverride(id, EpochState.Revealed);
        foreach (var character in ModelDb.AllCharacters)
        {
            var stats = progress.GetOrCreateCharacterStats(character.Id);
            stats.MaxAscension = Math.Max(stats.MaxAscension, AscensionManager.maxAscensionAllowed);
        }
        progress.MaxMultiplayerAscension = Math.Max(progress.MaxMultiplayerAscension, AscensionManager.maxAscensionAllowed);
#if STS2_BETA
        progress.MaxBlitzAscension = Math.Max(progress.MaxBlitzAscension, AscensionManager.maxAscensionAllowedBlitz);
#endif
        save.SaveProgressFile();
        return Path.GetFileName(backupPath);
    }
}
