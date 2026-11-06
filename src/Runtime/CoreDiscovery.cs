using System.Reflection;
using System.Text.Json;
using System.Collections;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using ShoujoKageki.Core.V1;

namespace ShoujoKageki.Core.Runtime;

public sealed class CoreDiscovery : ICoreDiscovery
{
    public IReadOnlyList<CoreModInfo> Mods { get; private set; } = [];
    public IReadOnlyList<CoreModelInfo> Models { get; private set; } = [];
    public event Action? ModelsChanged;
    public void Refresh()
    {
        Mods = Array.AsReadOnly(ModManager.Mods.Where(x => x.manifest?.id != null && x.state is ModLoadState.None or ModLoadState.Loaded or ModLoadState.Failed).Select(x => new CoreModInfo(x.manifest!.id!,
            x.manifest.version ?? "", x.modSource.ToString(), x.state == ModLoadState.Loaded)).ToArray());
        if (ModManager.State == ModManagerState.None) return;
        if (AccessTools.Field(typeof(ModelDb), "_contentById").GetValue(null) is not IDictionary content || content.Count == 0) return;
        Models = Array.AsReadOnly(content.Values.Cast<AbstractModel>().Select(model =>
        {
            var type = model.GetType();
            var mod = ModManager.Mods.FirstOrDefault(m => Assemblies(m).Contains(type.Assembly));
            var kind = model switch { CardModel => "card", CharacterModel => "character", CardPoolModel => "cardPool", RelicModel => "relic", _ => type.BaseType?.Name ?? "model" };
            var coreOwned = type.Assembly == typeof(RuntimeEntry).Assembly;
            return new CoreModelInfo(model.Id.ToString(), kind, coreOwned ? CoreApi.ModId : mod?.manifest?.id ?? "sts2",
                coreOwned ? RuntimeEntry.Context.ReleaseVersion : mod?.manifest?.version ?? RuntimeEntry.Context.GameVersion, type);
        }).OrderBy(x => x.Id, StringComparer.Ordinal).ToArray());
        ModelsChanged?.Invoke();
    }
    internal static IEnumerable<Assembly> Assemblies(Mod mod)
    {
        if (AccessTools.Field(typeof(Mod), "assemblies")?.GetValue(mod) is IEnumerable<Assembly> assemblies) return assemblies;
        return AccessTools.Field(typeof(Mod), "assembly")?.GetValue(mod) is Assembly assembly ? [assembly] : [];
    }
    public static bool IsShoujoCharacter(CharacterModel? character)
    {
        if (character == null) return false;
        var owner = ModManager.Mods.FirstOrDefault(m => Assemblies(m).Contains(character.GetType().Assembly));
        if (owner?.manifest?.id is not { } modId) return false;
        if (modId is "ShoujoKagekiAijoKaren" or "ShoujoKagekiJunna") return true;
        var requirements = Path.Combine(owner.path, "core.requirements");
        if (!File.Exists(requirements)) return false;
        try
        {
            using var metadata = JsonDocument.Parse(File.ReadAllText(requirements));
            return metadata.RootElement.TryGetProperty("characterFamily", out var family) && family.GetString() == "ShoujoKageki";
        }
        catch (Exception error) when (error is IOException or JsonException) { return false; }
    }
}
