using BaseLib.Utils;

namespace {namespace}.Fields;

/// <summary>
/// Attach data to {target_type} instances without modifying the class.
/// Get: {class_name}.{field_name}.Get(instance)
/// Set: {class_name}.{field_name}.Set(instance, value)
/// </summary>
public static class {class_name}
{{
    public static readonly SpireField<{target_type}, {field_type}> {field_name}
        = new(() => {default_value});
}}
