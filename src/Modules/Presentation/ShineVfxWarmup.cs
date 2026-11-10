using System.Reflection;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes;
using ShoujoKageki.Core.V1.Presentation;

namespace ShoujoKageki.Core.Runtime.Modules.Presentation;

internal static partial class ShineVfxWarmup
{
    private static Task? _pending;
    private static bool _failed;
    private static readonly GradientTexture2D White = new()
    {
        Width = 2, Height = 2,
        Gradient = new Gradient { Colors = [Colors.White, Colors.White] }
    };

    internal static Task EnsureReady()
    {
        if (_pending == null || _failed) _pending = Prepare();
        return _pending;
    }

    private static async Task Prepare()
    {
        _failed = false;
        SubViewport? viewport = null;
        try
        {
            PrepareVisualMethods();
            ShineCardBorder.PrepareNativeFrames();
            LightRibbon.PrepareResources();
            if (DisplayServer.GetName() == "headless") return;
            viewport = new SubViewport
            {
                Name = "CoreShineWarmup", Size = new Vector2I(128, 128), Disable3D = true,
                TransparentBg = true, GuiDisableInput = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always
            };
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(viewport);
            viewport.AddChild(new WarmupCanvas());
            viewport.AddChild(new WarmupCanvas { Material = ShineVfx.AdditiveMaterial });
            await CoreEffects.DrawFrame(viewport);
            await CoreEffects.DrawFrame(viewport);
            if (viewport.GetRenderInfo(Viewport.RenderInfoType.Canvas, Viewport.RenderInfo.DrawCallsInFrame) <= 0)
                throw new InvalidOperationException("Core warmup viewport did not draw.");
            CoreLog.Logger.Info("[ShineVfxWarmup] Core contours and canvas variants ready.");
        }
        catch (Exception error)
        {
            CoreLog.Logger.Warn("[ShineVfxWarmup] " + error);
            _failed = true;
        }
        finally { if (GodotObject.IsInstanceValid(viewport)) viewport!.Free(); }
    }

    private static void PrepareVisualMethods()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported) return;
        foreach (var type in new[] { typeof(ShineVfx), typeof(ShineCardBorder), typeof(NKarenShineAmbientVfx),
            typeof(NKarenShineWarningVfx), typeof(NKarenShineDepleteVfx), typeof(NKarenShinePileArrivalVfx),
            typeof(NKarenPileViewVfx), typeof(CanvasGeometryBatch), typeof(LightRibbon) })
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() == null) continue;
                try { RuntimeHelpers.PrepareMethod(method.MethodHandle); }
                catch (PlatformNotSupportedException) { return; }
            }
        }
    }

    [HarmonyPatch]
    private static class LoadingPatch
    {
        private static IEnumerable<MethodBase> TargetMethods() => new[] { "LoadMainMenuAssets", "LoadMainMenuEssentials", "LoadRunAssets" }
            .Select(name => AccessTools.Method(typeof(PreloadManager), name)).Where(method => method != null);
        private static void Postfix(ref Task __result) => __result = Complete(__result);
        private static async Task Complete(Task nativeLoad)
        {
            await nativeLoad;
            await EnsureReady();
        }
    }

    private partial class WarmupCanvas : Node2D
    {
        public override void _Draw()
        {
            var batch = ShineVfx.DrawBatch;
            batch.Clear();
            // Reserve the shared scratch geometry before visible card overlays use it.
            for (int i = 0; i < 100; i++) batch.Circle(new Vector2(64, 64), 8, Colors.White);
            batch.Line(Vector2.Zero, Vector2.One * 32, Colors.White, 2);
            batch.Star(new Vector2(64, 64), 10, .2f, Colors.White);
            batch.Flush(this);
            DrawTextureRect(White, new Rect2(0, 0, 2, 2), false);
            DrawLine(Vector2.Zero, Vector2.One * 32, Colors.White, 2, true);
            DrawArc(new Vector2(64, 64), 12, 0, Mathf.Tau, 40, Colors.White, 2, true);
            LightRibbon.Star(this, new Vector2(64, 64), 10, Colors.White, 1);
        }
    }
}
