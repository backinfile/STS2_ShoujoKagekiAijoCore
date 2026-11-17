using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ShoujoKageki.Core.Runtime.Modules.Execution;

internal static class BoundaryDiagnostics
{
    internal static InvalidOperationException Mismatch(string message, MethodBase target,
        IEnumerable<CodeInstruction> instructions)
    {
        var patches = Harmony.GetPatchInfo(target)?.Transpilers
            .Select(patch => $"{patch.owner}:{Describe(patch.PatchMethod)}:priority={patch.priority}") ?? [];
        var calls = instructions.Where(instruction => instruction.opcode == OpCodes.Call
                || instruction.opcode == OpCodes.Callvirt || instruction.opcode == OpCodes.Calli)
            .Select(instruction => instruction.operand is MethodBase method ? Describe(method) : instruction.ToString())
            .Distinct().Take(48);
        return new InvalidOperationException($"{message} Target={Describe(target)}; " +
            $"transpilers=[{string.Join("; ", patches)}]; calls=[{string.Join("; ", calls)}]; " +
            $"coreFingerprint={RuntimeEntry.Fingerprint}");
    }

    private static string Describe(MethodBase method)
        => $"{method.DeclaringType?.FullName}.{method.Name}" +
            $" ({method.Module.Assembly.GetName().Name}, mvid={method.Module.ModuleVersionId})";
}
