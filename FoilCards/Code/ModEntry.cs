using System;
using System.Threading;
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

    public static void Init()
    {
        try
        {
            Log.Warn("[FoilCards] Init...");

            // Use Harmony to patch a method we KNOW gets called — the Hook class
            _harmony = new Harmony("com.elliotttate.foilcards");
            _harmony.PatchAll();

            foreach (var m in _harmony.GetPatchedMethods())
                Log.Warn($"[FoilCards] Patched: {m.DeclaringType?.Name}.{m.Name}");

            // Start async polling loop
            System.Threading.Tasks.Task.Run(async () =>
            {
                Log.Warn("[FoilCards] Async loop starting, waiting 3s...");
                await System.Threading.Tasks.Task.Delay(3000);
                Log.Warn("[FoilCards] Async loop running!");
                int loopCount = 0;
                while (true)
                {
                    loopCount++;
                    try
                    {
                        var tree = Engine.GetMainLoop() as SceneTree;
                        if (tree?.Root != null)
                        {
                            _walkCount = 0;
                            ApplyRecursive(tree.Root);
                            if (loopCount <= 3 || (loopCount % 20 == 0 && loopCount <= 100))
                                Log.Warn($"[FoilCards] Loop #{loopCount}: walked {_walkCount} nodes, applied {_applyCount} foils");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (loopCount <= 3)
                            Log.Warn($"[FoilCards] Loop #{loopCount} error: {ex.GetType().Name}: {ex.Message}");
                    }
                    await System.Threading.Tasks.Task.Delay(500);
                }
            });

            Log.Warn("[FoilCards] Init complete — async poll started.");
        }
        catch (Exception ex)
        {
            Log.Warn($"[FoilCards] ERROR: {ex}");
        }
    }

    public static void ApplyFoilToAllCards()
    {
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree?.Root != null)
                ApplyRecursive(tree.Root);
        }
        catch { }
    }

    private static int _walkCount = 0;

    private static void ApplyRecursive(Node node)
    {
        _walkCount++;

        if (node is NCard card)
        {
            ApplyFoilToCard(card);
        }
        else if (_walkCount <= 3 && node.GetType().Name.Contains("Card"))
        {
            Log.Warn($"[FoilCards] Found card-like node: {node.GetType().FullName} name='{node.Name}'");
        }

        int count;
        try { count = node.GetChildCount(); } catch { return; }
        for (int i = 0; i < count; i++)
        {
            try { ApplyRecursive(node.GetChild(i)); } catch { }
        }
    }

    private static void ApplyFoilToCard(NCard card)
    {
        try
        {
            if (!card.IsNodeReady()) return;

            var portrait = card.GetNodeOrNull<TextureRect>("%Portrait");
            if (portrait == null || !portrait.Visible || portrait.Texture == null) return;

            if (portrait.Material is ShaderMaterial sm && sm.Shader == FoilShader.GetShader())
                return;
            if (portrait.Material != null)
                return;

            portrait.Material = FoilShader.CreateMaterial();

            _applyCount++;
            if (_applyCount <= 20)
                Log.Warn($"[FoilCards] Applied foil #{_applyCount} to '{card.Name}'");
        }
        catch { }
    }
}
