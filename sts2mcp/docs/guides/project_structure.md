# Project Structure Reference

## Recommended Layout
```
MyMod/
├── MyMod.csproj                    # .NET 9.0 project
├── NuGet.config                    # Package source config written by create_mod_project
├── mod_manifest.json               # Mod metadata
├── mod_image.png                   # Mod icon (optional)
├── Code/
│   ├── ModEntry.cs                 # [ModInitializer] entry point
│   ├── Cards/
│   │   └── MyCard.cs
│   ├── Relics/
│   │   └── MyRelic.cs
│   ├── Powers/
│   │   └── MyPower.cs
│   ├── Potions/
│   │   └── MyPotion.cs
│   ├── Config/
│   │   └── ModSettings.cs          # Optional generated settings panel
│   ├── Tooltips/
│   │   └── MyKeywordTooltip.cs     # Optional localization-backed hover tip helper
│   ├── Monsters/
│   │   └── MyMonster.cs
│   ├── Encounters/
│   │   └── MyEncounter.cs
│   └── Patches/
│       ├── CreateVisualsPatch.cs   # Required for custom monsters
│       └── MyPatches.cs
└── MyMod/                          # Resource folder (matches pck_name)
    ├── localization/
    │   └── eng/
    │       ├── cards.json
    │       ├── relics.json
    │       ├── powers.json
    │       ├── potions.json
    │       ├── tooltips.json
    │       ├── monsters.json
    │       └── encounters.json
    ├── images/
    │   ├── card_portraits/         # Card art (see sizes below)
    │   │   ├── big/                # Full card art: 606x852
    │   │   └── beta/               # Beta art (same sizes)
    │   ├── relics/                 # Small: 64x64, big/: 256x256, plus _outline variants
    │   ├── powers/                 # Small: 64x64, big/: 256x256
    │   ├── charui/                 # Character UI (energy icons, select screen, map markers)
    │   └── potions/
    └── MonsterResources/
        └── MyMonster/
            ├── my_monster.tscn     # Godot scene
            └── my_monster.png      # Sprite
```

## Image Size Reference

| Asset Type | Small / Packed | Big / Full |
|---|---|---|
| Card portrait (normal) | 250x190 | 500x380 (display) / 1000x760 (high-res) |
| Card portrait (full art) | 250x350 | 606x852 |
| Relic icon | 64x64 (+outline) | 256x256 |
| Power icon | 64x64 | 256x256 |

Card images go in `card_portraits/` (small) and `card_portraits/big/` (full art). Beta art goes in `card_portraits/beta/`.

## .csproj Key Settings

**Using Alchyr's NuGet templates** (recommended):
- SDK: `Godot.NET.Sdk/4.5.1`
- `TargetFramework`: `net9.0`
- `AllowUnsafeBlocks`: `true`
- `ImplicitUsings`: `true`
- Reference to `sts2.dll` with `Private=false` and optional `<Publicize>true</Publicize>`
- Reference to `0Harmony.dll` with `Private=false`
- `<PackageReference Include="Alchyr.Sts2.BaseLib" Version="*" />`
- `<PackageReference Include="Alchyr.Sts2.ModAnalyzers" Version="*" />` (code analyzers)
- Auto-copies DLL to mods folder on build
- Platform-aware path detection (Windows/Linux/macOS)

**Without templates** (MCP-scaffolded):
- SDK: `Microsoft.NET.Sdk`
- `TargetFramework`: `net9.0`
- `Nullable`: `enable`
- `ImplicitUsings`: `disable`
- NuGet reference to `Lib.Harmony`
- Reference to `sts2.dll` with `Private=false`

## Asset Path Extensions (Template Convention)

The NuGet templates include `StringExtensions` helper methods for building asset paths:
```csharp
"card.png".CardImagePath()     → "ModId/images/card_portraits/card.png"
"card.png".BigCardImagePath()  → "ModId/images/card_portraits/big/card.png"
"power.png".PowerImagePath()   → "ModId/images/powers/power.png"
"relic.png".RelicImagePath()   → "ModId/images/relics/relic.png"
"icon.png".CharacterUiPath()   → "ModId/images/charui/icon.png"
```
These return `res://`-compatible paths for use in portrait/icon overrides.

## Notes

- The resource folder should match `pck_name` from `mod_manifest.json`
- Build output normally lands under `bin/<Configuration>/net9.0/`
- Deployment may include more than one managed artifact if the project pulls in helper assemblies
