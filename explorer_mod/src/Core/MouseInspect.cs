using Godot;
using System;

namespace GodotExplorer.Core;

/// <summary>
/// Mouse-based node picking. Hover over any game element to highlight it,
/// click to select it in the inspector. Finds the deepest, smallest
/// visible CanvasItem under the cursor.
/// </summary>
public class MouseInspect
{
    private readonly SceneTree _sceneTree;
    private bool _active;

    // Overlay visuals
    private CanvasLayer? _overlayLayer;
    private ColorRect? _clickBlocker;
    private ReferenceRect? _highlightRect;
    private ColorRect? _highlightFill;
    private PanelContainer? _infoBg;
    private Label? _infoLabel;

    private ulong _hoveredNodeId;

    public bool IsActive
    {
        get => _active;
        set
        {
            _active = value;
            if (_overlayLayer != null)
                _overlayLayer.Visible = value;
            if (!value)
                ClearHighlight();
            ActiveChanged?.Invoke(value);
        }
    }

    public event Action<bool>? ActiveChanged;
    public event Action<Node>? NodePicked;

    public MouseInspect(SceneTree sceneTree)
    {
        _sceneTree = sceneTree;
        CreateOverlay();
    }

    private void CreateOverlay()
    {
        _overlayLayer = new CanvasLayer();
        _overlayLayer.Name = "GodotExplorer_MouseInspect";
        _overlayLayer.Layer = 129;
        _overlayLayer.Visible = false;

        // Full-screen transparent blocker to capture all mouse input
        _clickBlocker = new ColorRect();
        _clickBlocker.Name = "ClickBlocker";
        _clickBlocker.Color = new Color(0, 0, 0, 0);
        _clickBlocker.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _clickBlocker.MouseFilter = Control.MouseFilterEnum.Stop;
        _overlayLayer.AddChild(_clickBlocker);

        // Highlight fill
        _highlightFill = new ColorRect();
        _highlightFill.Name = "HighlightFill";
        _highlightFill.Color = new Color(0.3f, 0.5f, 1.0f, 0.1f);
        _highlightFill.MouseFilter = Control.MouseFilterEnum.Ignore;
        _highlightFill.Visible = false;
        _overlayLayer.AddChild(_highlightFill);

        // Highlight border
        _highlightRect = new ReferenceRect();
        _highlightRect.Name = "HighlightBorder";
        _highlightRect.BorderColor = new Color(0.35f, 0.6f, 1.0f, 0.95f);
        _highlightRect.BorderWidth = 2.0f;
        _highlightRect.EditorOnly = false;
        _highlightRect.MouseFilter = Control.MouseFilterEnum.Ignore;
        _highlightRect.Visible = false;
        _overlayLayer.AddChild(_highlightRect);

        // Info tooltip
        _infoBg = new PanelContainer();
        _infoBg.Name = "InfoBg";
        _infoBg.MouseFilter = Control.MouseFilterEnum.Ignore;
        _infoBg.Visible = false;
        var sb = new StyleBoxFlat();
        sb.BgColor = new Color(0.06f, 0.06f, 0.10f, 0.95f);
        sb.SetCornerRadiusAll(4);
        sb.SetBorderWidthAll(1);
        sb.BorderColor = new Color(0.35f, 0.55f, 0.95f, 1f);
        sb.SetContentMarginAll(6);
        _infoBg.AddThemeStyleboxOverride("panel", sb);
        _overlayLayer.AddChild(_infoBg);

        _infoLabel = new Label();
        _infoLabel.Name = "InfoLabel";
        _infoLabel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _infoLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.95f));
        _infoLabel.AddThemeFontSizeOverride("font_size", 12);
        _infoBg.AddChild(_infoLabel);

        _sceneTree.Root.CallDeferred("add_child", _overlayLayer);
    }

    /// <summary>
    /// Called every frame when mouse inspect is active.
    /// </summary>
    public void Process()
    {
        if (!_active) return;

        var viewport = _sceneTree.Root;
        if (viewport == null) return;

        Vector2 mousePos = viewport.GetMousePosition();

        // Walk the entire scene tree to find the deepest, smallest visible
        // CanvasItem whose rect contains the mouse position.
        _bestCandidate = null;
        _bestArea = float.MaxValue;
        FindSmallestAtPosition(_sceneTree.Root, mousePos);

        if (_bestCandidate != null)
        {
            HighlightNode(_bestCandidate, mousePos);
        }
        else
        {
            ClearHighlight();
        }
    }

    // Scratch state for the recursive search
    private CanvasItem? _bestCandidate;
    private float _bestArea;

    /// <summary>
    /// Recursively find the smallest visible CanvasItem containing the mouse.
    /// "Smallest" = smallest bounding area, which picks specific leaf nodes
    /// (buttons, labels, sprites) over their full-screen parent containers.
    /// </summary>
    private void FindSmallestAtPosition(Node parent, Vector2 pos)
    {
        int count;
        try { count = parent.GetChildCount(); }
        catch { return; }

        for (int i = 0; i < count; i++)
        {
            Node child;
            try { child = parent.GetChild(i); }
            catch { continue; }

            if (!GodotObject.IsInstanceValid(child)) continue;
            if (IsExplorerNode(child)) continue;

            // Check this node
            if (child is CanvasItem ci && ci.IsVisibleInTree())
            {
                Rect2 rect = GetGlobalRect(ci);
                float area = rect.Size.X * rect.Size.Y;

                // Must contain mouse and have meaningful area (skip zero-size)
                if (area > 1f && rect.HasPoint(pos))
                {
                    // Prefer smaller nodes — they're more specific
                    if (area < _bestArea)
                    {
                        _bestCandidate = ci;
                        _bestArea = area;
                    }
                }
            }

            // Always recurse into children to find deeper matches
            FindSmallestAtPosition(child, pos);
        }
    }

    public bool HandleClick()
    {
        if (!_active) return false;
        if (_hoveredNodeId == 0) return false;

        var obj = GodotObject.InstanceFromId(_hoveredNodeId);
        if (obj is Node node && GodotObject.IsInstanceValid(node))
        {
            NodePicked?.Invoke(node);
            IsActive = false;
            return true;
        }
        return false;
    }

    public void Toggle()
    {
        IsActive = !IsActive;
    }

    private void HighlightNode(CanvasItem node, Vector2 mousePos)
    {
        _hoveredNodeId = node.GetInstanceId();

        if (_highlightRect == null || _highlightFill == null || _infoLabel == null || _infoBg == null)
            return;

        Rect2 rect = GetGlobalRect(node);

        _highlightFill.Position = rect.Position;
        _highlightFill.Size = rect.Size;
        _highlightFill.Visible = true;

        _highlightRect.Position = rect.Position;
        _highlightRect.Size = rect.Size;
        _highlightRect.Visible = true;

        // Build info text
        string nodeName = (node as Node)?.Name.ToString() ?? "?";
        string typeName = node.GetClass();
        string path = (node as Node)?.GetPath().ToString() ?? "";
        int childCount = 0;
        try { childCount = (node as Node)?.GetChildCount() ?? 0; } catch { }

        string sizeStr = "";
        if (node is Control ctrl)
            sizeStr = $"\nSize: {ctrl.Size.X:F0} x {ctrl.Size.Y:F0}";

        _infoLabel.Text = $"{nodeName}  [{typeName}]"
            + (childCount > 0 ? $" ({childCount})" : "")
            + sizeStr
            + $"\n{path}";

        // Position tooltip near cursor, keeping on screen
        var vpSize = _sceneTree.Root.GetVisibleRect().Size;
        _infoBg.ResetSize();
        float infoX = mousePos.X + 18;
        float infoY = mousePos.Y + 22;
        if (infoX + _infoBg.Size.X > vpSize.X - 4)
            infoX = mousePos.X - _infoBg.Size.X - 10;
        if (infoY + _infoBg.Size.Y > vpSize.Y - 4)
            infoY = mousePos.Y - _infoBg.Size.Y - 10;
        _infoBg.Position = new Vector2(infoX, infoY);
        _infoBg.Visible = true;
    }

    private void ClearHighlight()
    {
        _hoveredNodeId = 0;
        if (_highlightRect != null) _highlightRect.Visible = false;
        if (_highlightFill != null) _highlightFill.Visible = false;
        if (_infoBg != null) _infoBg.Visible = false;
    }

    private static Rect2 GetGlobalRect(CanvasItem node)
    {
        if (node is Control ctrl)
            return ctrl.GetGlobalRect();

        var xform = node.GetGlobalTransform();
        var pos = xform.Origin;
        Vector2 size = new(64, 64);

        if (node is Sprite2D sprite && sprite.Texture != null)
        {
            size = sprite.Texture.GetSize() * xform.Scale;
            if (sprite.Centered)
                pos -= size * 0.5f;
        }
        else if (node is AnimatedSprite2D animSprite)
        {
            var frames = animSprite.SpriteFrames;
            if (frames != null)
            {
                var anim = animSprite.Animation;
                if (frames.GetFrameCount(anim) > 0)
                {
                    var tex = frames.GetFrameTexture(anim, animSprite.Frame);
                    if (tex != null)
                    {
                        size = tex.GetSize() * xform.Scale;
                        if (animSprite.Centered)
                            pos -= size * 0.5f;
                    }
                }
            }
        }

        return new Rect2(pos, size);
    }

    private static bool IsExplorerNode(Node node)
    {
        Node? current = node;
        int depth = 0;
        while (current != null && depth < 10)
        {
            if (current.Name.ToString().StartsWith("GodotExplorer"))
                return true;
            current = current.GetParent();
            depth++;
        }
        return false;
    }

    public void Cleanup()
    {
        if (_overlayLayer != null && GodotObject.IsInstanceValid(_overlayLayer))
            _overlayLayer.QueueFree();
    }
}
