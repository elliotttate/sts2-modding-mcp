using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace {namespace}.Patches;

/// <summary>
/// Required patch for custom static-image enemies. Apply once in your mod.
/// Enables loading custom .tscn scenes for monster visuals.
/// </summary>
[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.CreateVisuals))]
public static class CreateVisualsPatch
{{
    private static readonly MethodInfo _visualsPathGetter = typeof(MonsterModel)
        .GetProperty("VisualsPath", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetGetMethod(true)!;

    public static bool Prefix(MonsterModel __instance, ref NCreatureVisuals __result)
    {{
        var path = (string)_visualsPathGetter.Invoke(__instance, null)!;
        var scene = PreloadManager.Cache.GetScene(path);

        try
        {{
            __result = scene.Instantiate<NCreatureVisuals>();
            return false;
        }}
        catch (InvalidCastException)
        {{
        }}

        var raw = scene.Instantiate<Node2D>();
        var visuals = new NCreatureVisuals();
        visuals.Name = raw.Name;

        foreach (var child in raw.GetChildren())
        {{
            raw.RemoveChild(child);
            visuals.AddChild(child);
            if (child is Node n && n.UniqueNameInOwner)
            {{
                n.Owner = visuals;
                n.UniqueNameInOwner = true;
            }}
        }}

        raw.QueueFree();
        __result = visuals;
        return false;
    }}
}}
