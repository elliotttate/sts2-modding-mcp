using System.Collections.Generic;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;

namespace {namespace}.Tooltips;

/// <summary>
/// Provides a reusable hover tip for the custom keyword "{title}".
/// </summary>
public static class {class_name}Tooltip
{{
    public const string Table = "tooltips";
    public const string TitleKey = "{tooltip_key}.title";
    public const string DescriptionKey = "{tooltip_key}.description";

    public static HoverTip Create()
    {{
        return new HoverTip(
            new LocString(Table, TitleKey),
            new LocString(Table, DescriptionKey));
    }}

    public static IEnumerable<IHoverTip> AsSingleTip()
    {{
        yield return Create();
    }}
}}
