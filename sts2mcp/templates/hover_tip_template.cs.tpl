using Godot;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Logging;

namespace {namespace}.UI;

/// <summary>
/// Shows a hover tooltip at a position. Usage:
///   {class_name}.Show(globalPosition, "Title", "Description");
/// </summary>
public static class {class_name}
{{
    public static void Show(Vector2 position, string title, string body)
    {{
        var tip = new HoverTip(title, body);
        NHoverTipSet.CreateAndShow(tip, position);
    }}

    public static void ShowForNode(Control node, string title, string body)
    {{
        node.MouseEntered += () => Show(node.GlobalPosition + node.Size / 2, title, body);
        node.MouseExited += () => NHoverTipSet.HideAll();
    }}
}}
