using Godot;
using System;
using System.Reflection;
using ShoujoKageki.Core.Runtime.Modules.Presentation;

// The production border only uses the inherited Control surface on this path.
namespace MegaCrit.Sts2.Core.Nodes.Cards { public partial class NCard : Control { } }

public partial class Probe : Node
{
 public override void _Ready()
 {
  try
  {
   using var atlas = new AtlasTexture();
   ShineCardBorder.Prepare(atlas);
   GD.Print("PASS unavailable atlas does not interrupt preparation");
   using var image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
   image.Fill(Colors.White);
   using var texture = ImageTexture.CreateFromImage(image);
   atlas.Atlas = texture;
   atlas.Region = new Rect2(0, 0, 8, 8);
   ShineCardBorder.Prepare(atlas);
   var read = typeof(ShineCardBorder).GetMethod("GetContour", BindingFlags.Static | BindingFlags.NonPublic)!;
   if (((Vector2[])read.Invoke(null, new object[] { atlas })!).Length == 0) throw new Exception("Prepared atlas must retry when its source becomes available");
   GD.Print("PASS initially unavailable atlas recovers on the same resource");
   using var fullAtlas = new AtlasTexture { Atlas = texture };
   ShineCardBorder.Prepare(fullAtlas);
   if (((Vector2[])read.Invoke(null, new object[] { fullAtlas })!).Length == 0) throw new Exception("Default region must use the complete source");
   GD.Print("PASS zero-sized atlas region follows Godot dimensions");
   using var nestedSource = new AtlasTexture();
   using var nestedAtlas = new AtlasTexture { Atlas = nestedSource };
   ShineCardBorder.Prepare(nestedAtlas);
   nestedSource.Atlas = texture;
   ShineCardBorder.Prepare(nestedAtlas);
   if (((Vector2[])read.Invoke(null, new object[] { nestedAtlas })!).Length == 0) throw new Exception("Nested atlas must recover when its source becomes available");
   GD.Print("PASS nested unavailable atlas recovers without native image errors");
   GetTree().Quit();
  }
  catch (Exception error) { GD.Print("FAIL " + error); GetTree().Quit(1); }
 }
}
