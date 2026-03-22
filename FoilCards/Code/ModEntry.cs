using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FoilCards;

[ModInitializer("Init")]
public static class ModEntry
{
    private static int _applyCount = 0;
    private static Harmony? _harmony;
    private static readonly Dictionary<ulong, ShaderMaterial> _foilMaterials = new();
    private static readonly Dictionary<ulong, float> _cardTargetRotations = new();

    private const float MaxTiltDeg = 12.0f;
    private const float TiltSmooth = 8.0f;
    private const float LightSmooth = 5.0f;

    public static void Init()
    {
        try
        {
            Log.Warn("[FoilCards] Init...");
            _harmony = new Harmony("com.elliotttate.foilcards");
            _harmony.PatchAll();

            // Connect to SceneTree.ProcessFrame signal for per-frame updates
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree != null)
            {
                tree.ProcessFrame += OnProcessFrame;
                Log.Warn("[FoilCards] Connected to SceneTree.ProcessFrame signal.");
            }
            else
            {
                Log.Warn("[FoilCards] WARNING: SceneTree not available for ProcessFrame.");
            }

            // Background thread for slower foil application (finding new cards)
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(2000);
                while (true)
                {
                    try
                    {
                        var t = Engine.GetMainLoop() as SceneTree;
                        if (t?.Root != null)
                            ApplyFoilRecursive(t.Root);
                    }
                    catch { }
                    await System.Threading.Tasks.Task.Delay(500);
                }
            });

            Log.Warn("[FoilCards] Init complete.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[FoilCards] ERROR: {ex}");
        }
    }

    /// <summary>
    /// Called every frame by Godot's SceneTree.ProcessFrame signal.
    /// This runs on the main thread — perfect for smooth card tilt and shader updates.
    /// </summary>
    private static void OnProcessFrame()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root == null) return;

            float delta = (float)tree.Root.GetProcessDeltaTime();
            UpdateCardsRecursive(tree.Root, delta);
        }
        catch { }
    }

    // --- Per-frame update: tilt cards + update shader light_angle ---
    private static void UpdateCardsRecursive(Node node, float delta)
    {
        if (node is NCard card)
            UpdateCard(card, delta);

        int count;
        try { count = node.GetChildCount(); } catch { return; }
        for (int i = 0; i < count; i++)
        {
            try { UpdateCardsRecursive(node.GetChild(i), delta); } catch { }
        }
    }

    private static void UpdateCard(NCard card, float delta)
    {
        try
        {
            if (!card.IsNodeReady() || !card.IsInsideTree()) return;

            var portrait = card.GetNodeOrNull<TextureRect>("%Portrait");
            if (portrait == null || !portrait.Visible) return;

            // Only update if we have a foil material on this portrait
            var id = portrait.GetInstanceId();
            if (!_foilMaterials.TryGetValue(id, out var mat)) return;
            if (portrait.Material != mat) return;

            // Mouse position relative to card center
            var mouseGlobal = card.GetGlobalMousePosition();
            var cardRect = card.GetGlobalRect();
            if (cardRect.Size.X < 1 || cardRect.Size.Y < 1) return;

            var cardCenter = cardRect.Position + cardRect.Size * 0.5f;
            var relative = (mouseGlobal - cardCenter) / (cardRect.Size * 0.5f);
            relative = relative.Clamp(new Vector2(-1.5f, -1.5f), new Vector2(1.5f, 1.5f));

            // Is the mouse actually over this card?
            bool mouseOver = cardRect.HasPoint(mouseGlobal);
            float proximity = mouseOver ? 1.0f : Mathf.Max(0, 1.0f - (relative.Length() - 1.0f) * 2.0f);

            // --- Update shader light angle ---
            var current = (Vector2)mat.GetShaderParameter("light_angle");
            var smoothed = current.Lerp(relative, delta * LightSmooth);
            mat.SetShaderParameter("light_angle", smoothed);

            // --- Physical card tilt ---
            var cardId = card.GetInstanceId();
            float targetRot = -relative.X * MaxTiltDeg * proximity;

            // Smooth rotation
            if (!_cardTargetRotations.TryGetValue(cardId, out float currentTilt))
                currentTilt = card.RotationDegrees;

            float newTilt = Mathf.Lerp(currentTilt, targetRot, delta * TiltSmooth);
            _cardTargetRotations[cardId] = newTilt;

            // Only apply tilt if it's meaningful
            if (Mathf.Abs(newTilt) > 0.1f || Mathf.Abs(targetRot) > 0.1f)
            {
                card.RotationDegrees = newTilt;
                // Set pivot to center so it tilts around the middle
                card.PivotOffset = card.Size * 0.5f;
            }
        }
        catch { }
    }

    // --- Slower loop: find new cards and apply foil shader ---
    private static void ApplyFoilRecursive(Node node)
    {
        if (node is NCard card)
            ApplyFoil(card);

        int count;
        try { count = node.GetChildCount(); } catch { return; }
        for (int i = 0; i < count; i++)
        {
            try { ApplyFoilRecursive(node.GetChild(i)); } catch { }
        }
    }

    private static void ApplyFoil(NCard card)
    {
        try
        {
            if (!card.IsNodeReady() || !card.IsInsideTree()) return;

            var portrait = card.GetNodeOrNull<TextureRect>("%Portrait");
            if (portrait == null || !portrait.Visible || portrait.Texture == null) return;

            var id = portrait.GetInstanceId();

            // Already applied?
            if (_foilMaterials.TryGetValue(id, out var existing))
            {
                if (portrait.Material != existing && portrait.Material == null)
                    portrait.Material = existing; // Re-apply after pool reset
                return;
            }

            // Don't override non-null materials (blur for locked cards)
            if (portrait.Material != null) return;

            var mat = FoilShader.CreateMaterial();
            portrait.Material = mat;
            _foilMaterials[id] = mat;
            _applyCount++;
            if (_applyCount <= 20)
                Log.Warn($"[FoilCards] Applied foil #{_applyCount} to '{card.Name}'");
        }
        catch { }
    }

    public static void ApplyFoilToAllCards()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root != null) ApplyFoilRecursive(tree.Root);
        }
        catch { }
    }
}
