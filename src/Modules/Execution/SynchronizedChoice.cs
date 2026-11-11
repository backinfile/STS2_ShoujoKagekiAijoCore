namespace ShoujoKageki.Core.Runtime.Modules.Execution;

/// <summary>One choice ID and one validated result, independent of the local presentation.</summary>
internal static class SynchronizedChoice
{
    internal static async Task<int> ChooseIndex(int optionCount, bool isLocal, Func<uint> reserve,
        Func<Task<int>> selectLocal, Action<uint, int> publish, Func<uint, Task<int>> receive)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(optionCount);
        if (optionCount == 0) return -1;
        uint id = reserve();
        int index = isLocal ? await selectLocal() : await receive(id);
        if (index < -1 || index >= optionCount)
            throw new InvalidOperationException("Player choice is outside the offered options.");
        if (isLocal) publish(id, index);
        return index;
    }
}
