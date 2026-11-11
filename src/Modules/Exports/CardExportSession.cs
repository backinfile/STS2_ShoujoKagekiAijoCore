using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace ShoujoKageki.Core.Exports;

/// <summary>Owns snapshots, detached construction, rendering and teardown for one capture.</summary>
internal sealed class CardExportSession : IAsyncDisposable
{
    private readonly NGame _game;
    private readonly Control _host;
    private readonly List<Node> _nodes = [];
    private readonly List<CardModel> _models = [];
    private readonly List<NCard> _cardViews = [];
    private bool _disposed;

    internal CardExportSession(NGame game)
    {
        _game = game;
        _host = Own(new Control { Name = "CardExportHost", Position = new Vector2(-5000, -5000) });
    }

    private T Own<T>(T node) where T : Node
    {
        _nodes.Add(node);
        return node;
    }

    private CardModel Snapshot(CardModel source)
    {
        var card = CoreApi.Get<ICardSnapshots>().Capture(source);
        _models.Add(card);
        return card;
    }

    internal async Task<Image?> CaptureAsync(CardModel source, float scale, bool includeHoverTips)
    {
        var built = BuildViewport(Snapshot(source), scale, includeHoverTips);
        _host.AddChild(built.Viewport);
        _game.AddChild(_host);
        await WaitFrames(FramesAfterHostAdded);
        foreach (var view in _cardViews)
            if (GodotObject.IsInstanceValid(view)) RefreshCardVisuals(view);
        await WaitFrames(FramesAfterVisualApply);
        if (built.HoverRow != null && GodotObject.IsInstanceValid(built.HoverRow))
        {
            built.HoverRow.QueueSort();
            await WaitFrames(FramesAfterHoverLayout);
            ResizeViewportToHoverRow(built);
            await WaitFrames(FramesAfterHoverResize);
        }
        if (!GodotObject.IsInstanceValid(built.Viewport)) return null;
        built.Viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        var rendered = _game.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        RenderingServer.ForceDraw(swapBuffers: false);
        await rendered;
        if (!GodotObject.IsInstanceValid(built.Viewport)) return null;
        var image = built.Viewport.GetTexture()?.GetImage();
        if (image == null) return null;
        if (!image.IsEmpty() && !image.IsInvisible()) return image;
        image.Dispose();
        throw new InvalidOperationException("Card export produced no visible pixels.");
    }

    private async Task WaitFrames(int count)
    {
        if (!GodotObject.IsInstanceValid(_game)) return;
        var tree = _game.GetTree();
        if (tree == null) return;
        for (int i = 0; i < count; i++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try { await WaitFrames(FramesBeforeTeardown); }
        finally
        {
            var failures = new List<Exception>();
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                try { if (GodotObject.IsInstanceValid(_nodes[i])) _nodes[i].Free(); }
                catch (Exception error) { failures.Add(error); }
            }
            _nodes.Clear();
            _models.Clear();
            _cardViews.Clear();
            if (failures.Count > 0) throw new AggregateException("Card export teardown failed.", failures);
        }
    }

    // ---- 场景路径（原版游戏路径） ----
    private const string CardScenePath = "res://scenes/cards/card.tscn";
    private const string HoverTipScenePath = "res://scenes/ui/hover_tip.tscn";
    private const string CardHoverTipScenePath = "res://scenes/ui/card_hover_tip.tscn";
    private const string HoverTipDebuffMaterialPath = "res://materials/ui/hover_tip_debuff.tres";

    // ---- 尺寸常量 ----
    private const float CardHalfExtentX = 190f;
    private const float CardHalfExtentY = 240f;
    private const float FramePad = 6f;
    private const float HoverTipWidth = 360f;
    private const float HoverTipGap = 5f;
    private const int HoverColumnSeparation = 0;
    private const int RefCardHoverGap = 4;
    private const float RefCardScale = 0.75f;

    // ---- 帧等待常量 ----
    private const int FramesAfterHostAdded = 2;
    private const int FramesAfterVisualApply = 2;
    private const int FramesAfterHoverLayout = 2;
    private const int FramesAfterHoverResize = 2;
    private const int FramesBeforeTeardown = 1;

    // ==================== 私有实现 ====================

    private BuiltViewport BuildViewport(CardModel card, float scale, bool includeHoverTips)
    {
        var (cardW, cardH) = ComputeCardSize(scale);
        var vp = Own(new SubViewport
        {
            TransparentBg = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        });
        var root = Own(new Control { Name = "ExportRoot" });

        if (!includeHoverTips)
        {
            var frame = Mathf.RoundToInt(FramePad);
            var vpW = cardW + frame * 2;
            var vpH = cardH + frame * 2;
            vp.Size = new Vector2I(vpW, vpH);
            root.CustomMinimumSize = new Vector2(vpW, vpH);
            root.Size = new Vector2(vpW, vpH);

            var mainNCard = CreateNCard(card, scale, new Vector2(frame, frame));
            root.AddChild(mainNCard);
            vp.AddChild(root);

            return new BuiltViewport(vp, root, null);
        }

        // 含 HoverTips 模式：横向排列 [参考卡牌列 | 主卡牌 | 文本提示列]
        var pad = Mathf.RoundToInt(FramePad);
        var cardSlotW = cardW;
        var initW = cardSlotW + 800;
        var initH = cardH + pad * 2 + 120;
        vp.Size = new Vector2I(initW, initH);
        root.CustomMinimumSize = new Vector2(initW, initH);
        root.Size = new Vector2(initW, initH);

        var row = Own(new HBoxContainer
        {
            Position = new Vector2(pad, pad),
            Name = "HoverRow",
        });
        row.AddThemeConstantOverride("separation", HoverColumnSeparation);
        root.AddChild(row);

        var refCardsColumn = Own(new VBoxContainer { Name = "RefCardsColumn" });
        refCardsColumn.AddThemeConstantOverride("separation", RefCardHoverGap);
        row.AddChild(refCardsColumn);

        var cardSlot = Own(new Control
        {
            CustomMinimumSize = new Vector2(cardSlotW, cardH),
            Name = "CardSlot",
        });
        row.AddChild(cardSlot);
        var nCard = CreateNCard(card, scale, Vector2.Zero);
        cardSlot.AddChild(nCard);

        var textTipsColumn = Own(new VBoxContainer { Name = "TextTipsColumn" });
        textTipsColumn.AddThemeConstantOverride("separation", Mathf.RoundToInt(HoverTipGap));
        row.AddChild(textTipsColumn);

        PopulateHoverTips(refCardsColumn, textTipsColumn, card);

        vp.AddChild(root);
        return new BuiltViewport(vp, root, row);
    }

    private NCard CreateNCard(CardModel card, float scale, Vector2 topLeft)
    {
        var scene = ResourceLoader.Load<PackedScene>(CardScenePath);
        var nCard = Own(scene.Instantiate<NCard>());
        nCard.OnInstantiated();
        BindCard(nCard, card);
        nCard.Visible = true;

        nCard.Scale = Vector2.One * scale;
        var minLocal = new Vector2(-CardHalfExtentX, -CardHalfExtentY);
        nCard.Position = new Vector2(
            Mathf.Round(topLeft.X - minLocal.X * scale),
            Mathf.Round(topLeft.Y - minLocal.Y * scale));
        return nCard;
    }

    private void BindCard(NCard node, CardModel card)
    {
        node.Model = card;
        _cardViews.Add(node);
    }

    private static void RefreshCardVisuals(NCard nCard)
    {
        nCard.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
        if (nCard.Model is { IsUpgraded: true })
            nCard.ShowUpgradePreview();
    }

    private void PopulateHoverTips(VBoxContainer refCardsColumn, VBoxContainer textTipsColumn, CardModel card)
    {
        var tips = card.HoverTips;
        foreach (var tip in tips)
        {
            switch (tip)
            {
                case HoverTip hoverTip:
                    AddTextTip(textTipsColumn, hoverTip);
                    break;
                case CardHoverTip cardTip:
                    AddCardTip(refCardsColumn, cardTip);
                    break;
            }
        }
    }

    private void AddTextTip(VBoxContainer column, HoverTip tip)
    {
        var scene = ResourceLoader.Load<PackedScene>(HoverTipScenePath);
        var control = Own(scene.Instantiate<Control>());
        column.AddChild(control);

        var title = control.GetNode<MegaLabel>("%Title");
        if (tip.Title == null)
            title.Visible = false;
        else
            title.SetTextAutoSize(tip.Title);

        var desc = control.GetNode<MegaRichTextLabel>("%Description");
        desc.Text = tip.Description;
        desc.AutowrapMode = tip.ShouldOverrideTextOverflow
            ? TextServer.AutowrapMode.Off
            : TextServer.AutowrapMode.WordSmart;

        var icon = control.GetNode<TextureRect>("%Icon");
        if (icon != null)
            icon.Texture = tip.Icon;

        if (tip.IsDebuff)
        {
            var bg = control.GetNode<CanvasItem>("%Bg");
            if (bg != null)
                bg.Material = ResourceLoader.Load<Material>(HoverTipDebuffMaterialPath);
        }

        control.CustomMinimumSize = new Vector2(HoverTipWidth, 0f);
        control.ResetSize();
    }

    private void AddCardTip(VBoxContainer column, CardHoverTip tip)
    {
        var scene = ResourceLoader.Load<PackedScene>(CardHoverTipScenePath);
        var control = Own(scene.Instantiate<Control>());
        column.AddChild(control);

        var (paddedW, paddedH) = ComputeCardSize(RefCardScale);
        control.CustomMinimumSize = new Vector2(paddedW, paddedH);
        control.ResetSize();

        var node = control.GetNode<NCard>("%Card");
        if (tip.Card != null) BindCard(node, Snapshot(tip.Card));
        node.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        node.Scale = Vector2.One * RefCardScale;
        var minLocal = new Vector2(-CardHalfExtentX, -CardHalfExtentY);
        node.Position = new Vector2(
            Mathf.Round(-minLocal.X * RefCardScale),
            Mathf.Round(-minLocal.Y * RefCardScale));
    }

    private static void ResizeViewportToHoverRow(BuiltViewport built)
    {
        if (built.HoverRow == null || !GodotObject.IsInstanceValid(built.HoverRow))
            return;
        if (!GodotObject.IsInstanceValid(built.Viewport) || !GodotObject.IsInstanceValid(built.Root))
            return;

        var pad = Mathf.RoundToInt(FramePad);
        var sz = built.HoverRow.GetCombinedMinimumSize();
        var w = Mathf.CeilToInt(pad * 2 + sz.X);
        var h = Mathf.CeilToInt(pad * 2 + sz.Y);
        built.Viewport.Size = new Vector2I(w, h);
        built.Root.CustomMinimumSize = new Vector2(w, h);
        built.Root.Size = new Vector2(w, h);
    }

    private static (int w, int h) ComputeCardSize(float scale)
    {
        var w = Mathf.CeilToInt(2f * CardHalfExtentX * scale);
        var h = Mathf.CeilToInt(2f * CardHalfExtentY * scale);
        return (w, h);
    }

    private sealed class BuiltViewport(
        SubViewport viewport,
        Control root,
        HBoxContainer? hoverRow)
    {
        public SubViewport Viewport { get; } = viewport;
        public Control Root { get; } = root;
        public HBoxContainer? HoverRow { get; } = hoverRow;
    }
}
