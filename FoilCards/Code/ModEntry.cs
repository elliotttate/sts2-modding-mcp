using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
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

    public static void Init()
    {
        try
        {
            Log.Warn("[FoilCards] Init...");

            // Background thread: apply foil shader to cards
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
                            WalkAndApply(tree.Root);
                    }
                    catch { }
                    await System.Threading.Tasks.Task.Delay(500);
                }
            });

            // Auto-start the tilt loop via bridge after a delay
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(5000);
                try
                {
                    SendBridgeCommand("start_foil_tilt");
                    Log.Warn("[FoilCards] Started foil tilt loop via bridge.");
                }
                catch (Exception ex)
                {
                    Log.Warn($"[FoilCards] Could not start tilt loop: {ex.Message}");
                }
            });

            Log.Warn("[FoilCards] Init complete.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[FoilCards] ERROR: {ex}");
        }
    }

    private static void WalkAndApply(Node node)
    {
        if (node is NCard card)
            TryApplyFoil(card);

        int count;
        try { count = node.GetChildCount(); } catch { return; }
        for (int i = 0; i < count; i++)
        {
            try { WalkAndApply(node.GetChild(i)); } catch { }
        }
    }

    private static void TryApplyFoil(NCard card)
    {
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
                return;
            }

            if (portrait.Material != null) return;

            mat = FoilShader.CreateMaterial();
            portrait.Material = mat;
            _foilMaterials[id] = mat;
            _applyCount++;
            if (_applyCount <= 20)
                Log.Warn($"[FoilCards] Applied foil #{_applyCount}");
        }
        catch { }
    }

    /// <summary>Send a JSON-RPC request to the MCPTest bridge on localhost:21337</summary>
    private static void SendBridgeCommand(string method)
    {
        using var client = new TcpClient("127.0.0.1", 21337);
        var json = $"{{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"{method}\"}}";
        var data = Encoding.UTF8.GetBytes(json + "\n");
        client.GetStream().Write(data, 0, data.Length);
    }

    public static void ApplyFoilToAllCards() { }
}
