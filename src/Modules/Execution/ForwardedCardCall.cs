using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace ShoujoKageki.Core.Runtime.Modules.Execution;

/// <summary>Keep foreign forwarding code and adapt its unique underlying card call.</summary>
internal static class ForwardedCardCall
{
    private sealed record Site(MethodBase Body, MethodInfo Call);
    private sealed record Redirect(MethodInfo From, MethodInfo To);
    private static readonly Dictionary<MethodBase, Redirect> Redirects = [];
    private static readonly Harmony Patcher = new(V1.CoreApi.ModId + ".ForwardedCardCalls");

    internal static bool Adapt(MethodInfo bridge, Func<MethodInfo, bool> matches, MethodInfo? replacement)
    {
        // A dynamic or opaque dispatcher must still fail the caller's boundary check.
        var sites = Find(bridge, call => matches(call) || call == replacement, [], 0).Take(2).ToArray();
        if (sites.Length != 1) return false;
        var site = sites[0];
        if (replacement == null || site.Call == replacement) return true;
        lock (Redirects)
        {
            if (Redirects.TryGetValue(site.Body, out var existing))
                return existing.From == site.Call && existing.To == replacement;
            Redirects.Add(site.Body, new(site.Call, replacement));
            try
            {
                Patcher.Patch(site.Body, transpiler: new HarmonyMethod(typeof(ForwardedCardCall), nameof(Transpiler))
                    { priority = int.MinValue });
            }
            catch { Redirects.Remove(site.Body); throw; }
        }
        return true;
    }

    private static IEnumerable<Site> Find(MethodInfo method, Func<MethodInfo, bool> matches,
        HashSet<MethodBase> path, int depth)
    {
        var assembly = method.Module.Assembly;
        if (depth >= 6 || method.ReturnType != typeof(Task) || method.ContainsGenericParameters || method.IsAbstract
            || assembly == typeof(CardModel).Assembly || assembly == typeof(ForwardedCardCall).Assembly
            || assembly == typeof(Task).Assembly || assembly.GetName().Name?.StartsWith("System.", StringComparison.Ordinal) == true)
            yield break;
        MethodBase body = method.GetCustomAttribute<AsyncStateMachineAttribute>() != null
            ? AccessTools.AsyncMoveNext(method) : method;
        if (!path.Add(body) || body.GetMethodBody() == null) yield break;
        try
        {
            foreach (var instruction in PatchProcessor.GetCurrentInstructions(body))
            {
                if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt) continue;
                if (instruction.operand is not MethodInfo call) continue;
                if (matches(call)) yield return new Site(body, call);
                else foreach (var site in Find(call, matches, path, depth + 1)) yield return site;
            }
        }
        finally { path.Remove(body); }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var redirect = Redirects[__originalMethod];
        int calls = 0;
        foreach (var instruction in instructions)
        {
            if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
                && instruction.operand is MethodInfo method)
            {
                if (method == redirect.From)
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = redirect.To;
                    calls++;
                }
                else if (Adapt(method, call => call == redirect.From, redirect.To)) calls++;
            }
            yield return instruction;
        }
        if (calls != 1) throw new InvalidOperationException($"Core forwarded card boundary {__originalMethod}: calls={calls}");
    }
}
