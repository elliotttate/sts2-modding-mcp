# Building & Deploying Mods

## Built-in Mods (Zero Setup)

The MCP ships with two companion mods (`test_mod/` and `explorer_mod/`) that are automatically built and installed into the game's `mods/` folder on server startup. They are rebuilt when source changes are detected. No manual steps are needed.

## Standard Build & Deploy Flow

```
1. validate_mod(project_dir)     → Catch issues before building
2. build_mod(project_dir)        → Compile C# code with dotnet build
3. build_pck(source_dir, output) → Pack images/scenes/localization into .pck (if needed)
4. install_mod(project_dir)      → Copy DLL, manifest, PCK, images to game's mods/ folder
5. launch_game()                 → Test in-game
```

## Building

`build_mod` runs `dotnet build` on your project. It reads `.csproj` and `mod_manifest.json` to determine configuration.

```
build_mod(project_dir="E:/mods/MyMod")
```

Build output goes to `bin/Debug/net9.0/` by default. The main artifact is your mod's DLL.

## PCK Building

Use `build_pck` when your mod includes resources that need to be loadable via Godot's `res://` paths:
- Localization JSON files (`localization/eng/*.json`)
- Card/relic/power images
- Monster scene files (`.tscn`)
- VFX scenes
- Any other Godot-loadable resource

```
build_pck(
    source_dir="E:/mods/MyMod/MyMod",    # The resource root folder
    output_path="E:/mods/MyMod/mymod.pck",
    res_prefix="MyMod"                     # Maps to res://MyMod/...
)
```

The PCK builder is pure Python — no Godot install needed. It:
- Converts `.png` images to `.ctex` format with `.import` remaps
- Packs `.tscn`, `.json`, `.tres` files as-is
- Generates the Godot PCK header and file table

## Installation

`install_mod` copies built artifacts to the game's `mods/` folder:

```
install_mod(project_dir="E:/mods/MyMod")
```

This copies: the main DLL, portable PDB when present, `.deps.json`, `.runtimeconfig.json`, `mod_manifest.json`, optional `.pck`, and `mod_image.png`.

## Installation Layout

The deployed mod folder should look like:

```text
mods/
└── mymod/
    ├── MyMod.dll            # Main assembly
    ├── MyMod.pdb            # Debug symbols (Debug builds)
    ├── MyMod.deps.json      # Managed dependency metadata
    ├── MyMod.runtimeconfig.json
    ├── mymod.pck            # Optional resource pack
    ├── mod_manifest.json    # Required metadata
    └── mod_image.png        # Optional mod icon
```

## Validation

Run `validate_mod` before building to catch common issues:

```
validate_mod(project_dir="E:/mods/MyMod")
```

Checks: manifest exists, .csproj references, [ModInitializer] present, localization coverage, Harmony patch validity, async correctness.

## Build vs Publish (Template Projects)

If using the generated `.csproj` with Rider/Visual Studio, there are two workflows:

- **Build** (hammer icon / `dotnet build`): Fast — only compiles C# code to `.dll`. Use during development when you haven't changed resource files.
- **Publish** (right-click → Publish → Local folder): Compiles C# code AND exports the Godot `.pck` via headless Godot (`--export-pack`). **Required whenever you modify resource files** (images, localization, scenes).

Generated templates now include:

- Explicit Debug settings: `Optimize=false`, `DebugType=portable`
- Explicit Release settings: `Optimize=true`, `DebugType=none`, `PathMap=$(MSBuildProjectDirectory)=.`
- An optional `CopyToModsFolderOnBuild` target that can copy the built DLL, PDB, `.deps.json`, `.runtimeconfig.json`, and `mod_manifest.json` into the game's `mods/` folder

The copy target is disabled by default. Enable it in the `.csproj` when you want IDE builds to deploy directly:

```xml
<EnableCopyToModsFolderOnBuild>true</EnableCopyToModsFolderOnBuild>
```

By default the template derives `ModsPath` from `Sts2Dir`. Override it explicitly if your layout differs:

```xml
<ModsPath>E:\SteamLibrary\steamapps\common\Slay the Spire 2\mods</ModsPath>
```

For repeated in-process reloads, still prefer `hot_reload_project` or `watch_project`; those add unique assembly/version stamping needed by the default assembly load context.

## Manual Fallback

If debugging outside the MCP workflow:

1. `dotnet build YourMod.csproj -c Debug`
2. Copy the DLL and PDB from `bin/Debug/net9.0/` to `mods/<mod_id>/`
3. Copy `mod_manifest.json`
4. Copy the `.pck` file (if any)
5. Launch the game and verify the mod loads
