using System;
using System.Text;
using Godot;

namespace ShoujoKageki.Core.Telemetry;

internal partial class CoreErrorLogger : Godot.Logger
{
    public override void _LogMessage(string message, bool error)
    {
        if (!error || !TelemetryModule.ErrorUploadEnabled) return;
        string bounded = message.Length > 16384 ? message[..16384] : message;
        int newline = bounded.IndexOf('\n');
        CoreErrorReporter.Capture(bounded.StartsWith("[ERROR]", StringComparison.Ordinal) ? "game" : "stderr",
            newline < 0 ? bounded : bounded[..newline], newline < 0 ? "" : bounded[(newline + 1)..]);
    }

    public override void _LogError(string function, string file, int line, string code, string rationale,
        bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        if (errorType == (int)ErrorType.Warning || !TelemetryModule.ErrorUploadEnabled) return;
        try
        {
            CoreErrorReporter.Capture(errorType == (int)ErrorType.Shader ? "shader" : "engine",
                string.IsNullOrWhiteSpace(rationale) ? code : rationale, "", () =>
            {
                var stack = new StringBuilder(ClientErrorBuffer.Clean(function, 512));
                stack.Append(" at ").Append(ClientErrorBuffer.Clean(file, 512)).Append(':').Append(line);
                for (int i = 0; i < Math.Min(4, scriptBacktraces.Count); i++)
                {
                    var trace = scriptBacktraces[i];
                    for (int frame = 0; frame < Math.Min(16, trace.GetFrameCount()) && stack.Length < 8192; frame++)
                        stack.Append('\n').Append(ClientErrorBuffer.Clean(trace.GetFrameFunction(frame), 512))
                            .Append(" at ").Append(ClientErrorBuffer.Clean(trace.GetFrameFile(frame), 512));
                }
                return stack.ToString();
            });
        }
        catch (Exception) { }
    }
}
