using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;

namespace {namespace}.Piles;

/// <summary>
/// Custom card pile: {pile_name}
/// Route cards here by overriding GetResultPileType or patching card destination.
/// </summary>
public static class {class_name}
{{
    [CustomEnum]
    public static PileType CustomType;
}}
