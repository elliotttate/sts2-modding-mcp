"""MCP Server for Slay the Spire 2 modding."""

import json
import os
import subprocess
import sys
from pathlib import Path

from mcp.server import Server
from mcp.server.stdio import stdio_server
import mcp.types as types

from .game_data import GameDataIndex
from .mod_gen import ModGenerator

# ─── Configuration ────────────────────────────────────────────────────────────

GAME_DIR = os.environ.get(
    "STS2_GAME_DIR",
    r"E:\SteamLibrary\steamapps\common\Slay the Spire 2",
)
DECOMPILED_DIR = os.environ.get(
    "STS2_DECOMPILED_DIR",
    os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "decompiled"),
)

# ─── Initialize ───────────────────────────────────────────────────────────────

server = Server("sts2-modding-mcp")
game_data = GameDataIndex(DECOMPILED_DIR)
mod_gen = ModGenerator(GAME_DIR)


# ─── Tool Definitions ────────────────────────────────────────────────────────


@server.list_tools()
async def list_tools() -> list[types.Tool]:
    return [
        # ── Game Data Query Tools ──
        types.Tool(
            name="list_entities",
            description=(
                "List game entities (cards, relics, potions, powers, monsters, encounters, events, "
                "enchantments, characters, orbs, acts, etc.) with optional filters. "
                "Returns entity name, type, base class, and key properties. "
                "Entity types: card, relic, potion, power, monster, encounter, event, enchantment, "
                "affliction, character, orb, card_pool, relic_pool, potion_pool, act, modifier."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "entity_type": {
                        "type": "string",
                        "description": "Filter by entity type (card, relic, potion, power, monster, encounter, event, enchantment, affliction, character, orb, act, etc.)",
                    },
                    "query": {
                        "type": "string",
                        "description": "Search by name (case-insensitive substring match)",
                    },
                    "rarity": {
                        "type": "string",
                        "description": "Filter by rarity (Common, Uncommon, Rare, etc.)",
                    },
                    "limit": {
                        "type": "integer",
                        "description": "Max results to return (default 200)",
                        "default": 200,
                    },
                },
            },
        ),
        types.Tool(
            name="get_entity_source",
            description=(
                "Get the full decompiled C# source code for any game class. "
                "Works for cards, relics, potions, powers, monsters, encounters, events, "
                "base classes (CardModel, RelicModel, AbstractModel, etc.), hooks, modding API, "
                "combat system, commands, factories, and any other class in the game. "
                "Use this to understand how existing game content works before creating mods."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "class_name": {
                        "type": "string",
                        "description": "Class name to look up (e.g. 'Bash', 'StrengthPower', 'CardModel', 'Hook', 'ModManager')",
                    },
                },
                "required": ["class_name"],
            },
        ),
        types.Tool(
            name="search_game_code",
            description=(
                "Full-text regex search through all decompiled game source code (~23MB, 1300+ files). "
                "Use to find how specific APIs are used, locate method calls, find patterns, etc."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "pattern": {
                        "type": "string",
                        "description": "Regex pattern to search for (case-insensitive)",
                    },
                    "max_results": {
                        "type": "integer",
                        "description": "Max results (default 50)",
                        "default": 50,
                    },
                },
                "required": ["pattern"],
            },
        ),
        types.Tool(
            name="list_hooks",
            description=(
                "List all game hooks available for modding. Hooks are the primary way mods interact "
                "with game events. Categories: before (pre-event), after (post-event), modify (change values), "
                "should (boolean gates), try (conditional actions). "
                "Subcategories: card, damage_block, power, turn, map, reward, potion, orb, combat, "
                "death, hand, special, rest_site, gold, relic, general."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "category": {
                        "type": "string",
                        "description": "Filter by hook category: before, after, modify, should, try",
                    },
                    "subcategory": {
                        "type": "string",
                        "description": "Filter by subcategory: card, damage_block, power, turn, map, reward, potion, orb, combat, death, hand, special, rest_site, gold, relic, general",
                    },
                },
            },
        ),
        types.Tool(
            name="get_game_info",
            description=(
                "Get overview of the game: version, file paths, entity counts, available namespaces, "
                "and the modding API surface. Good starting point for understanding the game."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="get_console_commands",
            description=(
                "List all developer console commands available in-game for testing mods. "
                "Includes commands like 'card', 'relic', 'fight', 'gold', 'godmode', etc."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="browse_namespace",
            description=(
                "List all files in a specific namespace/directory of the decompiled source. "
                "Use list_namespaces first to see available namespaces, then browse specific ones."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "namespace": {
                        "type": "string",
                        "description": "Namespace directory name (e.g. 'MegaCrit.Sts2.Core.Models.Cards')",
                    },
                    "read_file": {
                        "type": "string",
                        "description": "Optionally read a specific file within the namespace (e.g. 'Bash.cs')",
                    },
                },
                "required": ["namespace"],
            },
        ),
        types.Tool(
            name="get_modding_guide",
            description=(
                "Get contextual documentation for modding STS2. Topics: getting_started, cards, relics, "
                "powers, potions, monsters, encounters, events, harmony_patches, localization, "
                "console, hooks, pools, building, debugging, project_structure."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "topic": {
                        "type": "string",
                        "description": "Guide topic",
                        "enum": [
                            "getting_started", "cards", "relics", "powers", "potions",
                            "monsters", "encounters", "events", "harmony_patches",
                            "localization", "console", "hooks", "pools", "building",
                            "debugging", "project_structure",
                        ],
                    },
                },
                "required": ["topic"],
            },
        ),
        # ── Mod Creation Tools ──
        types.Tool(
            name="create_mod_project",
            description=(
                "Create a complete mod project scaffold with proper directory structure, "
                ".csproj, ModEntry, mod_manifest.json, localization folders, and image directories. "
                "This is the first step in creating a new mod."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_name": {"type": "string", "description": "Mod display name"},
                    "author": {"type": "string", "description": "Author name"},
                    "description": {"type": "string", "description": "Mod description"},
                    "output_dir": {"type": "string", "description": "Output directory (default: game_dir/mod_projects/mod_name)"},
                },
                "required": ["mod_name", "author"],
            },
        ),
        types.Tool(
            name="generate_card",
            description=(
                "Generate a new card class with proper structure, dynamic vars, OnPlay logic, "
                "upgrade logic, and localization entries. "
                "Returns the source code and localization - does NOT write files automatically."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string", "description": "Mod C# namespace (e.g. 'MyMod')"},
                    "class_name": {"type": "string", "description": "Card class name (PascalCase, e.g. 'FlameStrike')"},
                    "card_type": {"type": "string", "enum": ["Attack", "Skill", "Power", "Status", "Curse"], "default": "Attack"},
                    "rarity": {"type": "string", "enum": ["Basic", "Common", "Uncommon", "Rare"], "default": "Common"},
                    "target_type": {"type": "string", "enum": ["AnyEnemy", "AllEnemies", "RandomEnemy", "None", "Self", "AnyAlly", "AllAllies"], "default": "AnyEnemy"},
                    "energy_cost": {"type": "integer", "default": 1},
                    "damage": {"type": "integer", "description": "Base damage (0 for non-attack)", "default": 0},
                    "block": {"type": "integer", "description": "Base block (0 for none)", "default": 0},
                    "magic_number": {"type": "integer", "description": "Extra numeric value", "default": 0},
                    "keywords": {
                        "type": "array",
                        "items": {"type": "string"},
                        "description": "Card keywords: Exhaust, Ethereal, Innate, Retain, Sly, Eternal, Unplayable",
                    },
                    "pool": {"type": "string", "description": "Card pool class (default: ColorlessCardPool)", "default": "ColorlessCardPool"},
                    "description": {"type": "string", "description": "Card description text with rich text tags"},
                    "upgrade_description": {"type": "string", "description": "Upgraded card description"},
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_relic",
            description=(
                "Generate a new relic class with proper structure, hook methods, and localization. "
                "Common trigger hooks: BeforeCombatStart, AfterCardPlayed, AfterDamageReceived, "
                "AfterTurnEnd, AfterBlockGained, ModifyDamageAdditive."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "Relic class name (PascalCase)"},
                    "rarity": {"type": "string", "enum": ["Starter", "Common", "Uncommon", "Rare", "Shop", "Event", "Ancient"], "default": "Common"},
                    "pool": {"type": "string", "default": "SharedRelicPool", "description": "RelicPool class name"},
                    "trigger_hook": {"type": "string", "description": "Primary hook method (e.g. 'AfterDamageReceived', 'BeforeCombatStart')"},
                    "description": {"type": "string"},
                    "flavor": {"type": "string"},
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_power",
            description=(
                "Generate a new power (buff/debuff) class with proper structure and hooks. "
                "Common trigger hooks: ModifyDamageAdditive, ModifyDamageMultiplicative, "
                "BeforeHandDraw, AfterTurnEnd, AfterCardPlayed."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "Power class name (PascalCase, should end with 'Power')"},
                    "power_type": {"type": "string", "enum": ["Buff", "Debuff"], "default": "Buff"},
                    "stack_type": {"type": "string", "enum": ["Counter", "Single"], "default": "Counter"},
                    "trigger_hook": {"type": "string", "description": "Primary hook method"},
                    "description": {"type": "string"},
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_potion",
            description="Generate a new potion class with proper structure and localization.",
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string"},
                    "rarity": {"type": "string", "enum": ["Common", "Uncommon", "Rare"], "default": "Common"},
                    "usage": {"type": "string", "enum": ["CombatOnly", "OutOfCombat", "Anywhere"], "default": "CombatOnly"},
                    "target_type": {"type": "string", "enum": ["None", "AnyEnemy", "AnyAlly", "AnyPlayer", "AllEnemies"], "default": "None"},
                    "pool": {"type": "string", "default": "SharedPotionPool"},
                    "block": {"type": "integer", "default": 0},
                    "description": {"type": "string"},
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_monster",
            description=(
                "Generate a new monster class with move state machine, scene file (.tscn), "
                "and localization. Provide a list of moves with their damage/block/type. "
                "Also generates the required CreateVisualsPatch if using static images."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "mod_name": {"type": "string", "description": "Mod folder name (for resource paths)"},
                    "class_name": {"type": "string"},
                    "min_hp": {"type": "integer", "default": 50},
                    "max_hp": {"type": "integer", "default": 55},
                    "moves": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "name": {"type": "string", "description": "Move ID (SCREAMING_SNAKE)"},
                                "damage": {"type": "integer"},
                                "block": {"type": "integer"},
                                "type": {"type": "string", "enum": ["attack", "defend", "buff", "debuff", "attack_defend"]},
                            },
                            "required": ["name", "type"],
                        },
                        "description": "List of monster moves",
                    },
                    "image_size": {"type": "integer", "default": 200, "description": "Sprite size in pixels"},
                },
                "required": ["mod_namespace", "mod_name", "class_name"],
            },
        ),
        types.Tool(
            name="generate_encounter",
            description="Generate a new encounter class that spawns specific monsters.",
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string"},
                    "room_type": {"type": "string", "enum": ["Monster", "Elite", "Boss"], "default": "Monster"},
                    "monsters": {
                        "type": "array",
                        "items": {"type": "string"},
                        "description": "List of monster class names to spawn",
                    },
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_harmony_patch",
            description=(
                "Generate a Harmony patch class to hook into existing game methods. "
                "Harmony patches are the primary way to modify game behavior beyond the hook system."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "Patch class name"},
                    "target_type": {"type": "string", "description": "Full type to patch (e.g. 'CardModel', 'CombatManager')"},
                    "target_method": {"type": "string", "description": "Method name to patch"},
                    "patch_type": {"type": "string", "enum": ["Prefix", "Postfix"], "default": "Postfix"},
                },
                "required": ["mod_namespace", "class_name", "target_type", "target_method"],
            },
        ),
        types.Tool(
            name="generate_localization",
            description=(
                "Generate localization JSON entries for a mod entity. Uses the game's localization "
                "format with SmartFormat support for dynamic variables: {Amount}, {Damage}, {Block}, etc. "
                "Rich text: [gold]keyword[/gold], [blue]{value}[/blue]."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_id": {"type": "string", "description": "Mod ID prefix"},
                    "entity_type": {"type": "string", "enum": ["card", "relic", "power", "potion", "monster", "encounter"]},
                    "entity_name": {"type": "string", "description": "Entity class name"},
                    "title": {"type": "string"},
                    "description": {"type": "string"},
                    "flavor": {"type": "string"},
                    "upgrade_description": {"type": "string"},
                },
                "required": ["mod_id", "entity_type", "entity_name"],
            },
        ),
        # ── BaseLib Tools ──
        types.Tool(
            name="generate_character",
            description=(
                "Generate a custom playable character class with card/relic/potion pools. "
                "REQUIRES BaseLib (Alchyr.Sts2.BaseLib). Generates CustomCharacterModel subclass "
                "with pool models, starter deck/relics, and visual asset paths."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "mod_name": {"type": "string", "description": "Mod folder name for resource paths"},
                    "class_name": {"type": "string", "description": "Character class name (PascalCase)"},
                    "starting_hp": {"type": "integer", "default": 80},
                    "starting_gold": {"type": "integer", "default": 99},
                    "orb_slots": {"type": "integer", "default": 0},
                },
                "required": ["mod_namespace", "mod_name", "class_name"],
            },
        ),
        types.Tool(
            name="generate_mod_config",
            description=(
                "Generate a mod config class with auto-generated in-game settings UI. "
                "REQUIRES BaseLib. Supports bool toggles, double sliders with ranges, "
                "and enum dropdowns. Config is auto-persisted to JSON files."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "default": "MyModConfig"},
                    "properties": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "name": {"type": "string"},
                                "type": {"type": "string", "enum": ["bool", "double", "enum"]},
                                "default": {"type": "string"},
                                "section": {"type": "string"},
                                "slider_min": {"type": "number"},
                                "slider_max": {"type": "number"},
                                "slider_step": {"type": "number"},
                                "enum_type": {"type": "string"},
                            },
                            "required": ["name", "type", "default"],
                        },
                    },
                },
                "required": ["mod_namespace"],
            },
        ),
        types.Tool(
            name="get_baselib_reference",
            description=(
                "Get documentation for BaseLib (Alchyr.Sts2.BaseLib) - the community modding library. "
                "Topics: overview, custom_card, custom_relic, custom_power, custom_potion, "
                "custom_character, custom_ancient, config, card_variables, common_actions, "
                "spire_field, weighted_list, il_patching, mod_interop, utilities."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "topic": {
                        "type": "string",
                        "enum": [
                            "overview", "custom_card", "custom_relic", "custom_power",
                            "custom_potion", "custom_character", "custom_ancient",
                            "config", "card_variables", "common_actions",
                            "spire_field", "weighted_list", "il_patching",
                            "mod_interop", "utilities",
                        ],
                    },
                },
                "required": ["topic"],
            },
        ),
        # ── Build & Deploy Tools ──
        types.Tool(
            name="build_mod",
            description="Build a mod project using 'dotnet build'. Returns build output and success status.",
            inputSchema={
                "type": "object",
                "properties": {
                    "project_dir": {"type": "string", "description": "Path to mod project directory"},
                },
                "required": ["project_dir"],
            },
        ),
        types.Tool(
            name="install_mod",
            description=(
                "Install a built mod to the game's mods directory. Copies DLL, manifest, PCK, and images."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "project_dir": {"type": "string", "description": "Path to mod project directory"},
                    "mod_name": {"type": "string", "description": "Override mod folder name (default: from manifest)"},
                },
                "required": ["project_dir"],
            },
        ),
        types.Tool(
            name="uninstall_mod",
            description="Remove a mod from the game's mods directory.",
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_name": {"type": "string", "description": "Mod folder name to remove"},
                },
                "required": ["mod_name"],
            },
        ),
        types.Tool(
            name="list_installed_mods",
            description="List all mods currently installed in the game's mods directory with their manifest data.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="launch_game",
            description=(
                "Launch Slay the Spire 2 with optional debug parameters. "
                "Can enable remote debugging (for Godot editor output) and other launch flags."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "remote_debug": {
                        "type": "boolean",
                        "description": "Enable Godot remote debugging on port 6007",
                        "default": False,
                    },
                    "renderer": {
                        "type": "string",
                        "enum": ["vulkan", "d3d12", "opengl"],
                        "description": "Rendering backend",
                    },
                    "extra_args": {
                        "type": "string",
                        "description": "Additional command-line arguments",
                    },
                },
            },
        ),
        types.Tool(
            name="decompile_game",
            description=(
                "Re-decompile sts2.dll to refresh the decompiled source. "
                "Use after a game update to get the latest game code. Requires ilspycmd."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        # ── Live Bridge Tools (require game running with MCPTest mod) ──
        types.Tool(
            name="bridge_ping",
            description=(
                "Check if the game is running and the MCPTest bridge mod is loaded. "
                "Returns mod version and status. The bridge runs on TCP port 21337."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_run_state",
            description=(
                "Get the current run state from the live game: act, floor, ascension, "
                "players with HP/gold/deck size/relic count. Requires game running with MCPTest mod."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_combat_state",
            description=(
                "Get live combat state: round number, all enemies (HP/block/powers/intents), "
                "player hand/energy/draw pile/discard pile/powers. Requires active combat."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_player_state",
            description=(
                "Get detailed player state from the live game: full deck listing, "
                "all relics, potions, gold, HP. Requires game running with active run."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_screen_state",
            description=(
                "Get current screen/navigation state: whether a run is in progress, "
                "in combat, current room type. Useful for knowing what commands are valid."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_start_run",
            description=(
                "Start a new singleplayer run in the game. "
                "Characters: Ironclad, Silent, Regent, Necrobinder, Defect. "
                "Requires game at main menu (no run in progress)."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "character": {"type": "string", "default": "Ironclad",
                                  "description": "Character class name: Ironclad, Silent, Regent, Necrobinder, Defect"},
                    "ascension": {"type": "integer", "default": 0, "description": "Ascension level (0-20)"},
                },
            },
        ),
        types.Tool(
            name="bridge_console",
            description=(
                "Execute a dev console command in the running game. "
                "Examples: 'gold 999', 'godmode', 'relic add ANCHOR', 'card BASH', "
                "'fight LAGAVULIN_MATRIARCH_NORMAL', 'heal 999', 'win', 'kill', "
                "'potion BLOCK_POTION', 'power STRENGTH_POWER 5 0', 'draw 3', "
                "'unlock all', 'event ABYSSAL_BATHS'. "
                "Use get_console_commands tool to see all 39 available commands."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "command": {
                        "type": "string",
                        "description": "Console command to execute (e.g. 'gold 999', 'fight LAGAVULIN_MATRIARCH_NORMAL')",
                    },
                },
                "required": ["command"],
            },
        ),
    ]


# ─── Tool Handlers ───────────────────────────────────────────────────────────


@server.call_tool()
async def call_tool(name: str, arguments: dict) -> list[types.TextContent]:
    try:
        result = await _handle_tool(name, arguments)
        if isinstance(result, str):
            return [types.TextContent(type="text", text=result)]
        return [types.TextContent(type="text", text=json.dumps(result, indent=2, default=str))]
    except Exception as e:
        return [types.TextContent(type="text", text=f"Error: {type(e).__name__}: {e}")]


async def _handle_tool(name: str, args: dict):
    # ── Game Data Query ──
    if name == "list_entities":
        results = game_data.list_entities(
            entity_type=args.get("entity_type", ""),
            query=args.get("query", ""),
            rarity=args.get("rarity", ""),
            limit=args.get("limit", 200),
        )
        return {"count": len(results), "entities": results}

    elif name == "get_entity_source":
        source = game_data.get_source(args["class_name"])
        if source:
            info = game_data.get_entity_info(args["class_name"])
            header = ""
            if info:
                header = f"// Type: {info.get('type', '?')} | Namespace: {info.get('namespace', '?')} | Base: {info.get('base_class', '?')}\n\n"
            return header + source
        return f"Class '{args['class_name']}' not found. Try search_game_code to locate it."

    elif name == "search_game_code":
        results = game_data.search_code(
            args["pattern"],
            max_results=args.get("max_results", 50),
        )
        return {"count": len(results), "results": results}

    elif name == "list_hooks":
        hooks = game_data.get_hooks(
            category=args.get("category", ""),
            subcategory=args.get("subcategory", ""),
        )
        return {"count": len(hooks), "hooks": hooks}

    elif name == "get_game_info":
        game_data.ensure_indexed()
        release_info_path = Path(GAME_DIR) / "release_info.json"
        release_info = {}
        if release_info_path.exists():
            try:
                release_info = json.loads(release_info_path.read_text())
            except Exception:
                pass

        return {
            "game_dir": GAME_DIR,
            "decompiled_dir": DECOMPILED_DIR,
            "data_dir": str(Path(GAME_DIR) / "data_sts2_windows_x86_64"),
            "mods_dir": str(Path(GAME_DIR) / "mods"),
            "release_info": release_info,
            "entity_summary": game_data.get_entity_types_summary(),
            "total_entities": len(game_data.entities),
            "total_hooks": len(game_data.hooks),
            "total_console_commands": len(game_data.console_commands),
            "engine": "Godot 4.5.1 C# (.NET 9.0)",
            "modding_libraries": ["Harmony 2.4.2", "MonoMod"],
        }

    elif name == "get_console_commands":
        return game_data.get_console_commands()

    elif name == "browse_namespace":
        namespace = args["namespace"]
        read_file = args.get("read_file", "")

        if read_file:
            content = game_data.get_source_by_path(f"{namespace}/{read_file}")
            if content:
                return content
            return f"File '{read_file}' not found in namespace '{namespace}'"

        files = game_data.list_files_in_namespace(namespace)
        if files:
            return {"namespace": namespace, "file_count": len(files), "files": files}

        # Try listing available namespaces
        namespaces = game_data.list_namespaces()
        matches = [ns for ns in namespaces if args["namespace"].lower() in ns.lower()]
        if matches:
            return {"error": f"Namespace '{namespace}' not found. Did you mean one of these?", "suggestions": matches}
        return {"error": f"Namespace '{namespace}' not found", "available_count": len(namespaces)}

    elif name == "get_modding_guide":
        return _get_guide(args["topic"])

    # ── Mod Creation ──
    elif name == "create_mod_project":
        return mod_gen.create_mod_project(
            mod_name=args["mod_name"],
            author=args["author"],
            description=args.get("description", ""),
            output_dir=args.get("output_dir", ""),
            use_baselib=args.get("use_baselib", True),
        )

    elif name == "generate_card":
        return mod_gen.generate_card(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            card_type=args.get("card_type", "Attack"),
            rarity=args.get("rarity", "Common"),
            target_type=args.get("target_type", "AnyEnemy"),
            energy_cost=args.get("energy_cost", 1),
            damage=args.get("damage", 0),
            block=args.get("block", 0),
            magic_number=args.get("magic_number", 0),
            keywords=args.get("keywords"),
            pool=args.get("pool", "ColorlessCardPool"),
            description=args.get("description", ""),
            upgrade_description=args.get("upgrade_description", ""),
            use_baselib=args.get("use_baselib", True),
        )

    elif name == "generate_relic":
        return mod_gen.generate_relic(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            rarity=args.get("rarity", "Common"),
            pool=args.get("pool", "SharedRelicPool"),
            description=args.get("description", ""),
            flavor=args.get("flavor", ""),
            trigger_hook=args.get("trigger_hook", ""),
            use_baselib=args.get("use_baselib", True),
        )

    elif name == "generate_power":
        return mod_gen.generate_power(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            power_type=args.get("power_type", "Buff"),
            stack_type=args.get("stack_type", "Counter"),
            description=args.get("description", ""),
            trigger_hook=args.get("trigger_hook", ""),
            use_baselib=args.get("use_baselib", True),
            mod_name=args.get("mod_name", ""),
        )

    elif name == "generate_potion":
        return mod_gen.generate_potion(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            rarity=args.get("rarity", "Common"),
            usage=args.get("usage", "CombatOnly"),
            target_type=args.get("target_type", "None"),
            pool=args.get("pool", "SharedPotionPool"),
            block=args.get("block", 0),
            description=args.get("description", ""),
            use_baselib=args.get("use_baselib", True),
        )

    elif name == "generate_monster":
        return mod_gen.generate_monster(
            mod_namespace=args["mod_namespace"],
            mod_name=args["mod_name"],
            class_name=args["class_name"],
            min_hp=args.get("min_hp", 50),
            max_hp=args.get("max_hp", 55),
            moves=args.get("moves"),
            image_size=args.get("image_size", 200),
        )

    elif name == "generate_encounter":
        return mod_gen.generate_encounter(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            room_type=args.get("room_type", "Monster"),
            monsters=args.get("monsters"),
        )

    elif name == "generate_harmony_patch":
        return mod_gen.generate_harmony_patch(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            target_type=args["target_type"],
            target_method=args["target_method"],
            patch_type=args.get("patch_type", "Postfix"),
        )

    elif name == "generate_localization":
        return mod_gen.generate_localization(
            mod_id=args["mod_id"],
            entity_type=args["entity_type"],
            entity_name=args["entity_name"],
            title=args.get("title", ""),
            description=args.get("description", ""),
            flavor=args.get("flavor", ""),
            upgrade_description=args.get("upgrade_description", ""),
        )

    # ── BaseLib Tools ──
    elif name == "generate_character":
        return mod_gen.generate_character(
            mod_namespace=args["mod_namespace"],
            mod_name=args["mod_name"],
            class_name=args["class_name"],
            starting_hp=args.get("starting_hp", 80),
            starting_gold=args.get("starting_gold", 99),
            orb_slots=args.get("orb_slots", 0),
        )

    elif name == "generate_mod_config":
        return mod_gen.generate_mod_config(
            mod_namespace=args["mod_namespace"],
            class_name=args.get("class_name", "MyModConfig"),
            properties=args.get("properties"),
        )

    elif name == "get_baselib_reference":
        return _get_baselib_reference(args["topic"])

    # ── Build & Deploy ──
    elif name == "build_mod":
        return mod_gen.build_mod(args["project_dir"])

    elif name == "install_mod":
        return mod_gen.install_mod(
            args["project_dir"],
            mod_name=args.get("mod_name", ""),
        )

    elif name == "uninstall_mod":
        return mod_gen.uninstall_mod(args["mod_name"])

    elif name == "list_installed_mods":
        return mod_gen.list_installed_mods()

    elif name == "launch_game":
        return _launch_game(
            remote_debug=args.get("remote_debug", False),
            renderer=args.get("renderer"),
            extra_args=args.get("extra_args", ""),
        )

    elif name == "decompile_game":
        return await _decompile_game()

    # ── Live Bridge ──
    elif name == "bridge_ping":
        from . import bridge_client
        return bridge_client.ping()

    elif name == "bridge_get_run_state":
        from . import bridge_client
        return bridge_client.get_run_state()

    elif name == "bridge_get_combat_state":
        from . import bridge_client
        return bridge_client.get_combat_state()

    elif name == "bridge_get_player_state":
        from . import bridge_client
        return bridge_client.get_player_state()

    elif name == "bridge_get_screen_state":
        from . import bridge_client
        return bridge_client.get_screen_state()

    elif name == "bridge_start_run":
        from . import bridge_client
        return bridge_client.start_run(
            character=args.get("character", "Ironclad"),
            ascension=args.get("ascension", 0),
        )

    elif name == "bridge_console":
        from . import bridge_client
        return bridge_client.execute_console_command(args["command"])

    else:
        return f"Unknown tool: {name}"


# ─── Guides ──────────────────────────────────────────────────────────────────

def _get_guide(topic: str) -> str:
    guides = {
        "getting_started": """\
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
""",
        "cards": """\
# Creating Custom Cards

## Base Class: CardModel
Cards extend `CardModel` and override key properties:
- `Type`: CardType.Attack/Skill/Power/Status/Curse
- `Rarity`: CardRarity.Basic/Common/Uncommon/Rare
- `TargetType`: AnyEnemy/AllEnemies/RandomEnemy/None/Self/AnyAlly/AllAllies
- `EnergyCost`: Integer energy cost
- `Keywords`: HashSet<CardKeyword> (Exhaust, Ethereal, Innate, Retain, Sly, Eternal)

## Pool Registration
Use `[Pool(typeof(PoolClass))]` attribute:
- `IroncladCardPool`, `SilentCardPool`, `RegentCardPool`, `NecrobinderCardPool`, `DefectCardPool`
- `ColorlessCardPool` (shared)

## Dynamic Variables
Override `CanonicalVars` to provide numeric values:
- `DamageVar(amount)` - Attack damage
- `BlockVar(amount)` - Block amount
- `MagicNumberVar(amount)` - Generic number
- `PowerVar<TPower>(amount)` - Power stack amount

Access in OnPlay: `DynamicVars.Damage.BaseValue`, `DynamicVars.Block.BaseValue`

## Key Methods
- `OnPlay(PlayerChoiceContext, CardPlay)` - Main effect
- `OnUpgrade()` - Upgrade modifications
- `CanPlay()` - Playability check
- `IsValidTarget(Creature)` - Target validation

## Commands Pattern
- `DamageCmd.Attack(amount).FromCard(this, cardPlay).Execute(choiceContext)`
- `CreatureCmd.GainBlock(creature, amount, ValueProp.Powered, this)`
- `PowerCmd.Apply<TPower>(target, amount, source, card)`
- `CardPileCmd.Draw(player, count, choiceContext)`
- `CardPileCmd.Add(card, pileType)`

## Localization (cards.json)
```json
{
  "MY_CARD.title": "Card Name",
  "MY_CARD.description": "Deal [blue]{Damage}[/blue] damage.",
  "MY_CARD.upgrade.description": "Deal [blue]{Damage}[/blue] damage."
}
```

## Console Test: `card MY_CARD`
""",
        "relics": """\
# Creating Custom Relics

## Base Class: RelicModel
Override key properties:
- `Rarity`: RelicRarity.Starter/Common/Uncommon/Rare/Shop/Event/Ancient
- `IsStackable`: Whether multiple instances can exist (rare)

## Pool Registration
`[Pool(typeof(SharedRelicPool))]` or character-specific pools.

## Common Hook Methods to Override
- `BeforeCombatStart()` - Trigger at combat start
- `AfterCardPlayed(CombatState, PlayerChoiceContext, CardPlay)` - After any card played
- `AfterDamageReceived(PlayerChoiceContext, Creature, DamageResult, ValueProp, Creature?, CardModel?)` - After taking damage
- `AfterTurnEnd(CombatState, CombatSide)` - End of turn
- `ModifyDamageAdditive(...)` - Modify damage dealt
- `ModifyBlock(...)` - Modify block gained
- `AfterBlockGained(...)` - After gaining block
- `BeforeHandDraw(...)` - Before drawing cards

## Key Patterns
- `Flash()` - Play relic activation animation
- `Owner` - The Player who has this relic
- Use state fields (e.g., `_usedThisCombat`) and reset in `AfterCombatEnd`

## Images: 256x256 PNG with 10px black outline (60% opacity)
Place at: `{ModName}/images/relics/{snake_name}.png`

## Localization (relics.json)
```json
{
  "MY_RELIC.title": "Relic Name",
  "MY_RELIC.description": "Effect description with [blue]{Value}[/blue].",
  "MY_RELIC.flavor": "Flavor text..."
}
```

## Console Test: `relic add MY_RELIC`
""",
        "powers": """\
# Creating Custom Powers (Buffs/Debuffs)

## Base Class: PowerModel
Override key properties:
- `Type`: PowerType.Buff or PowerType.Debuff
- `StackType`: PowerStackType.Counter (stackable) or PowerStackType.Single

## Key Properties
- `Amount` - Current stack count
- `Owner` - Creature with this power
- `Applier` - Creature that applied it

## Common Hook Methods
- `ModifyDamageAdditive(...)` - Add flat damage (like Strength)
- `ModifyDamageMultiplicative(...)` - Multiply damage (like Vulnerable: 1.5x)
- `BeforeHandDraw(Player, PlayerChoiceContext, CombatState)` - Before draw step
- `AfterTurnEnd(CombatState, CombatSide)` - End of turn (tick down with `PowerCmd.Decrement(this)`)
- `AfterCardPlayed(...)` - React to cards
- `BeforeDamageReceived(...)` - Before taking damage

## Images: 256x256 PNG with 10px black outline (60% opacity)

## Localization (powers.json)
```json
{
  "MY_POWER.title": "Power Name",
  "MY_POWER.smartDescription": "Effect with [blue]{Amount}[/blue] stacks and {Amount:plural:time|times}.",
  "MY_POWER.description": "Base description for 1 stack."
}
```

## Console Test: `power MY_POWER 3 0` (3 stacks on player index 0)
""",
        "potions": """\
# Creating Custom Potions

## Base Class: PotionModel
Override key properties:
- `Rarity`: PotionRarity.Common/Uncommon/Rare
- `Usage`: PotionUsage.CombatOnly/OutOfCombat/Anywhere
- `TargetType`: None/AnyEnemy/AnyAlly/AnyPlayer/AllEnemies/AllAllies

## Pool Registration
`[Pool(typeof(SharedPotionPool))]` or character-specific pools.

## Key Methods
- `OnUse(PlayerChoiceContext, Creature?)` - Main effect

## Localization (potions.json)
```json
{
  "MY_POTION.title": "Potion Name",
  "MY_POTION.description": "Effect description."
}
```

## Console Test: `potion MY_POTION`
""",
        "monsters": """\
# Creating Custom Monsters

## Base Class: MonsterModel
Override key properties:
- `MinInitialHp` / `MaxInitialHp` - HP range (random each combat)
- `VisualsPath` - Path to .tscn scene file

## Move State Machine
Override `GenerateMoveStateMachine()`:
```csharp
var strike = new MoveState("STRIKE", Strike, new SingleAttackIntent(10));
var defend = new MoveState("DEFEND", Defend, new DefendIntent());
strike.FollowUpState = defend;
defend.FollowUpState = strike;
return new MonsterMoveStateMachine(new List<MonsterState> { strike, defend }, strike);
```

## Intent Types
- `SingleAttackIntent(damage)` - Single attack
- `MultiAttackIntent(damage, count)` - Multi-hit
- `DefendIntent()` - Gaining block
- `BuffIntent()` - Applying buff
- `DebuffIntent()` - Applying debuff
- `new AbstractIntent[] { ... }` - Multiple intents per turn

## Randomized Starting Move
Use `RandomBranchState` with `AddBranch(state, MoveRepeatType.CannotRepeat)`

## Scene File (.tscn)
Required nodes: Visuals (Sprite2D), Bounds (Control), CenterPos (Marker2D), IntentPos (Marker2D).
Use `generate_monster` to auto-generate the scene.

## IMPORTANT: CreateVisualsPatch
Custom static-image monsters NEED a Harmony patch on `MonsterModel.CreateVisuals`.
Use `generate_monster` which includes instructions, or see the advanced modding guide.

## Ascension Scaling
`AscensionHelper.GetValueIfAscension(AscensionLevel.ToughEnemies, scaledValue, baseValue)`

## Console Test: `fight ENCOUNTER_NAME`
""",
        "encounters": """\
# Creating Custom Encounters

## Base Class: EncounterModel
Override:
- `RoomType`: Monster/Elite/Boss
- `AllPossibleMonsters`: Yield all monster types
- `GenerateMonsters()`: Return list of (MonsterModel, slotName?) tuples

## Adding to an Act
Harmony patch the act's `GenerateAllEncounters`:
```csharp
[HarmonyPatch(typeof(Underdocks), nameof(Underdocks.GenerateAllEncounters))]
public static class MyPatch {
    public static void Postfix(ref IEnumerable<EncounterModel> __result) {
        var list = __result.ToList();
        list.Add(ModelDb.Encounter<MyEncounter>());
        __result = list;
    }
}
```

## Acts: Underdocks (Act 1), Metropolis (Act 2), Glory (Act 3), TheHeart (Act 4)

## Encounter Scenes
For multi-monster encounters with specific positions, use `HasScene = true`
and create a .tscn with Marker2D nodes for slot positions.
""",
        "events": """\
# Creating Custom Events

## Base Class: EventModel
Events are choice-based narrative encounters. Override:
- `IsShared` - All players see same event
- `IsDeterministic` - Seed-affected
- `LayoutType` - Default/Combat/Ancient/Custom
- `GenerateInitialOptions()` - Create initial choices

## Key Methods
- `BeginEvent(Player, bool)` - Start event
- `SetEventState(LocString, IEnumerable<EventOption>)` - Update display
- `SetEventFinished(LocString)` - End event
- `EnterCombatWithoutExitingEvent()` - Trigger combat within event

## EventOption
Each choice has:
- Text (LocString)
- A callback method
- Optional conditions

## See existing events via: `list_entities` with `entity_type=event`
""",
        "harmony_patches": """\
# Harmony Patches

## Overview
Harmony patches let you modify game methods at runtime without changing game files.
The game ships with Harmony 2.4.2 (`0Harmony.dll`).

## Patch Types
- **Prefix**: Runs BEFORE original method. Return `false` to skip original.
- **Postfix**: Runs AFTER original method. Can modify `__result`.
- **Transpiler**: Modify IL code directly (advanced).

## Syntax
```csharp
[HarmonyPatch(typeof(TargetClass), nameof(TargetClass.TargetMethod))]
public static class MyPatch
{
    // Prefix - can prevent original from running
    public static bool Prefix(TargetClass __instance, ref ReturnType __result) { ... }

    // Postfix - runs after, can modify result
    public static void Postfix(TargetClass __instance, ref ReturnType __result) { ... }
}
```

## Special Parameters
- `__instance` - The object the method is called on
- `__result` - Return value (ref in postfix to modify)
- `__state` - Pass data from prefix to postfix
- Parameter names matching original method parameters

## Accessing Private Fields
- `Traverse.Create(__instance).Field("_fieldName").GetValue<Type>()`
- `AccessTools.FieldRefAccess<Type, FieldType>("fieldName")`

## Manual Patching
```csharp
var harmony = new Harmony("my.mod.id");
harmony.PatchAll(); // Auto-discover all [HarmonyPatch] classes
// or manually:
harmony.Patch(originalMethod, new HarmonyMethod(prefixMethod));
```

## Common Targets
- `CardModel.PortraitPath` (getter) - Replace card images
- `MonsterModel.CreateVisuals` - Custom monster visuals
- `CombatManager.SetReadyToEndTurn` - End turn events
- `NGame._Input` - Custom keybindings
- `NGame.LaunchMainMenu` - Skip splash screen
- Act `.GenerateAllEncounters` - Add encounters
""",
        "localization": """\
# Localization System

## File Structure
Place JSON files in: `{ModName}/localization/eng/`
- `cards.json` - Card text
- `relics.json` - Relic text
- `powers.json` - Power text
- `potions.json` - Potion text
- `monsters.json` - Monster names
- `encounters.json` - Encounter text

## Key Format
Keys use SCREAMING_SNAKE_CASE model IDs:
`MY_CARD.title`, `MY_CARD.description`, `MY_CARD.upgrade.description`

## Rich Text Tags
- `[gold]keyword name[/gold]` - Gold color for game keywords
- `[blue]{Value}[/blue]` - Blue for dynamic numbers
- Other colors: `[red]`, `[green]`, `[gray]`

## SmartFormat Variables
- `{Damage}` - Card damage value
- `{Block}` - Block value
- `{Amount}` - Power stack count
- `{StrengthPower}` - Power amount reference
- `{Amount:plural:card|cards}` - Pluralization
- `{count:conditional:text_if_true|text_if_false}` - Conditionals

## Languages
eng, zhs, deu, esp, fra, ita, jpn, kor, pol, ptb, rus, spa, tha, tur
""",
        "console": """\
# Developer Console

## Enabling
- Any loaded mod enables the full console automatically
- Manual: edit settings.save, add `"full_console": true`
- Open with backtick (`) key

## Key Commands
- `help [cmd]` - List commands or get help for specific command
- `card <id>` - Add card to hand/deck
- `relic add <id>` / `relic remove <id>` - Manage relics
- `potion <id>` - Add potion
- `fight <id>` - Start encounter
- `event <id>` - Start event
- `gold <amount>` - Add gold
- `heal <amount>` - Heal player
- `block <amount>` - Add block
- `energy` - Add energy
- `draw <count>` - Draw cards
- `discard <count>` - Discard cards
- `power <id> <amount> <player_index>` - Apply power
- `godmode` - Invincibility
- `win` - Win current combat
- `kill` - Kill enemies
- `unlock all` - Unlock all content
- `travel <node>` - Travel to map node
- `room <type>` - Enter room type
- `log <type> <level>` - Set log level
- `open saves` - Open saves directory
""",
        "hooks": """\
# Game Hook System

## Overview
Hooks are the primary way content (cards, relics, powers) interacts with game events.
All hooks are defined in `Hook.cs` and called on all `AbstractModel` instances in combat.

## Hook Categories

### Before Hooks (pre-event, can prepare)
BeforeCombatStart, BeforeCardPlayed, BeforeTurnEnd, BeforeHandDraw,
BeforeDamageReceived, BeforeBlockGained, BeforePowerAmountChanged,
BeforeCardRemoved, BeforeRoomEntered, BeforeRewardsOffered, BeforePotionUsed,
BeforeFlush, BeforePlayPhaseStart, BeforeCardAutoPlayed, BeforeDeath

### After Hooks (post-event, react)
AfterCombatEnd, AfterCombatVictory, AfterCardPlayed, AfterCardDrawn,
AfterCardDiscarded, AfterCardExhausted, AfterCardRetained,
AfterDamageReceived, AfterDamageGiven, AfterBlockGained, AfterBlockBroken,
AfterPowerAmountChanged, AfterTurnEnd, AfterEnergyReset, AfterHandEmptied,
AfterShuffle, AfterRoomEntered, AfterRewardTaken, AfterItemPurchased,
AfterPotionUsed, AfterRestSiteHeal, AfterRestSiteSmith, AfterGoldGained,
AfterDeath, AfterCreatureAddedToCombat, AfterOrbChanneled, AfterOrbEvoked

### Modify Hooks (change values, return modified value)
ModifyDamage, ModifyBlock, ModifyHandDraw, ModifyMaxEnergy,
ModifyEnergyCostInCombat, ModifyCardRewardOptions, ModifyMerchantPrice,
ModifyPowerAmountGiven, ModifyPowerAmountReceived, ModifyHealAmount,
ModifyRestSiteHealAmount, ModifyRewards, ModifyGeneratedMap, ModifyXValue,
ModifyAttackHitCount, ModifyCardPlayCount, ModifyStarCost, ModifyOrbValue

### Should Hooks (boolean gates, return true/false)
ShouldDie, ShouldDraw, ShouldPlay, ShouldFlush, ShouldClearBlock,
ShouldGainGold, ShouldGainStars, ShouldAfflict, ShouldEtherealTrigger,
ShouldAllowHitting, ShouldAllowTargeting, ShouldTakeExtraTurn,
ShouldProcurePotion, ShouldStopCombatFromEnding

## Use `list_hooks` tool for complete list with full signatures.
""",
        "pools": """\
# Pool System

## Overview
Pools determine which entities appear for which characters. Add to pools with `[Pool]` attribute.

## Card Pools
- `IroncladCardPool` - Ironclad character
- `SilentCardPool` - Silent character
- `RegentCardPool` - Regent character
- `NecrobinderCardPool` - Necrobinder character
- `DefectCardPool` - Defect character
- `ColorlessCardPool` - Available to all characters

## Relic Pools
- `SharedRelicPool` - Available to all characters
- `IroncladRelicPool`, `SilentRelicPool`, etc. - Character-specific
- `EventRelicPool` - From events only
- `FallbackRelicPool` - Fallback options

## Potion Pools
- `SharedPotionPool` - Available to all
- Character-specific potion pools

## Mod Registration
Use `[Pool(typeof(PoolName))]` on your entity class.
Alternatively, use `ModHelper.AddModelToPool<PoolType, ModelType>()` in your mod initializer
(must be called BEFORE pools are frozen during game init).
""",
        "building": """\
# Building & Deploying Mods

## Build Process
1. `dotnet build YourMod.csproj -c Debug`
2. Output DLL at `.godot/mono/temp/bin/Debug/YourMod.dll` (or `bin/Debug/`)

## PCK Export (for resources)
1. Open Godot 4.5.1
2. Create export template
3. Resources tab → "Export selected resources (and dependencies)"
4. Select mod_image.png, mod_manifest.json, localization files, monster scenes
5. Export as .pck

## Installation
Copy to game's `mods/` folder:
```
mods/
└── yourmod/
    ├── yourmod.dll         # Compiled mod assembly
    ├── yourmod.pck         # Resource pack (optional)
    ├── mod_manifest.json   # Required metadata
    └── mod_image.png       # Optional mod icon
```

## mod_manifest.json
```json
{
  "id": "yourmod",
  "pck_name": "YourMod",
  "name": "Your Mod",
  "author": "You",
  "description": "What it does",
  "version": "1.0.0",
  "has_pck": true,
  "has_dll": true,
  "affects_gameplay": true,
  "dependencies": []
}
```
""",
        "debugging": """\
# Debugging Mods

## Remote Debugging (Godot Output)
1. In Steam, add launch parameter: `--remote-debug tcp://127.0.0.1:6007`
2. Open Godot editor (port 6007 is default for debug server)
3. Check "Keep Debug Server Open" under Debug tab
4. Launch STS2 through Steam
5. Godot's Output panel shows all game logs

## In-Game Console
Press backtick (`) to open. Key debug commands:
- `log Generic DEBUG` - Enable debug logging
- `godmode` - Invincibility for testing
- `fight ENCOUNTER_ID` - Jump to specific encounter
- `card CARD_ID` - Add specific card
- `gold 999` - Get gold

## Logging in Code
```csharp
using MegaCrit.Sts2.Core.Logging;
Log.Info("message");    // Standard info
Log.Warn("message");    // Warning (yellow)
Log.Error("message");   // Error (red)
```

## File Logging
Write to: `UserDataPathProvider.GetAccountScopedBasePath("mymod_log.txt")`

## Common Issues
- Mod not showing: Check mod_manifest.json format and mod folder structure
- Assembly load failure: Ensure .NET 9.0 target, check DLL dependencies
- Null reference: Models must be accessed after initialization; check hook timing
- PCK not loading: Verify pck_name in manifest matches actual .pck filename
""",
        "project_structure": """\
# Project Structure Reference

## Recommended Layout
```
MyMod/
├── MyMod.csproj                    # .NET 9.0 project
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
- TargetFramework: net9.0
- GodotSharp 4.4.0
- Lib.Harmony 2.4.2
- Reference to sts2.dll (Private=false)
""",
    }
    return guides.get(topic, f"Unknown topic: {topic}. Available: {', '.join(guides.keys())}")


# ─── Utility Functions ───────────────────────────────────────────────────────

def _get_baselib_reference(topic: str) -> str:
    refs = {
        "overview": """\
# BaseLib (Alchyr.Sts2.BaseLib) - Community Modding Library

**Source:** E:\\Github\\BaseLib-StS2 | **NuGet:** Alchyr.Sts2.BaseLib | **Version:** 0.1.6

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
""",
        "custom_card": """\
# BaseLib: CustomCardModel

Extends `CardModel` with:
- `GainsBlock` auto-detected from BlockVar in CanonicalVars
- Custom frame support via pool's FramePath/FrameMaterial
- Auto-registration in CustomContentDictionary
- ICustomModel prefix applied automatically

Usage: Same as CardModel but extend `CustomCardModel` instead.
Your card pool should extend `CustomCardPoolModel` for custom frames.
""",
        "custom_relic": """\
# BaseLib: CustomRelicModel

Extends `RelicModel` with:
- Auto-add to content dictionary
- ICustomModel prefix for IDs
- Works with CustomRelicPoolModel for pool-specific features

Usage: Same as RelicModel but extend `CustomRelicModel` instead.
""",
        "custom_power": """\
# BaseLib: CustomPowerModel + ICustomPower

Extends `PowerModel` with:
- ICustomModel marker for ID prefixing
- Implement `ICustomPower` interface for custom icons:
  ```csharp
  public string PackedIcon => "res://MyMod/images/powers/my_power_packed.png";  // 64x64
  public string BigIcon => "res://MyMod/images/powers/my_power.png";            // 256x256
  public string? BigBetaIcon => null;                                           // optional
  ```
""",
        "custom_potion": """\
# BaseLib: CustomPotionModel

Extends `PotionModel` with:
- `AutoAdd` property (default true) for automatic pool registration
- ICustomModel prefix applied automatically
""",
        "custom_character": """\
# BaseLib: CustomCharacterModel

Full pipeline for creating new playable characters. Override:

**Visual Assets:**
- `VisualsPath` - Character .tscn scene
- `SelectScreenBgPath` - Character select background
- `EnergyCounterPath` - Energy counter .tscn
- Trail settings, icon paths

**Animation:**
- `SetupAnimator()` - Custom animation configuration
- Attack/Cast/Death animation name overrides

**Audio:**
- `AttackSfx`, `CastSfx`, `DeathSfx` paths

**Character Select UI:**
- `CharSelectInfoPath` - Info panel scene

**Gameplay:**
- `StartingMaxHp`, `StartingGold`, `OrbSlots`
- `StarterDeck()` - Returns initial card list
- `StarterRelics()` - Returns initial relic list
- `CardPoolModel` - Must return your CustomCardPoolModel

**Pool Models (create alongside character):**
- `CustomCardPoolModel` - Card pool with custom frames, materials, shader colors
- `CustomRelicPoolModel` - Relic pool
- `CustomPotionPoolModel` - Potion pool
- All support `ICustomEnergyIconPool` for custom energy icons

Register with `[Pool(typeof(YourCharacterCardPool))]` on cards.
""",
        "custom_ancient": """\
# BaseLib: CustomAncientModel

Create ancient (legendary) events:

**Key Features:**
- `OptionPools` system - Manages 3 option slots with weighted random selection
- Automatic dialogue loading from localization
- Force-spawn control with `ForceSpawn` / `ForceSpawnConflicts`
- Map/run history icon paths

**Required Overrides:**
- `OptionPools` property - Return OptionPools<AncientOption> instance
- `GenerateOptions()` - Create the 3 options from pools
- `ProcessOption()` - Handle player's choice

**Localization Format:**
- `{ID}.intro.text` / `.sfx` - Introduction dialogue
- `{ID}.option_{N}.text` / `.sfx` - Option dialogue for each slot
""",
        "config": """\
# BaseLib: Configuration System

## SimpleModConfig
```csharp
public class MyConfig : SimpleModConfig
{
    public override string FileName => "my_config";

    [ConfigSection("General")]
    public bool EnableFeature { get; set; } = true;

    [ConfigSection("Tuning")]
    [SliderRange(0.5, 3.0, 0.1)]
    [SliderLabelFormat("{0:0.00}x")]
    public double DamageMultiplier { get; set; } = 1.0;
}
```

## Registration (in ModEntry.Init()):
```csharp
var config = new MyConfig();
ModConfigRegistry.Register("mymodid", config);
```

## Access anywhere:
```csharp
var config = ModConfigRegistry.Get<MyConfig>("mymodid");
if (config.EnableFeature) { ... }
```

## Features:
- Auto-generates in-game UI with config button in top bar
- Supports bool (checkbox), double (slider), enum (dropdown)
- `[ConfigSection("Name")]` groups properties under headers
- `[SliderRange(min, max, step)]` for numeric ranges
- Auto-saves after 5s delay when changed
- Saved to `%APPDATA%\\.baselib\\{ModName}\\{FileName}.cfg`
""",
        "card_variables": """\
# BaseLib: Custom Card Variables

## ExhaustiveVar
Card's value decreases each time it's played (like Exhaustive keyword).
```csharp
new ExhaustiveVar(startingValue).WithTooltip()
```

## PersistVar
Value decreases each time played within a single turn.
```csharp
new PersistVar(startingValue).WithTooltip()
```

## RefundVar
Refunds energy when the card is played.
```csharp
new RefundVar(refundAmount).WithTooltip()
```

All support `.WithTooltip()` for automatic hover tip generation.
""",
        "common_actions": """\
# BaseLib: CommonActions

Static helper methods that reduce boilerplate for common card/power/relic effects:

```csharp
using BaseLib.Utils;

// Attack from a card (handles targeting, damage calculation, VFX)
await CommonActions.CardAttack(this, cardPlay, choiceContext);

// Gain block from a card
await CommonActions.CardBlock(this, choiceContext);

// Draw cards
await CommonActions.Draw(player, count, choiceContext);

// Apply a power
await CommonActions.Apply<StrengthPower>(target, amount, source, card);
await CommonActions.ApplySelf<StrengthPower>(owner, amount, card);

// Card selection UI (pick from a list)
var selected = await CommonActions.SelectCards(cards, count, message, choiceContext);
var single = await CommonActions.SelectSingleCard(cards, message, choiceContext);
```

These handle all the boilerplate around PlayerChoiceContext, ValueProp flags, etc.
""",
        "spire_field": """\
# BaseLib: SpireField<TKey, TVal>

Attach custom data to game objects without modifying their classes:

```csharp
// Define a field
private static readonly SpireField<Creature, int> _customCounter =
    new SpireField<Creature, int>(() => 0);  // default factory

// Use it
_customCounter[creature] = 5;
int count = _customCounter[creature];  // returns 5, or 0 for unset creatures
```

Uses `ConditionalWeakTable` internally - data is garbage collected with the key object.
""",
        "weighted_list": """\
# BaseLib: WeightedList<T>

IList<T> with weighted random selection:

```csharp
var list = new WeightedList<string>();
list.Add("common", 70);     // 70% chance
list.Add("uncommon", 25);   // 25% chance
list.Add("rare", 5);        // 5% chance

// Pick random (weighted)
string result = list.GetRandom(rng);

// Pick and remove
string result = list.GetRandom(rng, removeAfter: true);
```
""",
        "il_patching": """\
# BaseLib: IL Patching Utilities

## InstructionMatcher
Fluent builder for matching IL instruction sequences in Harmony transpilers:

```csharp
var matcher = new InstructionMatcher()
    .Ldarg_0()
    .Call(typeof(SomeClass).GetMethod("SomeMethod"))
    .Stloc(2);

var patcher = new InstructionPatcher(instructions);
if (patcher.Match(matcher))
{
    patcher.Replace(new[] {
        // replacement instructions
    });
}
```

## InstructionPatcher Methods
- `Match()` / `MatchStart()` / `MatchEnd()` - Find instruction patterns
- `Step(n)` - Move cursor position
- `Replace()` / `ReplaceLastMatch()` - Replace matched instructions
- `Insert()` / `InsertCopy()` - Insert new instructions
- `GetLabels()` / `GetOperandLabel()` - Extract IL labels
- `PrintLog()` / `PrintResult()` - Debug output

## PatchAsyncMoveNext Extension
For patching async methods (common in STS2):
```csharp
harmony.PatchAsyncMoveNext(
    typeof(CombatManager).GetMethod("StartTurn"),
    transpiler: new HarmonyMethod(typeof(MyPatch).GetMethod("Transpiler"))
);
```
""",
        "mod_interop": """\
# BaseLib: Mod Interop System

Soft-depend on other mods without hard DLL references:

```csharp
[ModInterop(modId: "othermod")]
public static class OtherModCompat
{
    [InteropTarget(Type = "OtherMod.SomeClass", Name = "SomeMethod")]
    public static Func<int, bool>? CheckSomething;
}
```

At runtime, if "othermod" is loaded, `CheckSomething` gets bound to the real method.
If not loaded, it stays null. Call with null check:

```csharp
if (OtherModCompat.CheckSomething?.Invoke(42) == true) { ... }
```
""",
        "utilities": """\
# BaseLib: Utility Classes

## GodotUtils
- `CreatureVisualsFromScene(path)` - Load scene as NCreatureVisuals (no CreateVisuals patch needed!)
- `TransferAllNodes(from, to)` - Move all children between nodes

## ShaderUtils
- `GenerateHsv(hue, sat, val)` - Create HSV shader material for color-shifted sprites

## GeneratedNodePool<T>
Object pooling for Godot nodes:
```csharp
var pool = new GeneratedNodePool<MyNode>(() => new MyNode());
pool.Initialize(preWarmCount: 5);
var node = pool.Get();
pool.Return(node);  // cleans up signals automatically
```

## Extension Methods
- `type.GetPrefix()` - Get mod ID prefix from namespace
- `dynamicVar.CalculateBlock()` - Calculate block with all modifiers
- `dynamicVar.WithTooltip()` - Add hover tooltip
- `harmony.PatchAsyncMoveNext()` - Patch async state machines
- `control.DrawDebug()` - Debug UI rectangles
- `float.OrFast()` - Apply FastMode speed multipliers
- `valueProp.IsPoweredAttack_()` - Check ValueProp flags
""",
    }
    return refs.get(topic, f"Unknown BaseLib topic: {topic}. Available: {', '.join(refs.keys())}")


def _launch_game(remote_debug: bool = False, renderer: str | None = None, extra_args: str = "") -> dict:
    # Build Steam launch options string for any extra flags
    launch_opts = []
    if remote_debug:
        launch_opts.append("--remote-debug tcp://127.0.0.1:6007")
    if renderer:
        launch_opts.append(f"--rendering-driver {renderer}")
    if extra_args:
        launch_opts.append(extra_args)

    # Launch via Steam protocol (required - direct exe launch fails without Steam)
    try:
        import platform
        if platform.system() == "Windows":
            os.startfile(f"steam://rungameid/2868840")
        else:
            subprocess.Popen(["xdg-open", "steam://rungameid/2868840"])

        result: dict = {
            "success": True,
            "method": "steam",
            "steam_app_id": 2868840,
        }
        if launch_opts:
            result["note"] = f"Set these as Steam launch options: {' '.join(launch_opts)}"
        return result
    except Exception as e:
        return {"success": False, "error": str(e)}


async def _decompile_game() -> dict:
    dll_path = Path(GAME_DIR) / "data_sts2_windows_x86_64" / "sts2.dll"
    if not dll_path.exists():
        return {"success": False, "error": f"sts2.dll not found at {dll_path}"}

    output_dir = Path(DECOMPILED_DIR)
    # Clear existing
    if output_dir.exists():
        import shutil
        shutil.rmtree(str(output_dir))
    output_dir.mkdir(parents=True, exist_ok=True)

    try:
        result = subprocess.run(
            ["ilspycmd", "-p", "-o", str(output_dir), str(dll_path)],
            capture_output=True,
            text=True,
            timeout=300,
            env={**os.environ, "PATH": os.environ.get("PATH", "") + os.pathsep + os.path.expanduser("~/.dotnet/tools")},
        )
        if result.returncode == 0:
            # Reset index
            game_data._indexed = False
            game_data.entities.clear()
            game_data.by_type.clear()
            game_data.all_files.clear()
            game_data.hooks.clear()
            game_data.console_commands.clear()
            return {"success": True, "output_dir": str(output_dir), "message": "Decompilation complete. Index will rebuild on next query."}
        return {"success": False, "stderr": result.stderr}
    except FileNotFoundError:
        return {"success": False, "error": "ilspycmd not found. Install: dotnet tool install -g ilspycmd"}
    except subprocess.TimeoutExpired:
        return {"success": False, "error": "Decompilation timed out after 5 minutes"}


# ─── Entry Point ─────────────────────────────────────────────────────────────

async def main():
    async with stdio_server() as (read_stream, write_stream):
        await server.run(
            read_stream,
            write_stream,
            server.create_initialization_options(),
        )
