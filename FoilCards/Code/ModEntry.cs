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
    private static readonly HashSet<ulong> _tiltSetup = new();

    public static void Init()
    {
        try
        {
            Log.Warn("[FoilCards] Init...");

            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(3000);
                Log.Warn("[FoilCards] Loop running.");
                while (true)
                {
                    try
                    {
                        var tree = Engine.GetMainLoop() as SceneTree;
                        if (tree?.Root != null)
                            ProcessAll(tree.Root);
                    }
                    catch { }
                    await System.Threading.Tasks.Task.Delay(100);
                }
            });

            Log.Warn("[FoilCards] Init complete.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[FoilCards] ERROR: {ex}");
        }
    }

    private static void ProcessAll(Node node)
    {
        if (node is NCard card)
            ProcessCard(card);

        int count;
        try { count = node.GetChildCount(); } catch { return; }
        for (int i = 0; i < count; i++)
        {
            try { ProcessAll(node.GetChild(i)); } catch { }
        }
    }

    private static void ProcessCard(NCard card)
    {
        try
        {
            if (!card.IsNodeReady()) return;
            var portrait = card.GetNodeOrNull<TextureRect>("%Portrait");
            if (portrait == null || !portrait.Visible || portrait.Texture == null) return;

            // Apply foil shader to portrait
            var mat = portrait.Material as ShaderMaterial;
            bool hasFoil = mat != null && mat.Shader == FoilShader.GetShader();
            if (!hasFoil)
            {
                if (portrait.Material != null) return; // blur
                mat = FoilShader.CreateMaterial();
                portrait.Material = mat;
                _applyCount++;
                if (_applyCount <= 20)
                    Log.Warn($"[FoilCards] Applied foil #{_applyCount}");
            }

            // Setup 3D tilt on CardContainer using UseParentMaterial + vertex shader
            // Only set UseParentMaterial on visual nodes (TextureRect, NinePatchRect, etc.)
            // NOT on Labels/RichTextLabels (those get garbled)
            var cardId = card.GetInstanceId();
            if (!_tiltSetup.Contains(cardId))
            {
                var body = card.GetNodeOrNull<Control>("%CardContainer");
                if (body != null)
                {
                    // Apply tilt vertex shader to CardContainer
                    if (body.Material == null)
                    {
                        body.Material = FoilShader.CreateTiltMaterial();
                    }

                    // Set UseParentMaterial on visual children ONLY (skip text nodes)
                    SetUseParentOnVisuals(body);
                    _tiltSetup.Add(cardId);

                    if (_applyCount <= 20)
                        Log.Warn($"[FoilCards] Tilt setup on '{card.Name}'");
                }
            }

            // Mouse tracking + tilt updates are done by the bridge's find_cards
            // handler on the main thread. Background thread can't reliably call
            // DisplayServer.MouseGetPosition(). Don't update shader params here.
        }
        catch { }
    }

    /// <summary>
    /// Recursively set UseParentMaterial on visual nodes only.
    /// Skips Labels and RichTextLabels to prevent text garbling.
    /// </summary>
    private static void SetUseParentOnVisuals(Node parent)
    {
        for (int i = 0; i < parent.GetChildCount(); i++)
        {
            try
            {
                var child = parent.GetChild(i);
                if (child is Label || child is RichTextLabel)
                    continue; // Skip text nodes — they get garbled by the vertex shader

                // Check by Godot class name too (MegaLabel, MegaRichTextLabel)
                var className = child.GetType().Name;
                if (className.Contains("Label") || className.Contains("RichText"))
                    continue;

                if (child is CanvasItem ci)
                    ci.UseParentMaterial = true;

                // Recurse into children
                SetUseParentOnVisuals(child);
            }
            catch { }
        }
    }

    public static void ApplyFoilToAllCards() { }
}
