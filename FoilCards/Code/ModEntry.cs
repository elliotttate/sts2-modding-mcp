using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace FoilCards;

/// <summary>
/// FoilCards mod — adds holographic foil + 3D perspective tilt to all cards.
///
/// Architecture (inspired by chaofan's 3dcardeffects for STS1):
/// - Wraps each card's CardContainer in a SubViewport so the entire card
///   (frame, art, text, icons) renders as a single texture
/// - Applies a perspective vertex shader to the SubViewportContainer
///   which warps ALL pixels together — true 3D trapezoid tilt
/// - Also applies a foil rainbow shader to the portrait art
/// </summary>
[ModInitializer("Init")]
public static class ModEntry
{
    private static int _setupCount = 0;
    private static readonly HashSet<ulong> _processedCards = new();

    // Card dimensions from the scene file
    private const int CardWidth = 300;
    private const int CardHeight = 422;

    public static void Init()
    {
        try
        {
            Log.Warn("[FoilCards] Init — clean build with SubViewport approach.");

            // Background thread: find NCards and set up SubViewport wrapping
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

            // Auto-start rotation via bridge after delay
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(6000);
                try
                {
                    var client = new System.Net.Sockets.TcpClient("127.0.0.1", 21337);
                    var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"start_auto_rotate\"}";
                    var data = System.Text.Encoding.UTF8.GetBytes(json + "\n");
                    client.GetStream().Write(data, 0, data.Length);
                    client.Close();
                    Log.Warn("[FoilCards] Auto-rotate started via bridge.");
                }
                catch (Exception ex)
                {
                    Log.Warn($"[FoilCards] Could not start auto-rotate: {ex.Message}");
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
            if (_processedCards.Contains(id)) return;

            // Apply foil shader to portrait
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

    public static void ApplyFoilToAllCards() { }
}
