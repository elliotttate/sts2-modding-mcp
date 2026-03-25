using System.Collections.Generic;
using BaseLib;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Models.Cards;

namespace {namespace}.Keywords;

/// <summary>
/// Custom keyword: {keyword_name}
/// Use in card descriptions: [color=#FFD700]{keyword_name}[/color]
/// Add to cards via: Keywords = new HashSet<CardKeyword> {{ {keyword_field}.CustomType }};
/// </summary>
public static class {keyword_field}
{{
    [CustomEnum]
    public static CardKeyword CustomType;
}}
