using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;

namespace CoreObservationProbe;

[ModInitializer(nameof(Initialize))]
public static class Startup
{
 internal static string Output = "";
 public static void Initialize()
 {
  Output = Path.Combine(OS.GetUserDataDir(), "core-observations.jsonl");
  OS.AddLogger(new ObservationLogger());
  if (System.Environment.GetEnvironmentVariable("CORE_OBSERVATION_DRAW") == "1")
  {
   var tree = (SceneTree)Engine.GetMainLoop();
   RenderingServer.ViewportSetUpdateMode(tree.Root.GetViewportRid(), RenderingServer.ViewportUpdateMode.Always);
   tree.ProcessFrame += () => RenderingServer.ForceDraw(false);
  }
 }
}

public partial class ObservationLogger : Logger
{
 public override void _LogError(string function, string file, int line, string code, string rationale,
  bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
 {
  if (!(code + rationale).Contains("Invalid Task ID", StringComparison.Ordinal)) return;
  File.AppendAllText(Startup.Output, JsonSerializer.Serialize(new {
   function, file, line, code, rationale, thread = System.Environment.CurrentManagedThreadId,
   managedStack = System.Environment.StackTrace
  }) + "\n");
 }
}

public sealed class ObservationCommand : AbstractConsoleCmd
{
 public override string CmdName => "core_observations";
 public override string Args => "border";
 public override string Description => "Run targeted Core texture checks.";
 public override bool IsNetworked => false;
 public override CmdResult Process(Player? player, string[] args)
 {
  var runtime = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name?.StartsWith("ShoujoKagekiAijoCore.Runtime.") == true);
  var type = runtime.GetType("ShoujoKageki.Core.Runtime.Modules.Presentation.ShineCardBorder", true)!;
  var prepare = type.GetMethod("Prepare", BindingFlags.NonPublic | BindingFlags.Static, new[] { typeof(Texture2D) })!;
  var contour = type.GetMethod("GetContour", BindingFlags.NonPublic | BindingFlags.Static)!;
  using var atlas = new AtlasTexture();
  prepare.Invoke(null, new object[] { atlas });
  using var image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
  image.Fill(Colors.White);
  using var texture = ImageTexture.CreateFromImage(image);
  atlas.Atlas = texture;
  prepare.Invoke(null, new object[] { atlas });
  int points = ((Vector2[])contour.Invoke(null, new object[] { atlas })!).Length;
  return new CmdResult(points > 0, JsonSerializer.Serialize(new { points, recovered = points > 0 }));
 }
}
