# BaseLib (Alchyr.Sts2.BaseLib) - Community Modding Library

**Source:** E:\Github\BaseLib-StS2 | **NuGet:** Alchyr.Sts2.BaseLib | **Version:** 0.1.6

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
1. **Auto-registration** - ICustomModel types get prefixed IDs and registered automatically
2. **Custom content support** - Proper image/icon loading for powers, cards, relics
3. **Config persistence** - JSON config with auto-UI, no manual UI code needed
4. **Character creation** - Full pipeline for new playable characters with pools
5. **Convenience methods** - CommonActions reduces boilerplate for damage/block/draw

## Add to .csproj
```xml
<PackageReference Include="Alchyr.Sts2.BaseLib" Version="0.1.*" />
```
