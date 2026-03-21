using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.RichTextTags;

namespace {namespace}.Keywords;

/// <summary>
/// Registers the custom keyword "{keyword_name}" with tooltip.
/// Call RegisterKeyword() from your ModEntry.Init().
/// </summary>
public static class {class_name}Keyword
{{
    public const string TAG = "{tag_name}";

    public static void RegisterKeyword()
    {{
        // Register rich text tag for use in descriptions: [{tag_name}]{keyword_name}[/{tag_name}]
        // This requires a Harmony patch on the rich text system
        // See the generated patch class for implementation
    }}
}}
