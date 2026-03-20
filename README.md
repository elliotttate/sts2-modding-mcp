# STS2 Modding MCP

A comprehensive [Model Context Protocol](https://modelcontextprotocol.io/) server for **Slay the Spire 2** modding. Reverse-engineers the game's C# assemblies and provides 26 tools for querying game data, generating mod code, building, and deploying mods — all accessible from AI assistants like Claude Code.

## What It Does

STS2 uses a modified **Godot 4.5.1 C#** engine with **.NET 9.0**. This MCP server decompiles `sts2.dll` and indexes the entire game, giving you instant access to:

- **3,015 game entities** — 577 cards, 290 relics, 260 powers, 64 potions, 121 monsters, 88 encounters, 68 events, 23 enchantments, and more
- **136 game hooks** — before/after events, value modifiers, boolean gates for combat, cards, damage, powers, turns, rewards, etc.
- **39 console commands** — in-game developer commands for testing
- **Full decompiled source** — searchable C# source for every class in the game

It also generates production-ready mod code using [BaseLib](https://www.nuget.org/packages/Alchyr.Sts2.BaseLib) (Alchyr's community modding library) by default, with fallback to the raw game API.

## Tools (26)

### Game Data Query (8 tools)

| Tool | Description |
|------|-------------|
| `list_entities` | Search/filter entities by type, name, rarity. Types: card, relic, potion, power, monster, encounter, event, enchantment, character, orb, act, etc. |
| `get_entity_source` | Get full decompiled C# source for any game class (cards, base classes, hooks, combat system, etc.) |
| `search_game_code` | Regex search through all decompiled source (~23MB, 1300+ files) |
| `list_hooks` | List game hooks filtered by category (before/after/modify/should) and subcategory (card/damage/power/turn/etc.) |
| `get_game_info` | Game version, paths, entity counts, namespace overview |
| `get_console_commands` | All 39 dev console commands with args and descriptions |
| `browse_namespace` | Navigate decompiled namespaces and read individual files |
| `get_modding_guide` | Built-in documentation: 16 topics from getting started to debugging |

### Mod Creation (12 tools)

| Tool | Description |
|------|-------------|
| `create_mod_project` | Scaffold a complete mod project (.csproj, ModEntry, manifest, localization, image dirs) |
| `generate_card` | Generate a card class with dynamic vars, OnPlay logic, upgrade logic, and localization |
| `generate_relic` | Generate a relic class with hook methods and localization |
| `generate_power` | Generate a power (buff/debuff) class with hook methods, ICustomPower icons |
| `generate_potion` | Generate a potion class with OnUse logic and localization |
| `generate_monster` | Generate a monster class with move state machine, .tscn scene file, and localization |
| `generate_encounter` | Generate an encounter class that spawns specific monsters |
| `generate_harmony_patch` | Generate a Harmony prefix/postfix patch class |
| `generate_localization` | Generate localization JSON with SmartFormat support |
| `generate_character` | Generate a full custom playable character with card/relic/potion pools (BaseLib) |
| `generate_mod_config` | Generate a config class with auto-generated in-game settings UI (BaseLib) |
| `get_baselib_reference` | Documentation for 15 BaseLib topics: CommonActions, SpireField, WeightedList, IL patching, etc. |

### Build & Deploy (5 tools)

| Tool | Description |
|------|-------------|
| `build_mod` | Build via `dotnet build` with output capture |
| `install_mod` | Copy DLL, manifest, PCK to the game's mods folder |
| `uninstall_mod` | Remove a mod from the game |
| `list_installed_mods` | Show installed mods with manifest data |
| `launch_game` | Launch STS2 with optional remote debug (Godot port 6007) |

### Maintenance (1 tool)

| Tool | Description |
|------|-------------|
| `decompile_game` | Re-decompile `sts2.dll` after a game update (requires `ilspycmd`) |

## Prerequisites

- **Python 3.11+**
- **[mcp](https://pypi.org/project/mcp/) package** — `pip install "mcp[cli]"`
- **[ilspycmd](https://www.nuget.org/packages/ilspycmd/)** — `dotnet tool install -g ilspycmd` (for initial decompilation)
- **.NET SDK 9.0** — for building mods
- **Slay the Spire 2** — the game itself

## Setup

### 1. Clone and install

```bash
git clone https://github.com/YOUR_USERNAME/sts2-modding-mcp.git
cd sts2-modding-mcp
pip install "mcp[cli]"
```

### 2. Decompile the game

The first time, you need to decompile `sts2.dll` to populate the `decompiled/` directory:

```bash
ilspycmd -p -o ./decompiled "E:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll"
```

Or use the `decompile_game` tool after connecting the MCP.

### 3. Configure paths

By default the server looks for the game at `E:\SteamLibrary\steamapps\common\Slay the Spire 2`. Override with environment variables:

```bash
export STS2_GAME_DIR="/path/to/Slay the Spire 2"
export STS2_DECOMPILED_DIR="/path/to/decompiled"
```

### 4. Register with Claude Code

**Option A — Project scope** (`.mcp.json` in your working directory):

```json
{
  "mcpServers": {
    "sts2-modding": {
      "command": "python",
      "args": ["E:/Github/sts2-modding-mcp/run.py"]
    }
  }
}
```

**Option B — User scope** (`~/.claude/settings.json` under `mcpServers`):

```json
{
  "mcpServers": {
    "sts2-modding": {
      "command": "python",
      "args": ["E:/Github/sts2-modding-mcp/run.py"]
    }
  }
}
```

Restart Claude Code and the server should appear in `/mcp`.

## Usage Examples

Once connected, you can ask Claude things like:

- *"Show me the source code for the Bash card"* → uses `get_entity_source`
- *"List all rare attack cards"* → uses `list_entities`
- *"How does the damage hook system work?"* → uses `list_hooks` + `get_entity_source`
- *"Create a new mod called FlameForge with a card that deals 20 damage and applies 2 Vulnerable"* → uses `create_mod_project` + `generate_card`
- *"Generate a relic that gives 3 Strength the first time you take damage each combat"* → uses `generate_relic`
- *"Build my mod and install it"* → uses `build_mod` + `install_mod`
- *"What BaseLib utilities are available for card effects?"* → uses `get_baselib_reference`

## BaseLib Integration

All code generation defaults to using [BaseLib](https://github.com/Alchyr/BaseLib-StS2) (`Alchyr.Sts2.BaseLib`), which provides:

- **Abstract base classes** — `CustomCardModel`, `CustomRelicModel`, `CustomPowerModel`, `CustomPotionModel`, `CustomCharacterModel`
- **Auto-registration** — ICustomModel types get prefixed IDs and pool registration automatically
- **Config system** — `SimpleModConfig` with auto-generated in-game UI
- **Card variables** — ExhaustiveVar, PersistVar, RefundVar
- **CommonActions** — Helper methods for damage, block, draw, apply powers
- **Utilities** — SpireField, WeightedList, IL patching tools

Set `use_baselib: false` on any generation tool to get raw game API code instead.

## Project Structure

```
sts2-modding-mcp/
├── run.py                  # Entry point
├── pyproject.toml          # Package metadata
├── requirements.txt        # Dependencies
├── sts2mcp/
│   ├── __init__.py
│   ├── server.py           # MCP server with all 26 tool definitions
│   ├── game_data.py        # Game data indexing and querying
│   └── mod_gen.py          # Code generation templates and mod building
└── decompiled/             # Decompiled game source (gitignored, ~23MB)
```

## Generated Mod Structure

When you use `create_mod_project`, it creates:

```
MyMod/
├── MyMod.csproj                    # .NET 9.0 + BaseLib + Harmony
├── mod_manifest.json               # Mod metadata
├── Code/
│   ├── ModEntry.cs                 # [ModInitializer] entry point
│   ├── Cards/                      # Custom cards
│   ├── Relics/                     # Custom relics
│   ├── Powers/                     # Custom powers
│   ├── Potions/                    # Custom potions
│   ├── Monsters/                   # Custom monsters
│   ├── Encounters/                 # Custom encounters
│   ├── Characters/                 # Custom characters (BaseLib)
│   ├── Config/                     # Mod config (BaseLib)
│   └── Patches/                    # Harmony patches
└── MyMod/
    ├── localization/eng/           # Localization JSON files
    ├── images/                     # Entity images
    └── MonsterResources/           # Monster scenes and sprites
```

## Updating After Game Patches

When STS2 updates, run the `decompile_game` tool (or manually re-run `ilspycmd`) to refresh the decompiled source. The index rebuilds automatically on the next query.

## License

MIT
