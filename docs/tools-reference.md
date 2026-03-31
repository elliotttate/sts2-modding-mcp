# Tool Reference

The MCP server exposes **151 tools**. This document lists them all by category.

## Game Data Query

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

## Core Mod Creation

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

## Build, Deploy, and Project Workflow

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

## Live Bridge and Playtesting

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

Additional bridge combat tools: `bridge_ping`, `bridge_get_screen`, `bridge_get_run_state`, `bridge_get_combat_state`, `bridge_get_player_state`, `bridge_get_map_state`, `bridge_play_card`, `bridge_end_turn`, `bridge_console`, `bridge_use_potion`, `bridge_make_event_choice`, `bridge_navigate_map`, `bridge_rest_site_choice`, `bridge_shop_action`, `bridge_get_card_piles`, and `bridge_manipulate_state`.

## Live Scene Inspection (GodotExplorer)

A companion mod (`explorer_mod/`) runs inside the game as a TCP server on port 27020, exposing the live Godot engine to MCP tools.

| Tool | Description |
|------|-------------|
| `explorer_get_scene_tree` | Walk the full Godot scene hierarchy with configurable depth and root path |
| `explorer_find_nodes` | Find nodes by name pattern with optional type filtering |
| `explorer_inspect_node` | Get detailed info for a specific node — type, properties, children |
| `explorer_get_property` | Read any property from any node in the running scene |
| `explorer_set_property` | Write a property value on a live node (position, scale, color, text, etc.) |
| `explorer_toggle_visibility` | Show/hide any CanvasItem node — useful for isolating visual layers |
| `explorer_tween_property` | Animate a property with Godot Tweens (duration, loops, easing) |
| `explorer_call_method` | Execute a method on a node with optional arguments |
| `explorer_get_node_count` | Total node count in the scene tree |
| `explorer_get_game_info` | Engine metadata: Godot version, FPS, window size, process name |
| `explorer_list_assemblies` | List all loaded .NET assemblies with version and type counts |
| `explorer_search_types` | Search for .NET types across all loaded assemblies |
| `explorer_inspect_type` | Get detailed .NET type info — methods, properties, base class, assembly |
| `explorer_list_groups` | List nodes in Godot groups or enumerate all groups |

## Automated Playtesting and Debugging

### Full Game Automation

The bridge can control every screen in the game:

- **Start seeded runs** with specific characters, ascension levels, modifiers, and fixture commands that pre-configure relics, cards, gold, and powers
- **Play cards** with targeting, **end turns**, **use/discard potions**
- **Navigate maps** by row/column, **make event choices**, **claim rewards**, **buy from shops**, **choose rest site actions**, **pick treasure**, and **select/skip/confirm cards**
- **Execute console commands** (gold, godmode, add relics/cards, force fights, heal, etc.)
- **Manipulate state** — set HP/gold/energy, draw cards, add powers/relics mid-run
- **Set game speed** from 0.1x to 20x for fast-forwarding through animations
- **Capture screenshots** at any point for visual verification

### Breakpoint Debugging

| Tool | Description |
|------|-------------|
| `bridge_debug_pause` | Pause action processing — the game renders but no actions execute |
| `bridge_debug_resume` | Resume from a breakpoint |
| `bridge_debug_step` | Step forward by one action, or step to the next player turn |
| `bridge_debug_set_breakpoint` | Set a breakpoint on an action type or hook, with optional conditions |
| `bridge_debug_remove_breakpoint` | Remove a breakpoint by ID |
| `bridge_debug_list_breakpoints` | List all breakpoints with hit counts and pause/step state |
| `bridge_debug_clear_breakpoints` | Clear all breakpoints and disable stepping |
| `bridge_debug_get_context` | Get the current pause context — why it paused, the current action, and a full game state snapshot |

### State Snapshots and A/B Testing

| Tool | Description |
|------|-------------|
| `bridge_save_snapshot` | Save a named snapshot of the full game state |
| `bridge_restore_snapshot` | Restore a previously saved snapshot |

### AutoSlay — Automated Multi-Run Stress Testing

| Tool | Description |
|------|-------------|
| `bridge_autoslay_start` | Start automated runs with configurable character, seed, ascension, modifiers, and fixture commands |
| `bridge_autoslay_stop` | Stop the current AutoSlay session |
| `bridge_autoslay_status` | Get progress — runs completed, current floor/act/room, elapsed time, errors |
| `bridge_autoslay_configure` | Configure timeouts (room, run, screen), watchdog behavior, polling intervals, max floor |

### Event and Exception Monitoring

| Tool | Description |
|------|-------------|
| `bridge_get_events` | Poll game events (card plays, turn ends, run starts, screenshots) since a given ID |
| `bridge_get_exceptions` | Poll recent unhandled exceptions with full stack traces |
| `bridge_get_game_log` | Retrieve captured game log messages filtered by level, type, or content |
| `bridge_hot_swap_patches` | Hot-reload Harmony patches from a new DLL without restarting the game |

## Code Intelligence and Validation

| Tool | Description |
|------|-------------|
| `suggest_hooks` | Given a modding intent (e.g. "add card draw", "prevent death"), recommend which hooks to override with signatures, stubs, and examples |
| `suggest_patches` | Suggest hooks and Harmony patch targets from a desired behavior change |
| `analyze_method_callers` | Trace callers/callees for a game method (O(1) via Roslyn call graph) |
| `get_entity_relationships` | Map the dependency graph around a card, relic, power, monster, or other entity |
| `search_hooks_by_signature` | Find hooks by parameter type |
| `get_hook_signature` | Return a hook signature plus a ready-to-paste override stub |
| `analyze_build_output` | Parse `dotnet build` stdout/stderr into structured compiler errors and warnings |
| `validate_mod` | Check common mod project problems before build/deploy |
| `check_mod_compatibility` | Check a mod against the current indexed game API |

## Game Asset Extraction (GDRE Tools)

These tools use [GDRE Tools](https://github.com/GDRETools/gdsdecomp) to reverse-engineer the Godot side of the game — the `SlayTheSpire2.pck` archive containing 15,000+ scenes, textures, resources, scripts, and audio files.

| Tool | Description |
|------|-------------|
| `list_game_assets` | List all files in the game PCK with optional extension/glob filtering. Shows extension breakdown (907 scenes, 3217 C# files, 2426 resources, 48 GDScript files, etc.) |
| `search_game_assets` | Fast in-memory substring search across all 15K+ asset paths. Find assets by name — e.g. search "ironclad" to find all 116 Ironclad-related assets |
| `extract_game_assets` | Extract files from the game PCK with glob include/exclude filters. Supports extracting scripts only |
| `recover_game_project` | Full Godot project recovery — extracts all assets, decompiles GDScript, converts binary resources to text. The asset-side equivalent of `decompile_game` |
| `decompile_gdscript` | Decompile GDScript bytecode (.gdc) to readable source (.gd) |
| `convert_resource` | Convert between binary and text resource formats (.scn/.res to .tscn/.tres and back) |

## Maintenance

| Tool | Description |
|------|-------------|
| `decompile_game` | Re-decompile `sts2.dll` after a game update (requires `ilspycmd`) |
| `recover_game_project` | Re-extract Godot assets from the game PCK after a game update (requires `gdre_tools`) |
