using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace {namespace}.Patches;

[HarmonyPatch(typeof({target_type}), nameof({target_type}.{target_method}))]
public static class {class_name}
{{
    public static {patch_type} {patch_method}({params})
    {{
{body}
    }}
}}
