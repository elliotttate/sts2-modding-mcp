using System;
using Godot;
using MegaCrit.Sts2.Core.Modding;
using GodotExplorer.Core;

namespace GodotExplorer;

/// <summary>
/// Mod entry point. The game's mod loader calls Init() on startup.
/// </summary>
[ModInitializer("Init")]
public static class GodotExplorerMod
{
    public static void Init()
    {
        try
        {
            GD.Print("[GodotExplorer] Init...");

            var sceneTree = Engine.GetMainLoop() as SceneTree;
            if (sceneTree == null)
            {
                GD.PrintErr("[GodotExplorer] Failed to get SceneTree from Engine.GetMainLoop()");
                return;
            }

            // Defer initialization to the next frame so the scene tree is fully ready.
            sceneTree.Connect("process_frame",
                Callable.From(() =>
                {
                    try
                    {
                        ExplorerCore.Initialize(sceneTree);
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[GodotExplorer] Initialization error: {ex}");
                    }
                }),
                (uint)GodotObject.ConnectFlags.OneShot);

            GD.Print("[GodotExplorer] Init complete — deferred setup queued.");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[GodotExplorer] Init ERROR: {ex}");
        }
    }
}
