using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace {namespace}.Utils;

/// <summary>
/// Cached reflection accessors for {target_type} private members.
/// Access: {class_name}.Get{first_field}(instance)
/// </summary>
public static class {class_name}
{{
{field_accessors}

    static {class_name}()
    {{
        // Validate all fields were found
{validation}
    }}
}}
