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
    private static readonly Dictionary<ulong, bool> _setupCards = new();

    public static void Init()
    {
        try
        {
            Log.Warn("[FoilCards] Init...");

            // Background thread: find NCards, apply foil to portraits
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(3000);
                Log.Warn("[FoilCards] Foil applicator running.");
                while (true)
                {
                    try
                    {
                        var tree = Engine.GetMainLoop() as SceneTree;
                        if (tree?.Root != null)
                            WalkAndSetup(tree.Root);
                    }
                    catch { }
                    await System.Threading.Tasks.Task.Delay(100); // ~10fps for smooth light updates
                }
            });

            Log.Warn("[FoilCards] Init complete.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[FoilCards] ERROR: {ex}");
        }
    }

    private static void WalkAndSetup(Node node)
    {
        if (node is NCard card)
            SetupCard(card);

        int count;
        try { count = node.GetChildCount(); } catch { return; }
        for (int i = 0; i < count; i++)
        {
            try { WalkAndSetup(node.GetChild(i)); } catch { }
        }
    }

    private static void SetupCard(NCard card)
    {
        try
        {
            if (!card.IsNodeReady()) return;
            var id = card.GetInstanceId();

            // Apply foil shader to portrait
            var portrait = card.GetNodeOrNull<TextureRect>("%Portrait");
            if (portrait == null || !portrait.Visible || portrait.Texture == null) return;

            ShaderMaterial? mat = null;

            if (_setupCards.ContainsKey(id))
            {
                // Already set up — just update light_angle
                mat = portrait.Material as ShaderMaterial;
                if (mat == null) return;
            }
            else
            {
                if (portrait.Material != null) return; // blur/other
                mat = FoilShader.CreateMaterial();
                portrait.Material = mat;
                _setupCards[id] = true;
                _applyCount++;
                if (_applyCount <= 20)
                    Log.Warn($"[FoilCards] Applied foil #{_applyCount}");
            }

            // Update light_angle from mouse position (DisplayServer works from any thread)
            try
            {
                var screenMouse = DisplayServer.MouseGetPosition();
                var winPos = DisplayServer.WindowGetPosition();
                var mousePos = new Vector2(screenMouse.X - winPos.X, screenMouse.Y - winPos.Y);

                // Get card rect via portrait's parent chain (Body/CardContainer)
                var body = card.GetNodeOrNull<Control>("%CardContainer");
                var cRect = body != null ? body.GetGlobalRect() : portrait.GetGlobalRect();
                if (cRect.Size.X < 1 || cRect.Size.Y < 1) return;

                var center = cRect.Position + cRect.Size * 0.5f;
                var rel = (mousePos - center) / (cRect.Size * 0.5f);
                rel = rel.Clamp(new Vector2(-1.5f, -1.5f), new Vector2(1.5f, 1.5f));

                // Smooth lerp the light angle
                try
                {
                    var cur = mat.GetShaderParameter("light_angle").AsVector2();
                    mat.SetShaderParameter("light_angle", cur.Lerp(rel, 0.2f));
                }
                catch
                {
                    mat.SetShaderParameter("light_angle", rel);
                }
            }
            catch { }
        }
        catch { }
    }

    public static void ApplyFoilToAllCards() { }
}
