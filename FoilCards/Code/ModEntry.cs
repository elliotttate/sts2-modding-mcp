using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FoilCards;

/// <summary>
/// FoilCards mod — holographic foil + 3D card tilt for Slay the Spire 2.
///
/// - Applies a foil rainbow shader to the portrait TextureRect
/// - Continuously tilts all cards via Scale.X on the NCard root
/// - Self-contained: no bridge dependency needed
/// </summary>
[ModInitializer("Init")]
public static class ModEntry
{
    private static int _setupCount = 0;
    private static readonly HashSet<ulong> _processedCards = new();

    private const int CardWidth = 300;
    private const int CardHeight = 422;

    // Tilt animation state
    private static float _tiltAngle = 0f;
    private static float _tiltSpeed = 1.5f; // radians per second

    public static void Init()
    {
        try
        {
            Log.Warn("[FoilCards] Init — foil shader + self-contained tilt.");

            // Setup loop: discover new cards and apply foil shader (slower, every 500ms)
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(3000);
                Log.Warn("[FoilCards] Setup loop running.");
                while (true)
                {
                    try
                    {
                        var tree = Engine.GetMainLoop() as SceneTree;
                        if (tree?.Root != null)
                            WalkAndSetup(tree.Root);
                    }
                    catch { }
                    await System.Threading.Tasks.Task.Delay(500);
                }
            });

            // Tilt loop: animate all cards at ~30fps
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(4000);
                Log.Warn("[FoilCards] Tilt loop running.");
                while (true)
                {
                    try
                    {
                        _tiltAngle += _tiltSpeed * 0.033f;
                        var tree = Engine.GetMainLoop() as SceneTree;
                        if (tree?.Root != null)
                            WalkAndTilt(tree.Root);
                    }
                    catch { }
                    await System.Threading.Tasks.Task.Delay(33); // ~30fps
                }
            });

            Log.Warn("[FoilCards] Init complete.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[FoilCards] ERROR: {ex}");
        }
    }

    // ─── Setup: find NCards and apply foil shader to portraits ────────────

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
            if (_processedCards.Contains(id)) return;

            var portrait = card.GetNodeOrNull<TextureRect>("%Portrait");
            if (portrait != null && portrait.Visible && portrait.Texture != null && portrait.Material == null)
            {
                portrait.Material = FoilShader.CreateFoilMaterial();
                _setupCount++;
                if (_setupCount <= 20)
                    Log.Warn($"[FoilCards] Applied foil #{_setupCount}");
            }

            _processedCards.Add(id);
        }
        catch { }
    }

    // ─── Tilt: animate Scale.X on NCard root for 3D card tilt ────────────

    private static void WalkAndTilt(Node node)
    {
        if (node is NCard && node is Control ctrl)
        {
            try
            {
                TiltCard(ctrl);
            }
            catch { }
        }

        int count;
        try { count = node.GetChildCount(); } catch { return; }
        for (int i = 0; i < count; i++)
        {
            try { WalkAndTilt(node.GetChild(i)); } catch { }
        }
    }

    private static void TiltCard(Control ctrl)
    {
        // Tilt: sin wave drives direction, cos gives foreshortening
        float tilt = (float)Math.Sin(_tiltAngle); // -1 to +1
        float tiltDeg = tilt * 30.0f; // ±30 degrees
        float tiltRad = tiltDeg * (float)Math.PI / 180.0f;
        float scaleX = (float)Math.Cos(tiltRad); // narrowing (0.87 ↔ 1.0)
        float lean = tilt * 2.0f * (float)Math.PI / 180.0f; // ±2° rotation for direction

        ctrl.SetDeferred("pivot_offset", new Vector2(150, 211));
        ctrl.SetDeferred("scale", new Vector2(scaleX, 1.0f));
        ctrl.SetDeferred("rotation", lean);

        // Update foil shader on portrait — rainbow shifts with tilt direction
        var portrait = ctrl.GetNodeOrNull<TextureRect>("%Portrait");
        if (portrait?.Material is ShaderMaterial foilMat)
        {
            var lightAngle = new Vector2(
                (float)Math.Sin(_tiltAngle),
                (float)Math.Cos(_tiltAngle)
            );
            foilMat.SetShaderParameter("light_angle", lightAngle);
        }
    }

    public static void ApplyFoilToAllCards() { }
}
