# Getting Started with STS2 Modding

## Prerequisites
- .NET SDK 9.0+
- Godot 4.5.1 (for PCK export only)
- The game: Slay the Spire 2

## Quick Start
1. Use `create_mod_project` to scaffold a new mod
2. Add content using `generate_card`, `generate_relic`, etc.
3. Build with `build_mod`
4. Install with `install_mod`
5. Test in-game with the developer console (backtick key)

## Key Concepts
- **sts2.dll**: The game's compiled C# code at `data_sts2_windows_x86_64/sts2.dll`
- **ModInitializer**: Attribute marking your mod's entry point class
- **Harmony**: Runtime method patching library for hooking into game code
- **Hooks**: The game's built-in event system (80+ hooks for combat, cards, etc.)
- **ModelDb**: Central registry for all game entities (auto-discovers via reflection)
- **Pools**: Collections that determine where entities appear (card pools, relic pools, etc.)

## Mod Structure
```
MyMod/
├── MyMod.csproj           # .NET project file referencing sts2.dll
├── mod_manifest.json      # Mod metadata (id, name, author, version)
├── Code/
│   ├── ModEntry.cs        # [ModInitializer] entry point
│   ├── Cards/             # Custom card classes
│   ├── Relics/            # Custom relic classes
│   ├── Powers/            # Custom power classes
│   ├── Potions/           # Custom potion classes
│   ├── Monsters/          # Custom monster classes
│   ├── Encounters/        # Custom encounter classes
│   └── Patches/           # Harmony patches
├── MyMod/
│   ├── localization/eng/  # Localization JSON files
│   ├── images/            # Entity images (256x256 for relics/powers)
│   └── MonsterResources/  # Monster scenes and sprites
└── mod_image.png          # Mod icon for the mod list
```

## Enabling the Console
A loaded mod automatically enables the full console. Press backtick (`) in-game.
Or manually: edit settings.save, add `"full_console": true` after `fps_limit`.
