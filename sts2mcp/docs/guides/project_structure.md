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
    │   ├── relics/                 # 256x256 with outline
    │   ├── powers/                 # 256x256 with outline
    │   ├── cards/                  # 1000x760 (606x852 for Ancient)
    │   └── potions/
    └── MonsterResources/
        └── MyMonster/
            ├── my_monster.tscn     # Godot scene
            └── my_monster.png      # Sprite
```

## .csproj Key Settings
- SDK: `Microsoft.NET.Sdk`
- `TargetFramework`: `net9.0`
- `Nullable`: `enable`
- `ImplicitUsings`: `disable`
- NuGet reference to `Lib.Harmony`
- Reference to `sts2.dll` with `Private=false`

## Notes

- The resource folder should match `pck_name` from `mod_manifest.json`
- Build output normally lands under `bin/<Configuration>/net9.0/`
- Deployment may include more than one managed artifact if the project pulls in helper assemblies
