using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;

namespace {namespace}.Patches;

/// <summary>
/// Injects localization for custom modifiers via LocManager.SetLanguage.
/// This ensures translations persist across language changes.
/// </summary>
[HarmonyPatch(typeof(LocManager), nameof(LocManager.SetLanguage))]
public class ModifierLocPatch
{{
    public static void Postfix(LocManager __instance)
    {{
        __instance.GetTable("modifiers").MergeWith(
            new Dictionary<string, string>
            {{
{loc_entries}
            }});
    }}
}}
