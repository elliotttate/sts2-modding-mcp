using System;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FoilCards;

[ModInitializer("Init")]
public static class ModEntry
{
    private static int _applyCount = 0;

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

            // Always apply/re-apply foil if portrait has no material or has our shader
            var mat = portrait.Material as ShaderMaterial;
            bool hasFoil = mat != null && mat.Shader == FoilShader.GetShader();

            if (!hasFoil)
            {
                // Don't override blur materials (locked/not-seen cards)
                if (portrait.Material != null) return;

                mat = FoilShader.CreateMaterial();
                portrait.Material = mat;
                _applyCount++;
                if (_applyCount <= 20)
                    Log.Warn($"[FoilCards] Applied foil #{_applyCount}");
            }

            // Update light_angle from mouse position
            try
            {
                var screenMouse = DisplayServer.MouseGetPosition();
                var winPos = DisplayServer.WindowGetPosition();
                var mouse = new Vector2(screenMouse.X - winPos.X, screenMouse.Y - winPos.Y);

                var body = card.GetNodeOrNull<Control>("%CardContainer");
                var rect = body != null ? body.GetGlobalRect() : portrait.GetGlobalRect();
                if (rect.Size.X < 1 || rect.Size.Y < 1) return;

                var center = rect.Position + rect.Size * 0.5f;
                var rel = (mouse - center) / (rect.Size * 0.5f);
                rel = rel.Clamp(new Vector2(-1.5f, -1.5f), new Vector2(1.5f, 1.5f));

                var cur = mat!.GetShaderParameter("light_angle").AsVector2();
                mat.SetShaderParameter("light_angle", cur.Lerp(rel, 0.2f));
            }
            catch { }
        }
        catch { }
    }

    public static void ApplyFoilToAllCards() { }
}
