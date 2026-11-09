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
    private static readonly Dictionary<Assembly, Mod?> Owners = new();
    private static readonly Dictionary<Type, bool> CharacterFamilies = new();
    public void Refresh()
    {
        Owners.Clear();
        CharacterFamilies.Clear();
        Mods = Array.AsReadOnly(ModManager.Mods.Where(x => x.manifest?.id != null && x.state is ModLoadState.None or ModLoadState.Loaded or ModLoadState.Failed).Select(x => new CoreModInfo(x.manifest!.id!,
            x.manifest.version ?? "", x.modSource.ToString(), x.state == ModLoadState.Loaded)).ToArray());
        if (ModManager.State == ModManagerState.None) return;
        if (AccessTools.Field(typeof(ModelDb), "_contentById").GetValue(null) is not IDictionary content || content.Count == 0) return;
        var owners = new Dictionary<Assembly, Mod?>();
        Mod? Owner(Assembly assembly)
        {
            if (!owners.TryGetValue(assembly, out var owner)) owners[assembly] = owner = OwnerOf(assembly);
            return owner;
        }
        Models = Array.AsReadOnly(content.Values.Cast<AbstractModel>().Select(model =>
        {
            var type = model.GetType();
            var mod = Owner(type.Assembly);
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
    private static Mod? OwnerOf(Assembly assembly)
    {
        if (Owners.TryGetValue(assembly, out var cached)) return cached;
        var owner = FindOwner(assembly);
        Owners[assembly] = owner;
        return owner;
    }
    private static Mod? FindOwner(Assembly assembly)
    {
        var associated = ModManager.Mods.FirstOrDefault(mod => Assemblies(mod).Contains(assembly));
        if (associated != null || assembly.IsDynamic || string.IsNullOrEmpty(assembly.Location)) return associated;
        // Stable exposes only the loader assembly. Its implementation stays inside the mod directory.
        var location = Path.GetFullPath(assembly.Location);
        return ModManager.Mods.Where(mod => !string.IsNullOrEmpty(mod.path))
            .OrderByDescending(mod => Path.GetFullPath(mod.path).Length)
            .FirstOrDefault(mod => location.StartsWith(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(mod.path)) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
    }
    public static bool IsShoujoCharacter(CharacterModel? character)
    {
        if (character == null) return false;
        var type = character.GetType();
        if (CharacterFamilies.TryGetValue(type, out var cached)) return cached;
        var isShoujo = ReadCharacterFamily(type);
        CharacterFamilies[type] = isShoujo;
        return isShoujo;
    }
    private static bool ReadCharacterFamily(Type type)
    {
        var owner = OwnerOf(type.Assembly);
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
