using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace {namespace}.Patches;

/// <summary>
/// Registers {class_name} as a {modifier_type} modifier in the custom run screen.
/// </summary>
[HarmonyPatch(typeof(ModelDb), "get_{modifier_list}")]
public class {class_name}RegistrationPatch
{{
    public static void Postfix(ref IReadOnlyList<ModifierModel> __result)
    {{
        var extended = __result.ToList();
        extended.Add(ModelDb.Modifier<{modifier_full_type}>());
        __result = extended.AsReadOnly();
    }}
}}
