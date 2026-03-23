# Troubleshooting

## Setup Issues

### "Decompiled source not found"
The `decompiled/` directory is empty or missing. Run:
```bash
ilspycmd -p -o ./decompiled "<game_dir>/data_sts2_windows_x86_64/sts2.dll"
```
Or use the `decompile_game` tool after connecting the MCP.

If `ilspycmd` isn't found: `dotnet tool install -g ilspycmd`

### "sts2.dll not found"
The `STS2_GAME_DIR` environment variable points to the wrong location, or the game isn't installed.
Default path: `E:\SteamLibrary\steamapps\common\Slay the Spire 2`

Check: `<game_dir>/data_sts2_windows_x86_64/sts2.dll` should exist.

### "dotnet CLI not found"
Install .NET SDK 9.0 from https://dotnet.microsoft.com/download

### Server won't start or connect
1. Check Python version: `python --version` (needs 3.11+)
2. Check dependencies are installed: `pip install .` (from the project root)
3. Test directly: `python run.py` — should start without errors
4. Check your `.mcp.json` or `settings.json` path is correct (use forward slashes)

## Build Issues

### "Build failed — assembly reference errors"
The mod can't find sts2.dll. Set the `STS2_GAME_DIR` environment variable to your game install path, or pass it to dotnet build:
```bash
dotnet build /p:Sts2GameDir="/path/to/Slay the Spire 2"
```
The csproj auto-detects the platform-specific data subfolder (`data_sts2_windows_x86_64`, `data_sts2_linuxbsd_x86_64`, `data_sts2_macos_arm64`). The resolved path must contain `sts2.dll`.

### "Type or namespace not found"
Common causes:
- Missing `using` statement — check the generated code's imports
- Wrong .NET target — must be `net9.0`
- Missing NuGet packages — run `dotnet restore`
- BaseLib reference missing — add `<PackageReference Include="Alchyr.Sts2.BaseLib" Version="0.1.*" />`

### "Hook method signature mismatch"
The game updated and a hook's parameters changed. Use:
```
get_hook_signature "HookName"
```
to get the current signature, then update your override.

### "EnableDynamicLoading not set"
Your `.csproj` must include:
```xml
<EnableDynamicLoading>true</EnableDynamicLoading>
```
Without this, the game can't load the mod DLL.

## In-Game Issues

### Mod doesn't appear in the mod list
1. Check `mods/<modname>/mod_manifest.json` exists and is valid JSON
2. Required manifest keys: `id`, `name`, `author`, `version`, `has_dll`
3. The DLL filename must match what `has_dll` expects
4. Check the Godot log for load errors

### Mod loads but content doesn't appear
- **Cards/relics/potions:** Check `[Pool(typeof(PoolName))]` attribute is present
- **Monsters:** Check `generate_create_visuals_patch` was applied
- **Encounters:** Check the act encounter patch was applied
- **Events:** Events need a patch to add them to an act's event pool
- **Modifiers:** Need the registration patch on `ModelDb.get_GoodModifiers`/`get_BadModifiers`
- **Localization:** Check JSON files are in `<ModName>/localization/eng/` and `has_pck: true` in manifest

### Null reference exception in hooks
- Models must be accessed after initialization — don't read `ModelDb` in static constructors
- `Owner` may be null if the entity isn't attached to a player yet
- `CombatManager.Instance` is null outside of combat
- Always null-check `player.PlayerCombatState` — it doesn't exist outside combat

### PCK not loading
- `pck_name` in manifest must match the actual `.pck` filename (without extension)
- `has_pck` must be `true` in manifest
- The PCK must be in the same directory as the manifest

## Bridge Issues

### "Bridge not running"
- Game must be open AND past the loading screen
- MCPTest mod must be installed and enabled
- Only one game instance can use port 21337

### "Bridge timed out"
- Game may be loading or in a transition
- State queries run on the main thread — if the game is stuck, so is the bridge
- Try `bridge_ping` first

### "Unknown method" from bridge
- The C# BridgeHandler doesn't have a handler for this method
- This means the bridge mod needs updating (rebuild test_mod)

### Actions don't work
- Check `bridge_get_screen` — you might be on the wrong screen
- Check `bridge_get_available_actions` — the action might not be legal
- Combat actions only work during `COMBAT_PLAYER_TURN`
- Map navigation only works on `MAP` screen

## Common Code Patterns

### Async methods without await
If your hook method is `async Task` but doesn't use `await`, add:
```csharp
await Task.CompletedTask;
```
Or remove the `async` keyword and return `Task.CompletedTask`.

### Flash() not showing
`Flash()` only works on relics and powers that are attached to a player in combat.
Make sure `Owner` is set and combat is in progress.

### Console commands not working
- The console is enabled automatically when any mod is loaded
- Command IDs use SCREAMING_SNAKE_CASE (e.g., `BURNING_BLOOD` not `BurningBlood`)
- Use `get_console_commands` to see the full command list with argument formats

## Log Locations

- **Godot log:** `%APPDATA%/Godot/app_userdata/Slay the Spire 2/logs/godot.log`
- **Mod log:** Check in-game console output or the Godot log
- **MCPTest bridge log:** Written to Godot log with `[MCPTest]` prefix
- **Settings:** `%APPDATA%/Godot/app_userdata/Slay the Spire 2/settings.save`
- **Save files:** `%APPDATA%/Godot/app_userdata/Slay the Spire 2/saves/`
