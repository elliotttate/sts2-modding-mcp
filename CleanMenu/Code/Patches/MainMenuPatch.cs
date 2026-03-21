using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace CleanMenu.Patches;

/// <summary>
/// Three states cycled by F1:
///   0 = Normal (everything visible)
///   1 = Logo only (UI hidden, logo stays)
///   2 = Background only (UI + logo hidden)
/// </summary>
[HarmonyPatch(typeof(NMainMenu), "_Ready")]
public static class MainMenuReadyPatch
{
    private static readonly List<NodePath> UiPaths = new()
    {
        new NodePath("MainMenuTextButtons"),
        new NodePath("%ButtonReticleLeft"),
        new NodePath("%ButtonReticleRight"),
        new NodePath("%ChangeProfileButton"),
        new NodePath("%PatchNotesButton"),
        new NodePath("%ContinueRunInfo"),
        new NodePath("%TimelineNotificationDot"),
    };

    internal static readonly List<CanvasItem> UiNodes = new();
    internal static readonly List<CanvasItem> DebugNodes = new();
    internal static CanvasItem? LogoNode;
    internal static NMainMenu? MenuInstance;
    private static bool _wasF1Pressed;

    // 0=normal, 1=logo only, 2=bg only
    internal static int State = 0;

    public static void Postfix(NMainMenu __instance)
    {
        MenuInstance = __instance;
        UiNodes.Clear();
        DebugNodes.Clear();
        LogoNode = null;
        _wasF1Pressed = false;

        // Collect UI elements to hide
        foreach (var path in UiPaths)
        {
            var node = __instance.GetNodeOrNull<CanvasItem>(path);
            if (node != null)
                UiNodes.Add(node);
        }

        // Find NDebugInfoLabelManager (version + modded text)
        CollectDebugLabels(__instance);

        // Find the logo inside NMainMenuBg
        var bg = __instance.GetNodeOrNull<Node>("%MainMenuBg");
        if (bg != null)
        {
            var logo = bg.GetNodeOrNull<CanvasItem>("%Logo");
            LogoNode = logo;
        }

        // Poll F1 each frame via ProcessFrame signal
        __instance.GetTree().ProcessFrame += OnProcessFrame;

        ApplyState();
    }

    private static void OnProcessFrame()
    {
        bool f1Down = Input.IsPhysicalKeyPressed(Key.F1);

        // Detect rising edge (key just pressed)
        if (f1Down && !_wasF1Pressed)
        {
            State = (State + 1) % 3;
            ApplyState();
        }

        _wasF1Pressed = f1Down;
    }

    private static void CollectDebugLabels(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            // NDebugInfoLabelManager extends Node (not CanvasItem), so hide its
            // MegaLabel children directly: ReleaseInfo and ModdedWarning
            if (child.GetType().Name == "NDebugInfoLabelManager")
            {
                var release = child.GetNodeOrNull<CanvasItem>("%ReleaseInfo");
                if (release != null) DebugNodes.Add(release);
                var modded = child.GetNodeOrNull<CanvasItem>("%ModdedWarning");
                if (modded != null) DebugNodes.Add(modded);
            }
            CollectDebugLabels(child);
        }
    }

    internal static void ApplyState()
    {
        bool showUi = State == 0;
        bool showLogo = State <= 1;

        // UI elements (buttons, profile, patch notes, etc.)
        foreach (var node in UiNodes)
        {
            if (GodotObject.IsInstanceValid(node))
                node.Visible = showUi;
        }

        // Debug labels (version, modded warning)
        foreach (var node in DebugNodes)
        {
            if (GodotObject.IsInstanceValid(node))
                node.Visible = showUi;
        }

        // Logo
        if (LogoNode != null && GodotObject.IsInstanceValid(LogoNode))
            LogoNode.Visible = showLogo;
    }
}
