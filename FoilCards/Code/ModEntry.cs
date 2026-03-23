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

            // Update tilt shader params from mouse position
            try
            {
                var body2 = card.GetNodeOrNull<Control>("%CardContainer");
                var tiltMat = body2?.Material as ShaderMaterial;

                var screenMouse = DisplayServer.MouseGetPosition();
                var winPos = DisplayServer.WindowGetPosition();
                var mouse = new Vector2(screenMouse.X - winPos.X, screenMouse.Y - winPos.Y);

                var rect = body2 != null ? body2.GetGlobalRect() : portrait.GetGlobalRect();
                if (rect.Size.X < 1 || rect.Size.Y < 1) return;

                var center = rect.Position + rect.Size * 0.5f;
                var rel = (mouse - center) / (rect.Size * 0.5f);
                rel = rel.Clamp(new Vector2(-1.5f, -1.5f), new Vector2(1.5f, 1.5f));

                // Update foil light_angle
                var cur = mat!.GetShaderParameter("light_angle").AsVector2();
                mat.SetShaderParameter("light_angle", cur.Lerp(rel, 0.2f));

                // Update tilt shader
                if (tiltMat != null)
                {
                    bool mouseOver = rect.HasPoint(mouse);
                    float proximity = mouseOver ? 1.0f : Mathf.Max(0, 1.0f - (rel.Length() - 1.0f) * 2.0f);
                    float tgtX = rel.X * 0.15f * proximity;
                    float tgtY = rel.Y * 0.08f * proximity;
                    try
                    {
                        float cX = (float)tiltMat.GetShaderParameter("tilt_x").AsDouble();
                        float cY = (float)tiltMat.GetShaderParameter("tilt_y").AsDouble();
                        tiltMat.SetShaderParameter("tilt_x", Mathf.Lerp(cX, tgtX, 0.2f));
                        tiltMat.SetShaderParameter("tilt_y", Mathf.Lerp(cY, tgtY, 0.2f));
                    }
                    catch
                    {
                        tiltMat.SetShaderParameter("tilt_x", tgtX);
                        tiltMat.SetShaderParameter("tilt_y", tgtY);
                    }
                }
            }
            catch { }
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
