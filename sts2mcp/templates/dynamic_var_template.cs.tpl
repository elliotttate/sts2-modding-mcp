using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Vars;

/// <summary>
/// Custom dynamic variable for use in card/power descriptions.
/// Reference in localization: {{{{var_name}}}} (use SmartFormat syntax).
/// Add to CanonicalVars: new {class_name}({default_value})
/// </summary>
public class {class_name} : DynamicVar
{{
    public {class_name}(decimal baseValue, ValueProp valueProp = ValueProp.Move)
        : base("{var_name}", baseValue, valueProp)
    {{
    }}
}}
