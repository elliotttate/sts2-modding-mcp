# STS2 Modding MCP

A comprehensive [Model Context Protocol](https://modelcontextprotocol.io/) server for **Slay the Spire 2** modding. Reverse-engineers the game's C# assemblies and Godot PCK assets, providing **147 MCP tools** for querying game data, generating mod code, applying it into real projects, building/deploying mods, and driving in-game playtests from AI assistants like Claude Code.

> **New in v3.1:** Roslyn-based C# code analysis replaces regex parsing — 3,048 classes with full syntax trees, 18,279 methods with call graphs, 135 enums, inverted indexes for O(1) type/override/invocation lookups, and a new `suggest_hooks` tool for intent-based hook recommendations.

## What It Does

STS2 uses a modified **Godot 4.5.1 C#** engine with **.NET 9.0**. This MCP server decompiles `sts2.dll` and extracts the Godot PCK, indexing the entire game from both sides, giving you instant access to:

- **3,048 game entities** — 577 cards, 290 relics, 261 powers, 64 potions, 119 monsters, 88 encounters, 67 events, 23 enchantments, and more
- **144 game hooks + 175 overridable methods** — before/after events, value modifiers, boolean gates for combat, cards, damage, powers, turns, rewards, etc.
- **39 console commands** — in-game developer commands for testing
- **Full decompiled source** — searchable C# source for every class in the game, indexed by a Roslyn syntax tree analyzer with call graphs, type references, and inheritance chains
- **15,000+ Godot assets** — every scene, texture, resource, script, and audio file in the game PCK, searchable by path

It also generates production-ready mod code using [BaseLib](https://www.nuget.org/packages/Alchyr.Sts2.BaseLib) (Alchyr's community modding library) by default, with fallback to the raw game API.

At the library level, `sts2mcp.mod_gen.ModGenerator` also now includes project-aware helpers for applying generated output into an existing mod project, building PCKs from the project manifest/resource layout, deploying built artifacts, and validating localization plus project-owned asset references.

## Tool Highlights

The MCP currently exposes 147 tools. The sections below highlight the main workflows and the newer complex-mod helpers rather than exhaustively listing every advanced scaffold one by one.

### Game Data Query

| Tool | Description |
|------|-------------|
| `list_entities` | Search/filter entities by type, name, rarity. Types: card, relic, potion, power, monster, encounter, event, enchantment, character, orb, act, etc. |
| `get_entity_source` | Get full decompiled C# source for any game class (cards, base classes, hooks, combat system, etc.) |
| `search_game_code` | Search decompiled source — uses Roslyn indexes for instant type/override/invocation lookups, falls back to regex for arbitrary patterns |
| `list_hooks` | List game hooks filtered by category (before/after/modify/should) and subcategory (card/damage/power/turn/etc.) |
| `get_game_info` | Game version, paths, entity counts, namespace overview |
| `get_console_commands` | All 39 dev console commands with args and descriptions |
| `browse_namespace` | Navigate decompiled namespaces and read individual files |
| `get_modding_guide` | Built-in documentation for 28 topics including getting started, hooks, localization, debugging, multiplayer networking, Godot UI, reflection, advanced Harmony, combat deep dive, and more |

### Core Mod Creation

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
| `get_baselib_reference` | Documentation for BaseLib topics such as CommonActions, SpireField, WeightedList, IL patching, and utilities |

### Build, Deploy, And Project Workflow

| Tool | Description |
|------|-------------|
| `inspect_mod_project` | Infer namespace, assembly name, PCK name, resource root, and localization layout from an existing project |
| `apply_generated_output` | Write generator output into an existing mod project, merge localization, and apply supported project edits transactionally |
| `build_mod` | Build via `dotnet build` with output capture, artifact listing, and optional project PCK generation |
| `build_project_pck` | Build a `.pck` directly from the project's manifest/resource layout |
| `install_mod` | Copy built artifacts, manifest, optional PCK, and mod image to the game's mods folder |
| `deploy_mod` | Validate, build, optionally pack, and deploy a mod in one project-aware call |
| `validate_mod_assets` | Validate broken `res://` references under the project-owned resource tree |
| `validate_mod_project` | Run combined localization and asset validation before shipping/testing |
| `uninstall_mod` | Remove a mod from the game |
| `list_installed_mods` | Show installed mods with manifest data |
| `launch_game` | Launch STS2 with optional remote debug (Godot port 6007) |

### Live Bridge And Playtesting

| Tool | Description |
|------|-------------|
| `bridge_start_run` | Start seeded test runs with optional fixtures, modifiers, cards, relics, powers, and event/fight setup |
| `bridge_get_available_actions` | Discover all currently legal combat and non-combat actions |
| `bridge_execute_action` | Execute screen-aware non-combat actions such as map travel, event choices, rewards, shops, rest sites, treasure, and card-selection flows |
| `bridge_wait_for_screen` | Wait until a requested screen becomes active and stable |
| `bridge_wait_until_idle` | Wait until the bridge state stops loading/changing between polls |
| `bridge_get_diagnostics` | Return current screen metadata plus recent bridge/runtime logs |
| `bridge_tail_log` | Return recent MCPTest bridge log lines |
| `bridge_get_last_errors` | Return recent bridge error/failure lines |

The existing bridge combat tools are still available alongside these helpers: `bridge_ping`, `bridge_get_screen`, `bridge_get_run_state`, `bridge_get_combat_state`, `bridge_get_player_state`, `bridge_get_map_state`, `bridge_play_card`, `bridge_end_turn`, `bridge_console`, `bridge_use_potion`, `bridge_make_event_choice`, `bridge_navigate_map`, `bridge_rest_site_choice`, `bridge_shop_action`, `bridge_get_card_piles`, and `bridge_manipulate_state`.

### Code Intelligence And Validation

| Tool | Description |
|------|-------------|
| `suggest_hooks` | **New** — Given a modding intent (e.g. "add card draw", "prevent death"), recommend which hooks to override with signatures, stubs, and examples |
| `suggest_patches` | Suggest hooks and Harmony patch targets from a desired behavior change |
| `analyze_method_callers` | Trace callers/callees for a game method (O(1) via Roslyn call graph) |
| `get_entity_relationships` | Map the dependency graph around a card, relic, power, monster, or other entity |
| `search_hooks_by_signature` | Find hooks by parameter type |
| `get_hook_signature` | Return a hook signature plus a ready-to-paste override stub |
| `analyze_build_output` | Parse `dotnet build` stdout/stderr into structured compiler errors and warnings |
| `validate_mod` | Check common mod project problems before build/deploy |
| `check_mod_compatibility` | Check a mod against the current indexed game API |

### Game Asset Extraction (GDRE Tools)

These tools use [GDRE Tools](https://github.com/GDRETools/gdsdecomp) to reverse-engineer the Godot side of the game — the `SlayTheSpire2.pck` archive containing 15,000+ scenes, textures, resources, scripts, and audio files. This complements the C# decompilation (`decompile_game` / `ilspycmd`) which covers game logic.

| Tool | Description |
|------|-------------|
| `list_game_assets` | List all files in the game PCK with optional extension/glob filtering. Shows extension breakdown (907 scenes, 3217 C# files, 2426 resources, 48 GDScript files, etc.) |
| `search_game_assets` | Fast in-memory substring search across all 15K+ asset paths. Find assets by name — e.g. search "ironclad" to find all 116 Ironclad-related assets |
| `extract_game_assets` | Extract files from the game PCK with glob include/exclude filters. Supports extracting scripts only |
| `recover_game_project` | Full Godot project recovery — extracts all assets, decompiles GDScript, converts binary resources to text. The asset-side equivalent of `decompile_game` |
| `decompile_gdscript` | Decompile GDScript bytecode (.gdc) to readable source (.gd) |
| `convert_resource` | Convert between binary and text resource formats (.scn/.res to .tscn/.tres and back) |

The asset list is cached in memory after the first call, making subsequent searches sub-millisecond.

## Complex Mod Workflows

These newer tools are intended to close the gap between "generate a file" and "ship/test a real mod project".

### Project Editing And Packaging

Use these tools when an assistant needs to write multiple generated artifacts into an existing repo instead of returning loose snippets:

| Tool | When To Use It | Notes |
|------|----------------|-------|
| `inspect_mod_project` | Before touching an existing project | Reads the `.csproj` and `mod_manifest.json` to infer namespace, assembly name, resource root, PCK name, and localization layout |
| `apply_generated_output` | After any generator call that returns code/localization blobs | Writes source files into the right project folders, merges localization JSON, applies supported `project_edits`, supports `dry_run`, and rolls back on conflicts |
| `build_project_pck` | When a project owns scenes/images/resources under its manifest resource root | Builds the `.pck` using project metadata instead of requiring a manual `base_prefix` |
| `deploy_mod` | When you want one step to build, optionally pack, and install | Validates first, syncs all runtime artifacts from build output, and removes stale managed/PCK files from the target mod folder |
| `validate_mod_assets` | Before packaging a visual/content-heavy mod | Checks for broken `res://` references under the project-owned resource tree |
| `validate_mod_project` | Before deploy or before asking an assistant to continue | Runs both localization and asset validation in one pass |

### Bridge Automation Beyond Combat

The bridge is no longer limited to combat-only manipulations. These tools support deterministic playtest flows across map/event/reward/shop/rest/card-selection screens:

| Tool | What It Adds | Example Use |
|------|--------------|-------------|
| `bridge_start_run` | Seeded starts plus fixture setup for relics, cards, powers, modifiers, fights, and events | Start repeatable regression runs |
| `bridge_execute_action` | Generic screen-aware action execution | Travel on the map, take rewards, buy shop items, pick treasure, choose event options |
| `bridge_wait_for_screen` | Poll until a named screen is active and stable | Wait for `REWARD` before claiming a reward |
| `bridge_wait_until_idle` | Poll until loading/transitions settle | Synchronize scripted playtests between actions |
| `bridge_get_diagnostics` | Bundle recent logs with current bridge/runtime state | Debug why automation stalled or hit the wrong screen |
| `bridge_tail_log` | Read recent bridge log lines | Inspect the last few actions and screen transitions |
| `bridge_get_last_errors` | Filter recent failures from the bridge log | Triage action routing or state-detection problems quickly |

### Hook-Aware Generation And Build Triage

These tools help assistants generate correct overrides and interpret failures instead of guessing:

| Tool | Purpose |
|------|---------|
| `get_hook_signature` | Returns the exact hook signature plus a ready-to-paste override stub |
| `analyze_build_output` | Parses `dotnet build` stdout/stderr into structured compiler errors and warnings |

`generate_relic`, `generate_power`, and `generate_enchantment` now use hook-signature-aware fallback stubs when a trigger hook is supplied, so uncommon hooks no longer default to a bare `/* TODO: add parameters */` method.

### Extra Scaffolds For Complex Content

These generators cover content types and patches that frequently appear in larger mods:

| Tool | Output |
|------|--------|
| `generate_ancient` | BaseLib `CustomAncientModel` scaffold with option pools and localization |
| `generate_create_visuals_patch` | Harmony patch required for static-image custom monster visuals |
| `generate_act_encounter_patch` | Encounter-pool injection patch for adding custom encounters to acts |

### Example Sequences

Update an existing project with multiple generated artifacts, validate it, then deploy it:

```text
inspect_mod_project
apply_generated_output
validate_mod_project
deploy_mod
```

Run a repeatable playtest that starts from a seed, waits for a reward screen, then claims the first reward:

```text
bridge_start_run
bridge_wait_until_idle
bridge_wait_for_screen
bridge_execute_action
```

Generate a relic using a less common hook, then explain any compiler failures structurally:

```text
get_hook_signature
generate_relic
build_mod
analyze_build_output
```

Explore the game's Godot assets to understand character scene structure before building a custom character:

```text
search_game_assets     # find "ironclad" scenes/resources
extract_game_assets    # extract the energy counter and combat scenes
convert_resource       # convert binary .scn to readable .tscn
generate_character     # generate character code with correct res:// paths
```

## Project Workflow Helpers

For callers using `sts2mcp.mod_gen.ModGenerator` directly, the generator layer also exposes project-aware helpers for multi-step workflows:

- `inspect_project(project_dir)` infers namespace, assembly name, PCK name, resource root, and localization directories from the `.csproj` plus `mod_manifest.json`
- `apply_generator_output(...)` / `apply_generator_outputs(...)` write generator results into an existing project, merge localization JSON, apply supported `project_edits`, and reject paths that escape the project root
- `build_project_pck(...)` builds a `.pck` using the project's manifest/resource layout instead of requiring the caller to supply `base_prefix` manually
- `deploy_mod(...)` builds, optionally packs, and installs a mod into the game's `mods/` folder in one step
- `validate_project_localization(...)`, `validate_project_assets(...)`, and `validate_project(...)` provide lightweight checks for JSON validity, missing localization coverage, and broken `res://` references under the project-owned resource tree

These helpers are intended for complex workflows where an assistant wants to generate several artifacts, merge them into a pre-existing project, validate the result, then package/deploy without hand-assembling paths.

### Advanced Generators (New in v3.0)

Inspired by patterns found across 21 community mods (ModConfig, BetterDrawing, Oddmelt, RMP, say-the-spire2, Archipelago, race-mod, and more), these generators cover the most common advanced modding needs:

| Tool | Description | Inspired By |
|------|-------------|-------------|
| `generate_net_message` | `INetMessage` + `IPacketSerializable` scaffold using `PacketWriter` / `PacketReader`, `Mode`, and `LogLevel` like the decompiled multiplayer messages | BetterDrawing, sts2_typing, BadApple |
| `generate_godot_ui` | Programmatic Godot UI panel with styled controls (labels, buttons, sliders, checkboxes) — no .tscn required | ModConfig, sts2_typing, 14+ mods |
| `generate_settings_panel` | Self-initializing ModConfig reflection bridge with JSON fallback and `ModEntry` patch hints | RouteSuggest, ModConfig |
| `generate_hover_tip` | HoverTip utility class for contextual tooltips on nodes or positions | sts2_typing, easyDmgCalc |
| `generate_overlay` | Auto-injected combat/map overlay with Harmony patch for scene tree injection | easyDmgCalc, sts2-agent |
| `generate_transpiler_patch` | IL bytecode Harmony transpiler for modifying method instructions directly | RMP-Mods, Oddmelt, race-mod |
| `generate_reflection_accessor` | Cached AccessTools field/property accessors with getters/setters | All 21 mods analyzed |
| `generate_custom_keyword` | [CustomEnum] CardKeyword with BaseLib, plus localization | Oddmelt (Stitch/Woven) |
| `generate_custom_pile` | [CustomEnum] PileType for custom card destinations | Oddmelt (StitchPile) |
| `generate_spire_field` | SpireField\<T, TValue\> for attaching data to game models without modification | Oddmelt |
| `generate_dynamic_var` | Custom DynamicVar subclass for card/power description variables | Oddmelt, More Bosses |

### Other Generator Scaffolds

| Tool | Description |
|------|-------------|
| `generate_event` | Event class with choice tree and handler methods |
| `generate_ancient` | BaseLib CustomAncientModel scaffold |
| `generate_orb` | Orb with passive/evoke effects (Defect mechanic) |
| `generate_enchantment` | Enchantment that attaches to and modifies cards |
| `generate_create_visuals_patch` | Harmony patch for custom monster visuals |
| `generate_act_encounter_patch` | Inject encounters into act monster pools |
| `generate_game_action` | Custom GameAction for the combat action queue |
| `generate_mechanic` | Full cross-cutting keyword mechanic (power + card + relic + localization) |
| `generate_custom_tooltip` | Localization-backed `HoverTip` helper for `ExtraHoverTips`-style usage |
| `generate_save_data` | Persistent JSON save data class |
| `generate_test_scenario` | Console command sequence for test setups |
| `generate_vfx_scene` | Godot .tscn particle effect scene |

### Modding Guides (28 Topics)

The `get_modding_guide` tool provides built-in documentation. The 12 new topics (marked with **new**) were derived from patterns found across the community mod ecosystem:

| Topic | Description |
|-------|-------------|
| `getting_started` | Prerequisites, quick start, key concepts |
| `cards` | CardModel, pools, dynamic vars, OnPlay, localization |
| `relics` | RelicModel, hooks, images, localization |
| `powers` | PowerModel, stacking, buff/debuff |
| `potions` | PotionModel, OnUse, potion pools |
| `monsters` | MonsterModel, move state machines, scenes |
| `encounters` | EncounterModel, room types, act pools |
| `events` | EventModel, choices, outcomes |
| `harmony_patches` | Prefix, postfix, targeting, common patterns |
| `localization` | JSON structure, SmartFormat, dynamic vars |
| `console` | Dev console commands and testing |
| `hooks` | All 144 hooks by category with signatures |
| `pools` | Card/relic/potion pool system |
| `building` | dotnet build, PCK export, installation |
| `debugging` | Remote debugging, logging, common issues |
| `project_structure` | Recommended layout, .csproj settings |
| **`multiplayer_networking`** | INetMessage, sending/receiving, transfer modes, message batching |
| **`godot_ui_construction`** | Programmatic Controls, StyleBox, themes, focus chains, hover tips, tweens |
| **`reflection_patterns`** | AccessTools, Traverse, __makeref struct mutation, cached FieldInfo |
| **`advanced_harmony`** | IL transpilers, async patching, multi-method targeting, prefix control |
| **`save_file_format`** | Save file locations, JSON schema, custom mod save data |
| **`game_log_parsing`** | godot.log format, regex patterns, in-code logging |
| **`combat_deep_dive`** | Intent system, damage pipeline, all Command APIs (DamageCmd, PowerCmd, etc.) |
| **`custom_keywords_and_piles`** | [CustomEnum] keywords, KeywordProperties, custom PileType routing |
| **`mod_config_integration`** | BaseLib SimpleModConfig, reflection bridge, manual JSON config |
| **`resource_loading`** | PCK vs DLL resources, fallback chains, assembly loading, intent atlas paths |
| **`rng_and_determinism`** | Rng class, RunRngSet, seed management, deterministic sub-seeds |
| **`accessibility_patterns`** | TTS integration, focus navigation, screen reader support, high contrast |

### Maintenance

| Tool | Description |
|------|-------------|
| `decompile_game` | Re-decompile `sts2.dll` after a game update (requires `ilspycmd`) |
| `recover_game_project` | Re-extract Godot assets from the game PCK after a game update (requires `gdre_tools`) |

## Prerequisites

- **Python 3.11+**
- **[ilspycmd](https://www.nuget.org/packages/ilspycmd/)** — `dotnet tool install -g ilspycmd` (for C# decompilation)
- **[GDRE Tools](https://github.com/GDRETools/gdsdecomp/releases)** — download the latest release and extract to `tools/` or set `GDRE_TOOLS_PATH` (for Godot asset extraction)
- **.NET SDK 9.0** — for building mods and the Roslyn code analyzer (auto-built on first run)
- **Slay the Spire 2** — the game itself

## Setup

### 1. Clone and install

```bash
git clone https://github.com/elliotttate/sts2-modding-mcp.git
cd sts2-modding-mcp

# Create a virtual environment (inside the cloned repo)
python -m venv venv

# Activate it
# Windows (PowerShell):
venv\Scripts\Activate.ps1
# Windows (cmd):
venv\Scripts\activate.bat
# macOS / Linux:
# source venv/bin/activate

# Install dependencies
pip install "mcp[cli]"
```

### 2. Decompile the game (C#)

The first time, you need to decompile `sts2.dll` to populate the `decompiled/` directory:

```bash
ilspycmd -p -o ./decompiled "E:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64\sts2.dll"
```

Or use the `decompile_game` tool after connecting the MCP.

### 3. Set up GDRE Tools (Godot assets)

Download the latest [GDRE Tools release](https://github.com/GDRETools/gdsdecomp/releases) and extract it to the `tools/` directory:

```bash
# From the project root
mkdir tools
cd tools
# Download and extract (Windows example)
curl -L -o gdre_tools.zip https://github.com/GDRETools/gdsdecomp/releases/download/v2.4.0/GDRE_tools-v2.4.0-windows.zip
unzip gdre_tools.zip
rm gdre_tools.zip
```

Or set the `GDRE_TOOLS_PATH` environment variable to point to your `gdre_tools` binary.

The `list_game_assets` and `search_game_assets` tools will then automatically find and index `SlayTheSpire2.pck`. Use `recover_game_project` for a one-time full extraction with GDScript decompilation.

### 4. Configure paths

By default the server looks for the game at `E:\SteamLibrary\steamapps\common\Slay the Spire 2`. Override with environment variables:

```bash
export STS2_GAME_DIR="/path/to/Slay the Spire 2"
export STS2_DECOMPILED_DIR="/path/to/decompiled"
export GDRE_TOOLS_PATH="/path/to/gdre_tools.exe"  # optional, defaults to tools/gdre_tools.exe
```

### 5. Register with Claude Code

Point the MCP config at the **venv's Python** so dependencies are always available — no need to activate the venv first.

**Option A — Project scope** (`.mcp.json` in your working directory):

```json
{
  "mcpServers": {
    "sts2-modding": {
      "command": "/path/to/sts2-modding-mcp/venv/Scripts/python.exe",
      "args": ["/path/to/sts2-modding-mcp/run.py"]
    }
  }
}
```

> **macOS / Linux:** use `venv/bin/python` instead of `venv/Scripts/python.exe`.

**Option B — User scope** (`~/.claude/settings.json` under `mcpServers`):

```json
{
  "mcpServers": {
    "sts2-modding": {
      "command": "/path/to/sts2-modding-mcp/venv/Scripts/python.exe",
      "args": ["/path/to/sts2-modding-mcp/run.py"]
    }
  }
}
```

Replace `/path/to/sts2-modding-mcp` with the actual path where you cloned the repo.

Restart Claude Code and the server should appear in `/mcp`.

## Usage Examples

Once connected, you can ask Claude things like:

- *"Which hook should I use to add extra card draw?"* → uses `suggest_hooks`
- *"Show me the source code for the Bash card"* → uses `get_entity_source`
- *"List all rare attack cards"* → uses `list_entities`
- *"How does the damage hook system work?"* → uses `list_hooks` + `get_entity_source`
- *"Create a new mod called FlameForge with a card that deals 20 damage and applies 2 Vulnerable"* → uses `create_mod_project` + `generate_card`
- *"Generate a relic that gives 3 Strength the first time you take damage each combat"* → uses `generate_relic`
- *"Build my mod and install it"* → uses `build_mod` + `install_mod`
- *"Apply these generated relic/card files into my existing mod, validate it, then deploy it"* → uses `apply_generated_output` + `validate_mod_project` + `deploy_mod`
- *"Start a seeded run, wait for the reward screen, then claim the first reward"* → uses `bridge_start_run` + `bridge_wait_for_screen` + `bridge_execute_action`
- *"What BaseLib utilities are available for card effects?"* → uses `get_baselib_reference`
- *"Generate a network message to sync drawing colors across players"* → uses `generate_net_message`
- *"Create a damage overlay that shows total incoming damage above the player"* → uses `generate_overlay`
- *"I need to access the private _drawingStates field on NMapDrawings"* → uses `generate_reflection_accessor`
- *"How do Harmony IL transpilers work?"* → uses `get_modding_guide` topic `advanced_harmony`
- *"Create a custom 'Stitch' keyword that can be added to cards"* → uses `generate_custom_keyword`
- *"Generate a settings panel that integrates with ModConfig if available"* → uses `generate_settings_panel`
- *"What scene files does the Ironclad character use?"* → uses `search_game_assets`
- *"Show me all the .tscn scenes in the game"* → uses `list_game_assets`
- *"Extract the energy counter scene so I can reference it for my custom character"* → uses `extract_game_assets`
- *"Recover the full Godot project from the game PCK"* → uses `recover_game_project`

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
│   ├── server.py           # MCP server with all 147 tool definitions
│   ├── game_data.py        # Game data indexing and querying
│   ├── mod_gen.py          # Code generation templates plus project-aware workflow helpers
│   ├── pck_builder.py      # Pure Python Godot PCK builder
│   ├── gdre_tools.py       # GDRE Tools integration for PCK extraction, GDScript decompilation, resource conversion
│   ├── analysis.py         # Code intelligence — patch suggestions, caller analysis, compatibility checks
│   ├── bridge_client.py    # TCP JSON-RPC client to in-game MCPTest mod
│   └── project_workflow.py # Project inspection, apply/merge, PCK build, deploy, and validation helpers
├── tools/
│   ├── roslyn_analyzer/    # C# Roslyn-based source analyzer (auto-built on first run)
│   └── gdre_tools.exe      # GDRE Tools binary (gitignored, download from GitHub releases)
├── decompiled/             # Decompiled C# source + roslyn_index.json (gitignored, ~23MB + ~17MB index)
└── recovered/              # Recovered Godot project (gitignored, generated by recover_game_project)
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
│   ├── Events/                     # Event scaffolds
│   ├── Orbs/                       # Orb scaffolds
│   ├── Enchantments/               # Enchantment scaffolds
│   ├── Actions/                    # GameAction scaffolds
│   ├── Networking/                 # Multiplayer net messages (new)
│   ├── UI/                         # Custom Godot UI panels (new)
│   ├── Overlays/                   # Combat/map overlays (new)
│   ├── Utils/                      # Reflection accessors (new)
│   ├── Keywords/                   # Custom card keywords (new)
│   ├── Piles/                      # Custom pile types (new)
│   ├── Fields/                     # SpireField data attachments (new)
│   ├── Vars/                       # Custom DynamicVar classes (new)
│   ├── Characters/                 # Custom characters (BaseLib)
│   ├── Config/                     # Mod config (BaseLib)
│   ├── Ancients/                   # Ancient scaffolds (BaseLib)
│   └── Patches/                    # Harmony patches
└── MyMod/
    ├── localization/eng/           # Localization JSON files
    ├── images/                     # Entity images
    └── MonsterResources/           # Monster scenes and sprites
```

`create_mod_project` now seeds empty localization files for cards, relics, powers, potions, monsters, encounters, events, orbs, and enchantments, plus `characters.json` and `ancients.json` when BaseLib scaffolds are enabled.

## Updating After Game Patches

When STS2 updates:

1. **C# source** — run `decompile_game` (or manually re-run `ilspycmd`) to refresh the decompiled source. The Roslyn index automatically rebuilds on the next query (detects staleness via file timestamps).
2. **Godot assets** — run `recover_game_project` to re-extract scenes, textures, resources, and GDScript from the updated PCK. The in-memory asset list cache refreshes on server restart.

## License

MIT
