using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Godot;

namespace ShoujoKageki.Core.Telemetry;

internal static class LegacyQueueMigration
{
    internal static void Import()
    {
        string source = ProjectSettings.GlobalizePath("user://karen-telemetry");
        string target = Path.Combine(TelemetryModule.Context.DataDirectory, "telemetry");
        foreach (string queue in new[] { "pending", "errors" })
        {
            string root = Path.Combine(source, queue);
            if (!Directory.Exists(root)) continue;
            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                if (!Regex.IsMatch(Path.GetFileName(directory), "^[A-Fa-f0-9]{16}$") ||
                    (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                string destination = Path.Combine(target, queue, Path.GetFileName(directory));
                Directory.CreateDirectory(destination);
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    if (!Regex.IsMatch(Path.GetFileName(file), "^[A-Fa-f0-9]{64}\\.(json|rejected)$") ||
                        (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                    string migrated = Path.Combine(destination, Path.GetFileName(file));
                    if (!File.Exists(migrated)) File.Move(file, migrated);
                    else if (SameContents(file, migrated)) File.Delete(file);
                }
            }
        }
    }
    private static bool SameContents(string left, string right)
    {
        using var first = File.OpenRead(left);
        using var second = File.OpenRead(right);
        return first.Length == second.Length && SHA256.HashData(first).AsSpan().SequenceEqual(SHA256.HashData(second));
    }
}
