using System.Reflection;
using MegaCrit.Sts2.Core.Modding;
using ShoujoKageki.Core.Bootstrap;

if (args.Length != 1) throw new ArgumentException("Pass a directory junction to this check's build-output directory.");
var initialize = typeof(Bootstrap).GetMethod("InitializeOnce", BindingFlags.NonPublic | BindingFlags.Static)!;
var logical = typeof(Bootstrap).GetField("_logicalMod", BindingFlags.NonPublic | BindingFlags.Static)!;
var directory = Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location)!;
var alias = Path.GetFullPath(args[0]);
if (!File.Exists(Path.Combine(alias, Path.GetFileName(typeof(Bootstrap).Assembly.Location))))
    throw new ArgumentException("The alias must point to the actual bootstrap check assembly directory.");
var checks = 0;
var failures = 0;
Mod Core(string root, ModLoadState state = ModLoadState.None) =>
    new() { path = root, state = state, manifest = new() { id = Bootstrap.ModId } };

void Check(string name, Mod[] mods, Mod? expected) {
    logical.SetValue(null, null);
    ModManager._mods = mods.ToList();
    Exception? observed = null;
    try { initialize.Invoke(null, null); }
    catch (TargetInvocationException error) { observed = error.InnerException; }
    var actual = (Mod?)logical.GetValue(null);
    bool ok = expected == null
        ? actual == null && observed?.Message == "Cannot identify the active Core mod entry."
        : ReferenceEquals(expected, actual) && observed?.Message.Contains("BaseLib") == true;
    if (!ok) {
        failures++;
        Console.WriteLine($"FAIL {name}: {observed?.GetType().Name}: {observed?.Message}");
    } else {
        checks++;
        Console.WriteLine($"PASS {name}");
    }
}

var exact = Core(directory);
Check("desktop assembly directory", [exact], exact);
var trailing = Core(directory + Path.DirectorySeparatorChar);
Check("same directory with trailing separator", [trailing], trailing);
var androidAlias = Core(alias);
Check("same bootstrap through a filesystem directory alias", [androidAlias], androidAlias);
var disabled = Core(alias, ModLoadState.Disabled);
Check("disabled Core is never activated", [disabled], null);
var failed = Core(alias, ModLoadState.Failed);
Check("failed Core is never activated", [failed], null);
var duplicate = Core(alias, ModLoadState.DisabledDuplicate);
Check("exact match wins when an eligible duplicate exists", [duplicate, exact], exact);
Check("ambiguous entries without a matching assembly directory are rejected", [Core(alias), Core(alias + Path.DirectorySeparatorChar)], null);
Check("missing Core is rejected", [], null);
Console.WriteLine($"Bootstrap checks: {checks} passed, {failures} failed.");
return failures == 0 ? 0 : 1;
