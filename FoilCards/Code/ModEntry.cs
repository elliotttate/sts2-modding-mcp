using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FoilCards;

[ModInitializer("Init")]
public static class ModEntry
{
    private static int _applyCount = 0;
    private static readonly Dictionary<ulong, ShaderMaterial> _foilMaterials = new();

    private const float MaxTiltDeg = 20.0f;  // More aggressive tilt
    private const float TiltSmooth = 0.3f;   // Faster response
    private const float LightSmooth = 0.3f;

    public static void Init()
    {
        try
        {
            Log.Warn("[FoilCards] Init...");

            // This exact pattern worked before — Task.Run with direct Godot access
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(3000);
                Log.Warn("[FoilCards] Loop running.");
                int loopCount = 0;
                while (true)
                {
                    loopCount++;
                    try
                    {
                        var tree = Engine.GetMainLoop() as SceneTree;
                        if (tree?.Root != null)
                        {
                            int found = 0;
                            int total = 0;

                            // Walk ALL windows, not just the main root
                            foreach (var window in tree.Root.GetTree().Root.GetChildren())
                            {
                                ProcessRecursive(window, ref found, ref total);
                            }
                            // Also walk the root itself
                            ProcessRecursive(tree.Root, ref found, ref total);

                            if (loopCount <= 3 || (loopCount % 20 == 0 && loopCount <= 100))
                                Log.Warn($"[FoilCards] Loop #{loopCount}: {total} nodes, {found} cards, {_applyCount} foils");
                        }
                    }
                    catch { }
                    await System.Threading.Tasks.Task.Delay(30); // ~33fps for smoother tilt
                }
            });

            Log.Warn("[FoilCards] Init complete.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[FoilCards] ERROR: {ex}");
        }
    }

    private static readonly HashSet<string> _loggedTypes = new();

    private static void ProcessRecursive(Node node, ref int found, ref int total)
    {
        total++;

        if (node is NCard card)
        {
            found++;
            ProcessCard(card);
        }
        else if (node is Control ctrl && node.GetType().FullName == "MegaCrit.Sts2.Core.Nodes.Cards.NCard")
        {
            found++;
            ProcessCardByReflection(ctrl);
        }

        // Log unique type names on first loop to understand the tree
        if (total <= 50 && _loggedTypes.Count < 30)
        {
            var tn = node.GetType().FullName ?? "?";
            if (_loggedTypes.Add(tn))
                Log.Warn($"[FoilCards] Type: {tn}");
        }

        int count;
        try { count = node.GetChildCount(); } catch { return; }
        for (int i = 0; i < count; i++)
        {
            try { ProcessRecursive(node.GetChild(i), ref found, ref total); } catch { }
        }
    }

    private static void ProcessCard(NCard card)
    {
        try
        {
            if (!card.IsNodeReady()) return;
            var portrait = card.GetNodeOrNull<TextureRect>("%Portrait");
            if (portrait == null || !portrait.Visible || portrait.Texture == null) return;

            var id = portrait.GetInstanceId();
            ShaderMaterial? mat;

            // Apply or re-apply foil
            if (_foilMaterials.TryGetValue(id, out mat))
            {
                if (portrait.Material != mat && portrait.Material == null)
                    portrait.Material = mat;
                if (portrait.Material != mat) return;
            }
            else
            {
                if (portrait.Material != null) return;
                mat = FoilShader.CreateMaterial();
                portrait.Material = mat;
                _foilMaterials[id] = mat;
                _applyCount++;
                if (_applyCount <= 20)
                    Log.Warn($"[FoilCards] Applied foil #{_applyCount}");
            }

            // --- Update light angle ---
            var mouseGlobal = card.GetGlobalMousePosition();
            var cardRect = card.GetGlobalRect();
            if (cardRect.Size.X < 1 || cardRect.Size.Y < 1) return;

            var cardCenter = cardRect.Position + cardRect.Size * 0.5f;
            var relative = (mouseGlobal - cardCenter) / (cardRect.Size * 0.5f);
            relative = relative.Clamp(new Vector2(-1.5f, -1.5f), new Vector2(1.5f, 1.5f));

            var currentAngle = (Vector2)mat.GetShaderParameter("light_angle");
            var smoothedAngle = currentAngle.Lerp(relative, LightSmooth);
            mat.SetShaderParameter("light_angle", smoothedAngle);

            // --- Physical card tilt ---
            // Rotate the Body/CardContainer child instead of the card itself,
            // because the card library's grid layout resets card.RotationDegrees.
            bool mouseOver = cardRect.HasPoint(mouseGlobal);
            float proximity = mouseOver ? 1.0f : Mathf.Max(0, 1.0f - (relative.Length() - 1.0f) * 2.0f);
            float targetRot = -relative.X * MaxTiltDeg * proximity;

            // Find the Body (CardContainer) child — this is the visual content
            var body = card.GetNodeOrNull<Control>("%CardContainer");
            if (body == null) body = card.GetNodeOrNull<Control>("CardContainer");
            if (body != null)
            {
                float currentRot = body.RotationDegrees;
                float newRot = Mathf.Lerp(currentRot, targetRot, TiltSmooth);
                body.PivotOffset = body.Size * 0.5f;
                body.RotationDegrees = newRot;
            }
        }
        catch { }
    }

    private static void ProcessCardByReflection(Control card)
    {
        // Same as ProcessCard but using the Control type (when is NCard fails due to assembly mismatch)
        try
        {
            if (!card.IsNodeReady()) return;
            var portrait = card.GetNodeOrNull<TextureRect>("%Portrait");
            if (portrait == null || !portrait.Visible || portrait.Texture == null) return;

            var id = portrait.GetInstanceId();
            ShaderMaterial? mat;

            if (_foilMaterials.TryGetValue(id, out mat))
            {
                if (portrait.Material != mat && portrait.Material == null)
                    portrait.Material = mat;
                if (portrait.Material != mat) return;
            }
            else
            {
                if (portrait.Material != null) return;
                mat = FoilShader.CreateMaterial();
                portrait.Material = mat;
                _foilMaterials[id] = mat;
                _applyCount++;
                if (_applyCount <= 20)
                    Log.Warn($"[FoilCards] Applied foil #{_applyCount} (via reflection)");
            }

            // Light angle
            var mouseGlobal = card.GetGlobalMousePosition();
            var cardRect = card.GetGlobalRect();
            if (cardRect.Size.X < 1 || cardRect.Size.Y < 1) return;

            var cardCenter = cardRect.Position + cardRect.Size * 0.5f;
            var relative = (mouseGlobal - cardCenter) / (cardRect.Size * 0.5f);
            relative = relative.Clamp(new Vector2(-1.5f, -1.5f), new Vector2(1.5f, 1.5f));

            var currentAngle = (Vector2)mat.GetShaderParameter("light_angle");
            mat.SetShaderParameter("light_angle", currentAngle.Lerp(relative, LightSmooth));

            // Tilt
            bool mouseOver = cardRect.HasPoint(mouseGlobal);
            float proximity = mouseOver ? 1.0f : Mathf.Max(0, 1.0f - (relative.Length() - 1.0f) * 2.0f);
            float targetRot = -relative.X * MaxTiltDeg * proximity;
            float newRot = Mathf.Lerp(card.RotationDegrees, targetRot, TiltSmooth);
            card.PivotOffset = card.Size * 0.5f;
            card.RotationDegrees = newRot;
        }
        catch { }
    }

    public static void ApplyFoilToAllCards()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            int found = 0, total = 0;
            if (tree?.Root != null) ProcessRecursive(tree.Root, ref found, ref total);
        }
        catch { }
    }
}
