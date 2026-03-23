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
                    await System.Threading.Tasks.Task.Delay(500);
                }
            });

            // Auto-start continuous tilt loop via bridge
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(5000);
                try
                {
                    var client = new System.Net.Sockets.TcpClient("127.0.0.1", 21337);
                    var json = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"start_card_tilt_loop\"}";
                    var data = System.Text.Encoding.UTF8.GetBytes(json + "\n");
                    client.GetStream().Write(data, 0, data.Length);
                    client.Close();
                    Log.Warn("[FoilCards] Started tilt loop via bridge.");
                }
                catch { }
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
            if (_setupCards.ContainsKey(id)) return;

            // Apply foil shader to portrait only (not the whole card)
            var portrait = card.GetNodeOrNull<TextureRect>("%Portrait");
            if (portrait != null && portrait.Visible && portrait.Texture != null && portrait.Material == null)
            {
                portrait.Material = FoilShader.CreateMaterial();
                _applyCount++;
                if (_applyCount <= 20)
                    Log.Warn($"[FoilCards] Applied foil #{_applyCount}");
            }

            // DO NOT apply UseParentMaterial or tilt shader here.
            // The tilt is handled by the bridge's find_cards/card_tilt_test
            // which wraps the card in a SubViewport for proper whole-card tilt.

            _setupCards[id] = true;
        }
        catch { }
    }

    public static void ApplyFoilToAllCards() { }
}
