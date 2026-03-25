# BaseLib (Alchyr.Sts2.BaseLib) - Community Modding Library

**Source:** E:\Github\BaseLib-StS2 | **NuGet:** Alchyr.Sts2.BaseLib | **Version:** 0.1.9+

## CRITICAL: C# Namespaces

The NuGet package is called `Alchyr.Sts2.BaseLib` but the **actual C# namespaces** are different:

| What you need | C# using statement |
|---|---|
| Custom model base classes (CustomCardModel, CustomRelicModel, CustomPotionModel, CustomPowerModel, CustomCharacterModel) | `using BaseLib.Abstracts;` |
| Custom pool models (CustomCardPoolModel, CustomRelicPoolModel, CustomPotionPoolModel) | `using BaseLib.Abstracts;` |
| `[Pool]` attribute (PoolAttribute) | `using BaseLib.Utils;` |
| Card variables (ExhaustiveVar, PersistVar, RefundVar) | `using BaseLib.Cards.Variables;` |
| CommonActions helpers | `using BaseLib.Utils.CommonActions;` |
| SpireField, WeightedList, GeneratedNodePool | `using BaseLib.Utils;` |
| IL Patching (InstructionMatcher) | `using BaseLib.Utils.Patching;` |
| Mod interop | `using BaseLib.Utils.ModInterop;` |

**Do NOT use `using Alchyr.Sts2.BaseLib.*;`** — those namespaces don't exist in the DLL.

## What it provides
- **Abstract base classes**: CustomCardModel, CustomRelicModel, CustomPowerModel, CustomPotionModel, CustomCharacterModel, CustomAncientModel
- **Pool models**: CustomCardPoolModel, CustomRelicPoolModel, CustomPotionPoolModel (with custom frames, energy icons)
- **Config system**: SimpleModConfig with auto-generated in-game UI (toggles, sliders, dropdowns)
- **Card variables**: ExhaustiveVar, PersistVar, RefundVar
- **CommonActions**: Helper methods for damage, block, draw, apply powers, card selection
- **Utilities**: SpireField (attach data to objects), WeightedList, GeneratedNodePool, ShaderUtils
- **IL Patching**: InstructionMatcher/InstructionPatcher for advanced Harmony transpilers
- **Mod Interop**: Soft-depend on other mods without hard references

## Key Benefits over raw game API
1. **Auto-registration** - ICustomModel types get prefixed IDs and registered automatically (prefixed with mod namespace, e.g. `MYMOD-CARD_NAME`)
2. **Custom content support** - Proper image/icon loading for powers, cards, relics
3. **Config persistence** - JSON config with auto-UI, no manual UI code needed
4. **Character creation** - Full pipeline for new playable characters with pools
5. **Convenience methods** - CommonActions reduces boilerplate for damage/block/draw
6. **Pool auto-registration** - Cards/relics/potions with `[Pool(typeof(MyPool))]` are auto-added to pools; no need for manual `GenerateAllCards()` overrides

## Add to .csproj
```xml
<PackageReference Include="Alchyr.Sts2.BaseLib" Version="0.1.*" />
```

## Required [Pool] Attributes

**Every** CustomCardModel, CustomRelicModel, and CustomPotionModel **must** have a `[Pool]` attribute. Without it, BaseLib throws a runtime exception during ModelDb initialization:
```
System.Exception: Model MyMod.MyCard must be marked with a PoolAttribute to determine which pool to add it to.
```
This includes curse cards (`[Pool(typeof(CurseCardPool))]`) and status cards (`[Pool(typeof(StatusCardPool))]`). Powers do NOT need pool attributes.
