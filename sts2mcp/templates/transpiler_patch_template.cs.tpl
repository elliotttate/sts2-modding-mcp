using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace {namespace}.Patches;

/// <summary>
/// IL Transpiler patch for {target_type}.{target_method}.
/// {description}
/// </summary>
[HarmonyPatch(typeof({target_type}), "{target_method}")]
public static class {class_name}
{{
    public static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        ILGenerator generator)
    {{
        var codes = new List<CodeInstruction>(instructions);
        bool patched = false;

        for (int i = 0; i < codes.Count; i++)
        {{
            // Example: Find a specific method call and insert code before/after it
            if (codes[i].opcode == OpCodes.{search_opcode}
                && codes[i].operand is MethodInfo method
                && method.Name == "{search_method}")
            {{
                // Insert your modification here
                // Example: Insert a call to your static method before the target
                codes.Insert(i, new CodeInstruction(
                    OpCodes.Call,
                    AccessTools.Method(typeof({class_name}), nameof(Modify))));
                patched = true;
                break;
            }}
        }}

        if (!patched)
            Log.Warn("[{mod_id}] Transpiler failed to find target in {target_type}.{target_method}");

        return codes;
    }}

    /// <summary>
    /// Called from the transpiled code. Modify values on the stack here.
    /// </summary>
    public static void Modify()
    {{
        // TODO: Implement your modification logic
    }}
}}
