using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Encounters;

namespace {namespace}.Patches;

/// <summary>
/// Adds the custom encounter to the specified act.
/// </summary>
[HarmonyPatch(typeof({act_class}), nameof({act_class}.GenerateAllEncounters))]
public static class {class_name}
{{
    public static void Postfix(ref IEnumerable<EncounterModel> __result)
    {{
        var list = __result.ToList();
        list.Add(ModelDb.Encounter<{encounter_class}>());
        __result = list;
    }}
}}
