"""MCP Server for Slay the Spire 2 modding."""

import asyncio
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
from .pck_builder import build_pck, list_pck_contents
from .character_assets import get_character_asset_paths, scaffold_character_assets
from .analysis import CodeAnalyzer
from . import gdre_tools

# image_gen is imported lazily — it pulls in numpy, Pillow, rembg which are
# heavy / optional deps.  Importing eagerly would crash the server when only
# mcp[cli] is installed.
image_gen = None

def _get_image_gen():
    global image_gen
    if image_gen is None:
        try:
            from . import image_gen as _ig
            image_gen = _ig
        except ImportError as e:
            raise RuntimeError(
                "Image tools require extra dependencies. "
                "Install them with:  pip install Pillow numpy rembg google-genai"
            ) from e
    return image_gen

# ─── Configuration ────────────────────────────────────────────────────────────

from .setup import resolve_config, auto_detect_on_startup, get_setup_status as _get_setup_status

GAME_DIR, DECOMPILED_DIR = resolve_config()

# ─── Initialize ───────────────────────────────────────────────────────────────

server = Server("sts2-modding-mcp")
game_data = GameDataIndex(DECOMPILED_DIR)
mod_gen = ModGenerator(GAME_DIR)
analyzer = CodeAnalyzer(game_data)


async def _call_bridge(func, *args, **kwargs):
    return await asyncio.to_thread(func, *args, **kwargs)


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
            name="get_setup_status",
            description=(
                "Check the server setup status: whether the game was found, .NET/ilspycmd are installed, "
                "source is decompiled, GDRE tools are available. Use this to diagnose setup issues."
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
                "Get contextual documentation for modding STS2. Covers 40+ topics including content creation "
                "(cards, relics, powers, potions, monsters, encounters, events, enchantments, orbs, modifiers), "
                "systems (hooks, pools, combat_deep_dive, dynamic_vars, game_actions, mechanics, vfx_scenes, overlays), "
                "infrastructure (harmony_patches, localization, building, project_structure, resource_loading, godot_ui_construction), "
                "BaseLib (custom_keywords_and_piles, mod_config_integration), advanced (reflection_patterns, advanced_harmony, "
                "multiplayer_networking, rng_and_determinism, save_file_format, fastmp), and testing/debugging "
                "(debugging, testing, autoslay, console, bridge_setup, troubleshooting, workflows, game_log_parsing)."
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
                            "debugging", "project_structure", "modifiers",
                            "bridge_setup", "workflows", "troubleshooting",
                            "multiplayer_networking", "godot_ui_construction",
                            "reflection_patterns", "advanced_harmony",
                            "save_file_format", "game_log_parsing",
                            "combat_deep_dive", "custom_keywords_and_piles",
                            "mod_config_integration", "resource_loading",
                            "rng_and_determinism", "accessibility_patterns",
                            "image_generation", "testing", "autoslay",
                            "enchantments", "orbs", "game_actions",
                            "overlays", "dynamic_vars", "mechanics",
                            "vfx_scenes", "ui_elements", "fastmp",
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
                    "use_baselib": {
                        "type": "boolean",
                        "default": True,
                        "description": "Use BaseLib-enabled scaffolds and project layout when possible",
                    },
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
                    "use_baselib": {"type": "boolean", "default": True},
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
                    "use_baselib": {"type": "boolean", "default": True},
                    "mod_name": {
                        "type": "string",
                        "description": "Optional mod root name for resource-linked power icon paths",
                    },
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
                    "use_baselib": {"type": "boolean", "default": True},
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
            description=(
                "Build a mod project using project-aware defaults from its .csproj and manifest. "
                "Can optionally build the project's PCK artifact in the same call."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "project_dir": {"type": "string", "description": "Path to mod project directory"},
                    "configuration": {"type": "string", "default": "Debug"},
                    "build_pck_artifact": {"type": "boolean", "default": False},
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
                    "configuration": {"type": "string", "default": "Debug"},
                    "include_pck": {"type": "boolean", "description": "Copy the project PCK if present/expected"},
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
        # ── Asset & PCK Tools ──
        types.Tool(
            name="build_pck",
            description=(
                "Build a Godot .pck resource pack from a directory. Pure Python — no Godot install needed. "
                "Converts .png images to .ctex format with .import remaps. "
                "Packs .tscn scenes, .json, .tres files as-is. "
                "The PCK is required for mods that include visual assets (images, scenes, materials)."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "source_dir": {"type": "string", "description": "Directory containing mod assets to pack"},
                    "output_path": {"type": "string", "description": "Output .pck file path"},
                    "base_prefix": {"type": "string", "default": "", "description": "Path prefix in PCK (e.g., 'MyMod/' for res://MyMod/)"},
                    "convert_pngs": {"type": "boolean", "default": True, "description": "Convert PNGs to .ctex format"},
                },
                "required": ["source_dir", "output_path"],
            },
        ),
        types.Tool(
            name="list_pck",
            description="List the contents of a .pck file. Useful for debugging asset loading issues.",
            inputSchema={
                "type": "object",
                "properties": {
                    "pck_path": {"type": "string", "description": "Path to .pck file"},
                },
                "required": ["pck_path"],
            },
        ),
        types.Tool(
            name="scaffold_character_assets",
            description=(
                "Generate the complete directory structure and placeholder scenes for a new playable character. "
                "Creates 8+ .tscn scene files (combat visuals, energy counter, char select, rest site, merchant, etc.), "
                "localization entries, and a checklist of required image assets. "
                "Use with generate_character (for C# code) and build_pck (for packaging)."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_name": {"type": "string", "description": "Mod namespace/folder name"},
                    "class_name": {"type": "string", "description": "Character class name (PascalCase)"},
                    "output_dir": {"type": "string", "description": "Root output directory for assets"},
                    "sprite_size": {"type": "integer", "default": 300, "description": "Placeholder sprite size in pixels"},
                },
                "required": ["mod_name", "class_name", "output_dir"],
            },
        ),
        types.Tool(
            name="get_character_asset_paths",
            description=(
                "Get ALL required asset file paths for a character, organized by category. "
                "Shows exact res:// paths for combat visuals, energy counter, character select, "
                "icons, animations, SFX events, Spine animation names, and localization keys. "
                "Essential reference when creating character assets."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "char_id": {"type": "string", "description": "Character class name"},
                    "mod_name": {"type": "string", "description": "Mod folder name"},
                },
                "required": ["char_id", "mod_name"],
            },
        ),
        # ── GDRE Tools (Godot RE — game asset extraction & analysis) ──
        types.Tool(
            name="list_game_assets",
            description=(
                "List all files inside the game's Godot PCK archive (SlayTheSpire2.pck). "
                "Shows every res:// path the game uses — scenes, textures, scripts, resources, audio, etc. "
                "Use to discover asset paths for modding, find scene structures, or understand game layout. "
                "Requires gdre_tools binary (see GDRE_TOOLS_PATH env var)."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "filter_ext": {
                        "type": "string",
                        "description": "Filter by file extension (e.g. '.tscn', '.gd', '.tres', '.png', '.ogg')",
                    },
                    "filter_glob": {
                        "type": "string",
                        "description": "Glob pattern to filter paths (e.g. '**/Cards/**', '*.gdc')",
                    },
                },
            },
        ),
        types.Tool(
            name="search_game_assets",
            description=(
                "Search game asset paths by substring. Fast in-memory search across all files in the game PCK. "
                "Use to find specific assets by name — e.g. search 'Bash' to find all assets related to the Bash card, "
                "or 'character_select' to find character selection scenes."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "pattern": {
                        "type": "string",
                        "description": "Substring to search for in asset paths (case-insensitive)",
                    },
                    "extensions": {
                        "type": "array",
                        "items": {"type": "string"},
                        "description": "Optional extension filter (e.g. ['.tscn', '.tres'])",
                    },
                },
                "required": ["pattern"],
            },
        ),
        types.Tool(
            name="extract_game_assets",
            description=(
                "Extract files from the game PCK to a local directory for analysis. "
                "Can extract everything, filter by glob pattern, or extract only scripts. "
                "Useful for examining game scenes, understanding node hierarchies, or getting reference textures."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "output_dir": {
                        "type": "string",
                        "description": "Directory to extract files into",
                    },
                    "include": {
                        "type": "string",
                        "description": "Glob pattern for files to include (e.g. 'res://**/*.tscn', 'res://Scenes/Cards/**')",
                    },
                    "exclude": {
                        "type": "string",
                        "description": "Glob pattern for files to exclude",
                    },
                    "scripts_only": {
                        "type": "boolean",
                        "default": False,
                        "description": "Only extract script files (.gd, .gdc)",
                    },
                },
                "required": ["output_dir"],
            },
        ),
        types.Tool(
            name="recover_game_project",
            description=(
                "Full Godot project recovery from the game PCK — the asset-side equivalent of decompile_game. "
                "Extracts all assets, decompiles GDScript bytecode to readable .gd, and converts binary "
                "scenes/resources to text format (.tscn/.tres). Run once after game install or major update."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "output_dir": {
                        "type": "string",
                        "description": "Directory to recover the project into (default: recovered/ next to decompiled/)",
                    },
                },
            },
        ),
        types.Tool(
            name="decompile_gdscript",
            description=(
                "Decompile GDScript bytecode (.gdc) files to readable source (.gd). "
                "Use after extracting .gdc files from the game PCK to understand game scripts."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "input_path": {
                        "type": "string",
                        "description": "Path to .gdc file or glob pattern (e.g. 'extracted/**/*.gdc')",
                    },
                    "output_dir": {
                        "type": "string",
                        "description": "Output directory (default: same directory as input)",
                    },
                },
                "required": ["input_path"],
            },
        ),
        types.Tool(
            name="convert_resource",
            description=(
                "Convert between binary and text Godot resource formats. "
                "Binary→text: .scn/.res → .tscn/.tres (readable, editable). "
                "Text→binary: .tscn/.tres → .scn/.res (for packing). "
                "Essential for understanding game scene node hierarchies and resource structures."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "input_path": {
                        "type": "string",
                        "description": "Path to resource file or glob (e.g. 'extracted/**/*.scn')",
                    },
                    "output_dir": {
                        "type": "string",
                        "description": "Output directory (default: same directory)",
                    },
                    "direction": {
                        "type": "string",
                        "enum": ["bin_to_txt", "txt_to_bin"],
                        "default": "bin_to_txt",
                        "description": "Conversion direction",
                    },
                },
                "required": ["input_path"],
            },
        ),
        # ── Live Bridge Tools (require game running with MCPTest mod) ──
        types.Tool(
            name="bridge_ping",
            description=(
                "Check if the game is running and the MCPTest bridge mod is loaded. "
                "Returns mod version, current screen, run/combat status. Bridge on TCP 21337."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_screen",
            description=(
                "Get current game screen. Possible values:\n"
                "  COMBAT_PLAYER_TURN — your turn, play cards or end turn\n"
                "  COMBAT_ENEMY_TURN — wait for enemies to finish\n"
                "  MAP — choose a path node to travel to\n"
                "  EVENT — choose an event option\n"
                "  REWARD — claim rewards (gold/card/relic/potion) then proceed\n"
                "  CARD_SELECTION — pick/skip a card (reward, upgrade, remove)\n"
                "  SHOP — buy items or leave\n"
                "  REST_SITE — rest/smith/recall then proceed\n"
                "  TREASURE — pick relic then proceed\n"
                "  MAIN_MENU, CHARACTER_SELECT, GAME_OVER, LOADING, SETTINGS, TIMELINE\n"
                "Tip: Use bridge_get_full_state instead for screen + actions in one call."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_run_state",
            description=(
                "Get current run state: act, floor, ascension, seed, current room, "
                "players with HP/gold/deck size/relic count/max energy."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_combat_state",
            description=(
                "Get live combat state with full intent decomposition: round, player turn status, "
                "enemies (HP/block/powers/intent with damage/hits/total_damage), "
                "player hand (each card's playability, energy cost, valid targets), "
                "energy, draw/discard/exhaust pile sizes, powers."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_player_state",
            description=(
                "Get detailed player state: full deck with card types/rarities/costs/upgrades, "
                "all relics with rarities, potion slots, gold, HP."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_map_state",
            description=(
                "Get full map graph: all nodes with row/col, type (Monster/Elite/Boss/Rest/Shop/Event/Treasure), "
                "visited status, available (can travel to) status, child connections."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_available_actions",
            description=(
                "Get all currently legal actions for the current screen. Returns a list of action "
                "objects with action name, indices, labels, and types. Use this to know EXACTLY "
                "what you can do right now. Each action maps to a bridge tool:\n"
                "  - play_card → bridge_play_card(card_index, target_index)\n"
                "  - end_turn → bridge_end_turn\n"
                "  - travel → bridge_navigate_map(row, col)\n"
                "  - event_option → bridge_make_event_choice(choice_index)\n"
                "  - reward_select → bridge_reward_select(reward_index)\n"
                "  - reward_proceed → bridge_reward_proceed\n"
                "  - card_select → bridge_card_select(card_index)\n"
                "  - card_skip → bridge_card_skip\n"
                "  - card_confirm → bridge_card_confirm\n"
                "  - treasure_pick → bridge_treasure_pick(treasure_index)\n"
                "  - treasure_proceed → bridge_treasure_proceed\n"
                "  - shop_buy → bridge_shop_buy(item_type, index)\n"
                "  - shop_proceed → bridge_shop_proceed\n"
                "  - rest_option → bridge_rest_site_choice(choice)\n"
                "  - rest_proceed → bridge_rest_site_proceed\n"
                "Call this first when unsure what to do on the current screen."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_full_state",
            description=(
                "Get compact combined game state in ONE call: current screen, run info "
                "(act/floor/HP/gold/character), combat state (if in combat), and all available "
                "actions. This is the best starting point — call this to understand where the "
                "game is and what you can do. The available_actions list tells you exactly which "
                "bridge tools to call next."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_auto_proceed",
            description=(
                "Automatically advance past non-combat screens (rewards, card selections, "
                "treasure, rest sites, shops, events, loading). Handles screen transitions "
                "and waits for stability. Stops and returns when it reaches a screen that "
                "needs a decision: combat (play cards), map (choose path), or optionally "
                "reward/card selection screens. Use this when you don't care about intermediate "
                "screens and just want to get to the next decision point."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "skip_cards": {
                        "type": "boolean",
                        "default": True,
                        "description": "Auto-skip card selection screens (set false to stop and choose)",
                    },
                    "skip_rewards": {
                        "type": "boolean",
                        "default": False,
                        "description": "Auto-skip reward screens (set true to skip all rewards)",
                    },
                    "timeout_seconds": {
                        "type": "number",
                        "default": 15,
                        "description": "Max seconds to wait",
                    },
                },
            },
        ),
        types.Tool(
            name="bridge_act_and_wait",
            description=(
                "Execute any action, wait for the game to settle, then return the new full state. "
                "This is the RECOMMENDED way to interact with the game — it combines action + wait + "
                "state read in one call so you always know where you are after acting.\n"
                "Actions: play_card, end_turn, use_potion, discard_potion, event_option, "
                "reward_select, reward_proceed, card_select, card_skip, card_confirm, "
                "treasure_pick, treasure_proceed, shop_buy, shop_proceed, rest_option, "
                "rest_proceed, map_travel, proceed.\n"
                "Returns: action_result + full game state (screen, player, combat, available_actions)."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "action": {
                        "type": "string",
                        "description": "Action name (e.g. play_card, end_turn, reward_select, card_skip)",
                    },
                    "card_index": {"type": "integer", "description": "For play_card / card_select"},
                    "target_index": {"type": "integer", "description": "For play_card / use_potion (enemy index)"},
                    "choice_index": {"type": "integer", "description": "For event_option"},
                    "reward_index": {"type": "integer", "description": "For reward_select"},
                    "treasure_index": {"type": "integer", "description": "For treasure_pick"},
                    "potion_index": {"type": "integer", "description": "For use_potion / discard_potion"},
                    "item_type": {"type": "string", "description": "For shop_buy: card/relic/potion/remove"},
                    "index": {"type": "integer", "description": "Generic index (shop items)"},
                    "choice": {"type": "string", "description": "For rest_option: rest/smith/recall"},
                    "row": {"type": "integer", "description": "For map_travel"},
                    "col": {"type": "integer", "description": "For map_travel"},
                    "confirm": {"type": "boolean", "description": "Auto-confirm after card_select"},
                    "settle_timeout": {
                        "type": "number",
                        "default": 5.0,
                        "description": "Max seconds to wait for screen to stabilize after action",
                    },
                },
                "required": ["action"],
            },
        ),
        types.Tool(
            name="bridge_start_run",
            description=(
                "Start a new singleplayer run. Characters: Ironclad, Silent, Regent, Necrobinder, Defect. "
                "Supports deterministic seeds, optional modifiers/act lists, and fixture commands "
                "for rapid test setup immediately after the run starts."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "character": {"type": "string", "default": "Ironclad"},
                    "ascension": {"type": "integer", "default": 0},
                    "seed": {"type": "string", "description": "Deterministic run seed"},
                    "fixture": {
                        "type": "object",
                        "description": "Optional bridge fixture payload for deterministic setup",
                    },
                    "modifiers": {"type": "array", "items": {"type": "string"}},
                    "acts": {"type": "array", "items": {"type": "string"}},
                    "relics": {"type": "array", "items": {"type": "string"}},
                    "cards": {"type": "array", "items": {"type": "string"}},
                    "potions": {"type": "array", "items": {"type": "string"}},
                    "powers": {"type": "array", "items": {"type": "object"}},
                    "gold": {"type": "integer"},
                    "hp": {"type": "integer"},
                    "energy": {"type": "integer"},
                    "draw_cards": {"type": "integer"},
                    "fight": {"type": "string"},
                    "event": {"type": "string"},
                    "godmode": {"type": "boolean", "default": False},
                    "fixture_commands": {"type": "array", "items": {"type": "string"}},
                },
            },
        ),
        types.Tool(
            name="bridge_play_card",
            description=(
                "Play a card from the player's hand in combat. Specify card_index (0-based position in hand) "
                "and target_index (0-based enemy index, required for AnyEnemy cards like Strike). "
                "Use bridge_get_combat_state first to see hand contents and valid targets."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "card_index": {"type": "integer", "description": "Index of card in hand (0-based)"},
                    "target_index": {"type": "integer", "default": -1, "description": "Target enemy index (for targeted cards)"},
                },
                "required": ["card_index"],
            },
        ),
        types.Tool(
            name="bridge_end_turn",
            description="End the current player turn in combat.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_console",
            description=(
                "Execute a dev console command in the running game. "
                "Examples: 'gold 999', 'godmode', 'relic add ANCHOR', 'card BASH', "
                "'fight AXEBOTS_NORMAL', 'heal 999', 'win', 'kill', "
                "'potion BLOCK_POTION', 'power STRENGTH_POWER 5 0', 'draw 3', "
                "'unlock all', 'event ABYSSAL_BATHS'. "
                "Use get_console_commands for the full list."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "command": {"type": "string", "description": "Console command string"},
                },
                "required": ["command"],
            },
        ),
        # ── New Generators ──
        types.Tool(
            name="generate_event",
            description=(
                "Generate a custom event class with a choice tree. Events are narrative encounters "
                "with player choices (accept/refuse/leave). Generates EventModel subclass with "
                "EventOption yields and choice handler methods."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "Event class name (PascalCase, e.g. 'MysteriousAltar')"},
                    "is_shared": {"type": "boolean", "default": False, "description": "All players see same event (multiplayer)"},
                    "choices": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "label": {"type": "string", "description": "Choice text shown to player"},
                                "method_name": {"type": "string", "description": "Handler method name"},
                                "effect_description": {"type": "string", "description": "What this choice does"},
                            },
                            "required": ["label", "method_name"],
                        },
                        "description": "List of event choices",
                    },
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_ancient",
            description=(
                "Generate a BaseLib CustomAncientModel scaffold with option pools and localization."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string"},
                    "option_relics": {"type": "array", "items": {"type": "string"}},
                    "min_act_number": {"type": "integer", "default": 2},
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_orb",
            description=(
                "Generate a custom orb class (Defect character mechanic). Orbs sit in slots and have two effects: "
                "Passive triggers automatically at end of each turn, Evoke triggers when pushed out by a new orb "
                "or manually evoked. Values scale with Focus via ModifyOrbValue(). "
                "See get_modding_guide topic 'orbs' for patterns and Focus interaction."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "Orb class name (PascalCase, e.g. 'PlasmaOrb')"},
                    "passive_amount": {"type": "integer", "default": 3},
                    "evoke_amount": {"type": "integer", "default": 9},
                    "passive_description": {"type": "string"},
                    "evoke_description": {"type": "string"},
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_enchantment",
            description=(
                "Generate a custom enchantment class. Enchantments are card-local modifications that attach "
                "to specific card instances and trigger via the same hook system as powers/relics. "
                "Use EnchantedCard property to reference the attached card. Unlike powers (player-wide) "
                "or upgrades (permanent), enchantments are instance-specific and can be added/removed. "
                "See get_modding_guide topic 'enchantments' for lifecycle and hook details."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string"},
                    "trigger_hook": {"type": "string", "description": "Primary hook (e.g. 'ModifyDamageAdditive', 'AfterCardPlayed')"},
                    "description": {"type": "string"},
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_modifier",
            description=(
                "Generate a custom run modifier (Good or Bad) for the custom run screen. "
                "Includes ModifierModel subclass, registration Harmony patch (to add to ModelDb), "
                "and LocManager localization patch. Modifiers alter gameplay mechanics like "
                "rewards, card pools, relic acquisition, and run setup."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "Modifier class name (PascalCase, e.g. 'Famine', 'VintagePlus')"},
                    "modifier_type": {"type": "string", "enum": ["Good", "Bad"], "default": "Bad"},
                    "description": {"type": "string", "description": "What the modifier does (shown to player)"},
                    "hook": {
                        "type": "string",
                        "description": "Primary lifecycle hook",
                        "enum": [
                            "TryModifyRewardsLate", "AfterRunCreated",
                            "ModifyCardRewardCreationOptions", "ModifyMerchantCardPool",
                            "GenerateNeowOption",
                        ],
                    },
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_create_visuals_patch",
            description="Generate the CreateVisuals Harmony patch required for static-image custom enemies.",
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                },
                "required": ["mod_namespace"],
            },
        ),
        types.Tool(
            name="generate_act_encounter_patch",
            description="Generate a patch that injects a custom encounter into an act's encounter pool.",
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string"},
                    "act_class": {"type": "string"},
                    "encounter_class": {"type": "string"},
                },
                "required": ["mod_namespace", "class_name", "act_class", "encounter_class"],
            },
        ),
        types.Tool(
            name="generate_game_action",
            description=(
                "Generate a custom GameAction class for queuing multi-step combat effects on the game's "
                "action queue. Use GameAction (instead of direct await) when you need effects to interleave "
                "with other queued actions, execute between turns, or sync over multiplayer. "
                "Enqueue via RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(). "
                "See get_modding_guide topic 'game_actions' for when to use vs. direct effects."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string"},
                    "description": {"type": "string"},
                    "parameters": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "name": {"type": "string"},
                                "type": {"type": "string"},
                            },
                            "required": ["name", "type"],
                        },
                    },
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        # ── Mod Composition Tools ──
        types.Tool(
            name="generate_mechanic",
            description=(
                "Generate a complete cross-cutting keyword mechanic spanning multiple entity types: "
                "a tracking PowerModel (holds stacks, implements the effect), a sample card that applies it, "
                "a sample relic that rewards/synergizes with it, and all localization entries. "
                "Use this for new mechanics like Poison, Mantra, or Vulnerable — concepts that need "
                "a power + cards + relics working together. For single entities, use the individual generators instead. "
                "See get_modding_guide topic 'mechanics' for design patterns (threshold, tick-down, modifier)."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "mod_name": {"type": "string", "description": "Mod folder name"},
                    "keyword_name": {"type": "string", "description": "Mechanic/keyword name (e.g. 'Resonance', 'Fury')"},
                    "keyword_description": {"type": "string", "description": "What the mechanic does"},
                    "sample_card_name": {"type": "string", "description": "Name for the sample card (default: KeywordStrike)"},
                    "sample_relic_name": {"type": "string", "description": "Name for the sample relic (default: KeywordTalisman)"},
                },
                "required": ["mod_namespace", "mod_name", "keyword_name"],
            },
        ),
        types.Tool(
            name="validate_mod",
            description=(
                "Validate a mod project for common issues before building. Checks performed: "
                "(1) mod_manifest.json exists and is valid JSON, "
                "(2) .csproj has correct target framework and references, "
                "(3) at least one class has [ModInitializer] attribute, "
                "(4) localization files exist for all generated entities, "
                "(5) Harmony patches target valid methods with correct signatures, "
                "(6) async methods properly await and don't fire-and-forget. "
                "Returns a list of warnings and errors with file paths and line numbers."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "project_dir": {"type": "string", "description": "Path to mod project directory"},
                },
                "required": ["project_dir"],
            },
        ),
        types.Tool(
            name="generate_custom_tooltip",
            description=(
                "Generate a custom keyword tooltip that appears on hover in card/relic descriptions. "
                "Creates a Harmony patch that registers the keyword with HoverTipManager."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "tag_name": {"type": "string", "description": "Rich text tag name (e.g. 'fury' for [fury]Fury[/fury])"},
                    "title": {"type": "string", "description": "Keyword display name"},
                    "tooltip_description": {"type": "string", "description": "Hover tooltip text"},
                },
                "required": ["mod_namespace", "tag_name", "title", "tooltip_description"],
            },
        ),
        types.Tool(
            name="generate_save_data",
            description=(
                "Generate a save data class for persisting mod state across sessions. "
                "Data is stored as JSON at %APPDATA%/.sts2mods/{mod_id}/save_data.json."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "mod_id": {"type": "string", "description": "Mod identifier"},
                    "class_name": {"type": "string", "default": "ModSaveData"},
                    "fields": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "name": {"type": "string"},
                                "type": {"type": "string", "description": "C# type: int, string, bool, double, List<string>"},
                                "default": {"type": "string", "description": "Default value expression"},
                            },
                            "required": ["name", "type", "default"],
                        },
                    },
                },
                "required": ["mod_namespace", "mod_id"],
            },
        ),
        types.Tool(
            name="generate_test_scenario",
            description=(
                "Generate a console command sequence for testing a specific game state. "
                "Outputs commands to add relics, cards, gold, powers, start fights, etc."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "scenario_name": {"type": "string"},
                    "relics": {"type": "array", "items": {"type": "string"}, "description": "Relics to add"},
                    "cards": {"type": "array", "items": {"type": "string"}, "description": "Cards to add to hand"},
                    "gold": {"type": "integer", "description": "Gold to give"},
                    "hp": {"type": "integer", "description": "HP to heal"},
                    "powers": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "name": {"type": "string"},
                                "stacks": {"type": "integer", "default": 1},
                                "target": {"type": "integer", "default": 0},
                            },
                            "required": ["name"],
                        },
                    },
                    "fight": {"type": "string", "description": "Encounter to start"},
                    "event": {"type": "string", "description": "Event to trigger"},
                    "godmode": {"type": "boolean", "default": False},
                },
                "required": ["scenario_name"],
            },
        ),
        types.Tool(
            name="generate_vfx_scene",
            description=(
                "Generate a Godot .tscn scene file with GPUParticles2D for combat visual effects. "
                "Creates a particle system scene to pack into your mod's PCK and load at runtime via "
                "GD.Load<PackedScene>(\"res://YourMod/vfx/name.tscn\"). Configure particle count, "
                "lifetime, one_shot (burst vs. loop), and explosiveness (spread vs. instant). "
                "See get_modding_guide topic 'vfx_scenes' for loading patterns and common VFX recipes."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "node_name": {"type": "string", "description": "Scene root node name"},
                    "particle_count": {"type": "integer", "default": 30},
                    "lifetime": {"type": "number", "default": 0.5},
                    "one_shot": {"type": "boolean", "default": True},
                    "explosiveness": {"type": "number", "default": 0.8},
                },
                "required": ["node_name"],
            },
        ),
        # ── Code Intelligence Tools ──
        types.Tool(
            name="suggest_patches",
            description=(
                "Given a desired behavior change in natural language (e.g. 'make all attacks cost 1 less energy', "
                "'double poison damage', 'add a card to the reward screen'), analyze decompiled game source "
                "and suggest which methods to Harmony patch. Returns target class, method name, patch type "
                "(Prefix/Postfix/Transpiler), rationale, and a code sketch. Works best for specific, "
                "concrete behavior changes. For broad changes, break them into specific sub-behaviors first."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "desired_behavior": {"type": "string", "description": "What you want to change (natural language)"},
                    "max_suggestions": {"type": "integer", "default": 10},
                },
                "required": ["desired_behavior"],
            },
        ),
        types.Tool(
            name="analyze_method_callers",
            description=(
                "Show the call graph for a game method: who calls it, what it calls, "
                "and which classes override it. Essential for understanding patch side effects."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "class_name": {"type": "string", "description": "Class containing the method"},
                    "method_name": {"type": "string", "description": "Method to analyze"},
                    "max_results": {"type": "integer", "default": 30},
                },
                "required": ["class_name", "method_name"],
            },
        ),
        types.Tool(
            name="get_entity_relationships",
            description=(
                "Show what other entities a game entity interacts with: powers it applies, "
                "cards it references, commands it uses, hooks it implements. "
                "Map the dependency graph for any card, relic, power, monster, etc."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "entity_name": {"type": "string", "description": "Entity class name (e.g. 'Bash', 'StrengthPower', 'JawWorm')"},
                },
                "required": ["entity_name"],
            },
        ),
        types.Tool(
            name="search_hooks_by_signature",
            description=(
                "Search game hooks by parameter type. Answer questions like "
                "'What hooks give me access to CombatState?' or 'Which hooks receive a CardModel?'"
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "param_type": {"type": "string", "description": "Parameter type to search for (e.g. 'CombatState', 'CardModel', 'DamageResult')"},
                },
                "required": ["param_type"],
            },
        ),
        types.Tool(
            name="get_hook_signature",
            description="Return a hook's full signature plus a ready-to-paste override stub for generator workflows.",
            inputSchema={
                "type": "object",
                "properties": {
                    "hook_name": {"type": "string"},
                },
                "required": ["hook_name"],
            },
        ),
        types.Tool(
            name="suggest_hooks",
            description=(
                "Given a modding intent in natural language (e.g. 'make potions heal more', "
                "'add extra card draw', 'prevent death'), recommend which game hooks to override. "
                "Returns hook names, signatures, ready-to-paste override stubs, and example classes. "
                "This is the recommended starting point for any mod that interacts with game events."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "intent": {
                        "type": "string",
                        "description": "What you want your mod to do (natural language)",
                    },
                    "max_suggestions": {
                        "type": "integer",
                        "description": "Max hooks to suggest (default 10)",
                        "default": 10,
                    },
                },
                "required": ["intent"],
            },
        ),
        types.Tool(
            name="analyze_build_output",
            description="Parse dotnet build stdout/stderr into structured compiler errors and warnings.",
            inputSchema={
                "type": "object",
                "properties": {
                    "stdout": {"type": "string"},
                    "stderr": {"type": "string"},
                },
            },
        ),
        # ── Game Update Resilience ──
        types.Tool(
            name="diff_game_versions",
            description=(
                "Compare two decompiled source directories to find API changes after a game update. "
                "Shows added/removed files, changed hooks, changed public methods."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "old_decompiled_dir": {"type": "string", "description": "Path to old decompiled source"},
                    "new_decompiled_dir": {"type": "string", "description": "Path to new decompiled source"},
                },
                "required": ["old_decompiled_dir", "new_decompiled_dir"],
            },
        ),
        types.Tool(
            name="check_mod_compatibility",
            description=(
                "Check if a mod's code references any APIs that changed in the latest game version. "
                "Verifies Harmony patch targets, base classes, ModelDb references, and hook signatures."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "project_dir": {"type": "string", "description": "Path to mod project directory"},
                },
                "required": ["project_dir"],
            },
        ),
        types.Tool(
            name="list_game_vfx",
            description=(
                "List VFX-related classes, particle systems, and animation references in the game. "
                "Use to find existing VFX to reuse in your mod."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "query": {"type": "string", "description": "Optional filter query", "default": ""},
                },
            },
        ),
        # ── Extended Bridge Tools ──
        types.Tool(
            name="bridge_use_potion",
            description=(
                "Use a potion from the player's potion slots. Specify potion_index (0-based) "
                "and target_index for targeted potions. Use bridge_get_player_state to see potions."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "potion_index": {"type": "integer", "description": "Potion slot index (0-based)"},
                    "target_index": {"type": "integer", "default": -1, "description": "Target enemy index for targeted potions"},
                },
                "required": ["potion_index"],
            },
        ),
        types.Tool(
            name="bridge_make_event_choice",
            description=(
                "Select a choice in the current event. Requires being on the EVENT screen. "
                "choice_index is 0-based. Use bridge_get_screen to verify you're in an event."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "choice_index": {"type": "integer", "description": "Event choice index (0-based)"},
                },
                "required": ["choice_index"],
            },
        ),
        types.Tool(
            name="bridge_navigate_map",
            description=(
                "Travel to a map node by row and column. Requires being on the MAP screen. "
                "Use bridge_get_map_state to see available nodes."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "row": {"type": "integer", "description": "Map node row"},
                    "col": {"type": "integer", "description": "Map node column"},
                },
                "required": ["row", "col"],
            },
        ),
        types.Tool(
            name="bridge_rest_site_choice",
            description=(
                "Make a choice at a rest site. Options: 'rest' (heal), 'smith' (upgrade card), 'recall'."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "choice": {"type": "string", "enum": ["rest", "smith", "recall"], "description": "Rest site action"},
                },
                "required": ["choice"],
            },
        ),
        types.Tool(
            name="bridge_shop_action",
            description=(
                "Perform a shop action: buy a card, relic, or potion by index, or remove a card."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "action": {"type": "string", "enum": ["buy_card", "buy_relic", "buy_potion", "remove_card"]},
                    "index": {"type": "integer", "default": 0, "description": "Item index (0-based)"},
                },
                "required": ["action"],
            },
        ),
        # ── Screen Interaction Tools ──
        types.Tool(
            name="bridge_execute_action",
            description=(
                "Unified action dispatcher — execute any screen-appropriate action by name. "
                "Actions: travel/map_travel, event_option, event_proceed, reward_select, reward_proceed, "
                "reward_skip, shop_buy, shop_proceed, rest_option, rest_proceed, treasure_pick, "
                "treasure_proceed, card_select, card_confirm, card_skip, discard_potion, proceed. "
                "Pass action-specific params (choice_index, reward_index, card_index, etc.)."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "action": {
                        "type": "string",
                        "description": "Action name (e.g. reward_select, card_select, proceed)",
                    },
                    "choice_index": {"type": "integer", "description": "For event_option"},
                    "reward_index": {"type": "integer", "description": "For reward_select"},
                    "card_index": {"type": "integer", "description": "For card_select (single card)"},
                    "card_indices": {
                        "type": "array",
                        "items": {"type": "integer"},
                        "description": "For card_select (multiple cards)",
                    },
                    "treasure_index": {"type": "integer", "description": "For treasure_pick"},
                    "potion_index": {"type": "integer", "description": "For discard_potion"},
                    "item_type": {"type": "string", "description": "For shop_buy: card/relic/potion/remove"},
                    "index": {"type": "integer", "description": "Generic index for shop items"},
                    "choice": {"type": "string", "description": "For rest_option: rest/smith/recall"},
                    "row": {"type": "integer", "description": "For map_travel"},
                    "col": {"type": "integer", "description": "For map_travel"},
                    "confirm": {"type": "boolean", "description": "Auto-confirm after card_select"},
                },
                "required": ["action"],
            },
        ),
        types.Tool(
            name="bridge_reward_select",
            description=(
                "Claim a reward from the reward screen. Specify the reward_index (0-based) "
                "from the available rewards shown by bridge_get_available_actions. "
                "Rewards include gold, cards, relics, and potions."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "reward_index": {"type": "integer", "description": "Reward index (0-based)"},
                },
                "required": ["reward_index"],
            },
        ),
        types.Tool(
            name="bridge_reward_skip",
            description="Skip the current reward (e.g. skip card reward selection).",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_reward_proceed",
            description="Proceed from the reward screen after claiming desired rewards.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_card_select",
            description=(
                "Select one or more cards on a card selection screen (card reward picks, "
                "upgrades at rest/events, card removal at shops/events, scry, etc.). "
                "Use card_index for a single card or card_indices for multiple. "
                "Set confirm=true to auto-confirm after selection."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "card_index": {"type": "integer", "description": "Single card index (0-based)"},
                    "card_indices": {
                        "type": "array",
                        "items": {"type": "integer"},
                        "description": "Multiple card indices (0-based)",
                    },
                    "confirm": {
                        "type": "boolean",
                        "default": False,
                        "description": "Auto-confirm selection after choosing",
                    },
                },
            },
        ),
        types.Tool(
            name="bridge_card_skip",
            description="Skip card selection (decline to pick a card reward, cancel selection, etc.).",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_card_confirm",
            description="Confirm the current card selection (after selecting cards with bridge_card_select).",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_treasure_pick",
            description=(
                "Pick a treasure/relic from a treasure chest. Specify treasure_index (0-based) "
                "from the available items shown by bridge_get_available_actions."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "treasure_index": {"type": "integer", "default": 0, "description": "Treasure index (0-based)"},
                },
            },
        ),
        types.Tool(
            name="bridge_treasure_proceed",
            description="Proceed from the treasure screen after picking (or skipping) treasure.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_proceed",
            description=(
                "Generic proceed/advance — works on any screen. Attempts to click proceed, "
                "continue, leave, or done buttons on the current screen. Use this when you need "
                "to advance past a screen and no specific action tool applies."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_discard_potion",
            description=(
                "Discard a potion from the player's potion slots (e.g. to make room for a new one). "
                "Specify potion_index (0-based slot position)."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "potion_index": {"type": "integer", "description": "Potion slot index (0-based)"},
                },
                "required": ["potion_index"],
            },
        ),
        types.Tool(
            name="bridge_shop_buy",
            description=(
                "Buy a specific item from the shop. Specify item_type (card/relic/potion/remove) "
                "and index (0-based position within that category)."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "item_type": {
                        "type": "string",
                        "enum": ["card", "relic", "potion", "remove"],
                        "description": "Type of item to buy",
                    },
                    "index": {"type": "integer", "default": 0, "description": "Item index within category (0-based)"},
                },
                "required": ["item_type"],
            },
        ),
        types.Tool(
            name="bridge_shop_proceed",
            description="Leave the shop and return to the map.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_rest_site_proceed",
            description="Leave the rest site after performing an action (rest/smith/recall).",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_card_piles",
            description=(
                "Get detailed contents of all card piles in combat: hand, draw pile, "
                "discard pile, and exhaust pile. Each card includes name, type, cost, upgraded status."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_manipulate_state",
            description=(
                "Apply state changes for testing. Set HP, gold, energy, draw cards, add relics/cards/powers, "
                "start fights, enable godmode. A test harness for rapid mod iteration."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "hp": {"type": "integer", "description": "Heal this amount"},
                    "gold": {"type": "integer", "description": "Add this much gold"},
                    "energy": {"type": "integer", "description": "Add energy charges"},
                    "draw_cards": {"type": "integer", "description": "Draw N cards"},
                    "add_relic": {"type": "string", "description": "Relic ID to add"},
                    "add_card": {"type": "string", "description": "Card ID to add to hand"},
                    "add_power": {
                        "type": "object",
                        "properties": {
                            "name": {"type": "string"},
                            "stacks": {"type": "integer", "default": 1},
                            "target": {"type": "integer", "default": 0},
                        },
                    },
                    "fight": {"type": "string", "description": "Start encounter by ID"},
                    "godmode": {"type": "boolean", "description": "Toggle invincibility"},
                },
            },
        ),
        # ── Live Coding & Iteration Tools ──
        types.Tool(
            name="bridge_hot_swap_patches",
            description=(
                "Hot-swap Harmony patches from a new DLL without restarting the game. "
                "Unpatches all existing patches and re-applies from the specified assembly. "
                "Enables rapid iteration on patch-based mods."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "dll_path": {
                        "type": "string",
                        "description": "Absolute path to the new DLL containing Harmony patches",
                    },
                },
                "required": ["dll_path"],
            },
        ),
        types.Tool(
            name="bridge_set_game_speed",
            description=(
                "Set the game speed multiplier for faster testing. "
                "1.0 = normal, 2.0 = double speed, 10.0 = 10x speed, 0.5 = half speed. "
                "Range: 0.1 to 20.0. Use high values to quickly test through combat sequences."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "speed": {
                        "type": "number",
                        "description": "Speed multiplier (0.1 to 20.0, default 1.0)",
                        "default": 1.0,
                    },
                },
                "required": ["speed"],
            },
        ),
        types.Tool(
            name="bridge_restart_run",
            description=(
                "Restart a run using the same parameters as the last bridge_start_run call. "
                "Saves time by not re-specifying character, ascension, seed, fixtures, etc."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_state_diff",
            description=(
                "Get changes in game state since the last call. First call captures a baseline. "
                "Subsequent calls return only fields that changed (HP, block, energy, hand, enemies, etc.). "
                "Useful for verifying the effect of actions without comparing full state dumps."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_exceptions",
            description=(
                "Get recent unhandled exceptions captured by the bridge mod. "
                "Catches mod exceptions in real-time including type, message, stack trace, and source."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "max_count": {
                        "type": "integer",
                        "description": "Maximum exceptions to return (default 20)",
                        "default": 20,
                    },
                    "since_id": {
                        "type": "integer",
                        "description": "Only return exceptions after this ID (for polling)",
                        "default": 0,
                    },
                },
            },
        ),
        types.Tool(
            name="bridge_get_events",
            description=(
                "Get game events since a given event ID. Events include card plays, turn ends, "
                "run starts, hot swaps, screenshots, and other tracked actions. "
                "Use since_id for cursor-based polling."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "since_id": {
                        "type": "integer",
                        "description": "Return events after this ID (default 0 = all)",
                        "default": 0,
                    },
                    "max_count": {
                        "type": "integer",
                        "description": "Maximum events to return (default 100)",
                        "default": 100,
                    },
                },
            },
        ),
        types.Tool(
            name="bridge_capture_screenshot",
            description=(
                "Capture a screenshot of the game window. Saves as PNG. "
                "Useful for visual verification of UI mods, VFX, and custom scenes."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "save_path": {
                        "type": "string",
                        "description": "Path to save the PNG (default: auto-generated in AppData/MCPTest/screenshots/)",
                    },
                },
            },
        ),
        types.Tool(
            name="bridge_save_snapshot",
            description=(
                "Save a named snapshot of the current game state. "
                "Can be restored later with bridge_restore_snapshot for A/B testing."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "Snapshot name (default: 'default')",
                        "default": "default",
                    },
                },
            },
        ),
        types.Tool(
            name="bridge_restore_snapshot",
            description=(
                "Restore a previously saved state snapshot by name. "
                "Re-applies HP, gold, energy via console commands."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "name": {
                        "type": "string",
                        "description": "Snapshot name to restore (default: 'default')",
                        "default": "default",
                    },
                },
            },
        ),
        # ── Breakpoints & Stepping Tools ──
        types.Tool(
            name="bridge_debug_pause",
            description=(
                "Pause the game's action processing. The game continues rendering but no more actions "
                "(card plays, damage, powers, etc.) execute until resumed. Use to inspect state mid-combat."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_debug_resume",
            description="Resume from a breakpoint or pause. Continues normal game execution.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_debug_step",
            description=(
                "Step: resume execution then pause again at the next opportunity. "
                "In 'action' mode, pauses after the next game action completes. "
                "In 'turn' mode, pauses at the start of the next player turn. "
                "After stepping, use bridge_debug_get_context to inspect the game state."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mode": {
                        "type": "string",
                        "description": "Step granularity: 'action' (pause after each action) or 'turn' (pause at each player turn)",
                        "default": "action",
                        "enum": ["action", "turn"],
                    },
                },
            },
        ),
        types.Tool(
            name="bridge_debug_set_breakpoint",
            description=(
                "Set a breakpoint that pauses execution when a condition is met. "
                "Action breakpoints pause when a specific action type executes (e.g., PlayCardAction, DamageAction). "
                "Hook breakpoints pause when a specific game hook fires (e.g., BeforeCardPlayed, BeforeDamageReceived). "
                "Optional conditions can further filter: 'hp<10', 'energy==0', 'round>=3'."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "type": {
                        "type": "string",
                        "description": "Breakpoint type: 'action' or 'hook'",
                        "default": "action",
                        "enum": ["action", "hook"],
                    },
                    "target": {
                        "type": "string",
                        "description": (
                            "What to break on. For action: class name (e.g., 'PlayCardAction', 'DamageAction'). "
                            "For hook: hook name (e.g., 'BeforeCardPlayed', 'BeforeDamageReceived', 'BeforeDeath', "
                            "'BeforePlayPhaseStart', 'AfterTurnEnd', 'BeforeRoomEntered')."
                        ),
                    },
                    "condition": {
                        "type": "string",
                        "description": "Optional condition: 'hp<10', 'energy==0', 'block>5', 'round>=3', 'gold>500'",
                    },
                },
                "required": ["target"],
            },
        ),
        types.Tool(
            name="bridge_debug_remove_breakpoint",
            description="Remove a breakpoint by its ID.",
            inputSchema={
                "type": "object",
                "properties": {
                    "id": {"type": "integer", "description": "Breakpoint ID to remove"},
                },
                "required": ["id"],
            },
        ),
        types.Tool(
            name="bridge_debug_list_breakpoints",
            description="List all breakpoints with their IDs, types, targets, conditions, hit counts, and current pause/step state.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_debug_clear_breakpoints",
            description="Remove all breakpoints and disable step mode.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_debug_get_context",
            description=(
                "Get the current breakpoint/pause context. When paused, returns: why execution stopped, "
                "which breakpoint or step triggered it, the current action type, and a full game state snapshot "
                "(HP, block, energy, hand, powers, enemies, floor, act, room)."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        # ── Debugging & Logging Tools ──
        types.Tool(
            name="bridge_get_game_log",
            description=(
                "Get captured game log messages from the game's own logging system. "
                "Covers all game subsystems (Actions, Network, GameSync, VisualSync, Generic). "
                "Different from bridge_get_log which reads the bridge mod's log file. "
                "Supports filtering by log level and message content, and cursor-based polling via since_id."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "max_count": {
                        "type": "integer",
                        "description": "Max entries to return (default 100, max 500)",
                        "default": 100,
                    },
                    "since_id": {
                        "type": "integer",
                        "description": "Only return entries after this ID (for polling). Default 0 = all.",
                        "default": 0,
                    },
                    "level": {
                        "type": "string",
                        "description": "Filter by log level: VeryDebug, Load, Debug, Info, Warn, Error",
                    },
                    "contains": {
                        "type": "string",
                        "description": "Filter by substring in message (case-insensitive)",
                    },
                },
            },
        ),
        types.Tool(
            name="bridge_set_log_level",
            description=(
                "Set game logging verbosity. Can set per-category levels (e.g., Actions→Debug to see "
                "every game action), the global fallback, or the capture buffer threshold. "
                "Log levels from most to least verbose: VeryDebug, Load, Debug, Info, Warn, Error. "
                "Log types: Generic, Network, Actions, GameSync, VisualSync. "
                "Lowering levels to Debug or VeryDebug enables verbose output for that subsystem."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "type": {
                        "type": "string",
                        "description": "Log category to set (Generic, Network, Actions, GameSync, VisualSync). Used with level.",
                    },
                    "level": {
                        "type": "string",
                        "description": "Level for the specified type (VeryDebug, Load, Debug, Info, Warn, Error)",
                    },
                    "global_level": {
                        "type": "string",
                        "description": "Set the global fallback level for all types not explicitly configured",
                    },
                    "capture_level": {
                        "type": "string",
                        "description": "Set minimum level captured into the ring buffer (affects bridge_get_game_log). Default: Info",
                    },
                },
            },
        ),
        types.Tool(
            name="bridge_get_log_levels",
            description=(
                "Get current log level settings for all categories, the global level, and the capture threshold. "
                "Also returns valid type and level names for reference."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_get_diagnostics",
            description=(
                "Get comprehensive diagnostics in a single call: current screen, run state (floor/act/room), "
                "combat state, active screen object shape, current event shape, and recent bridge log lines. "
                "Useful as a first step when investigating issues."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "log_lines": {
                        "type": "integer",
                        "description": "Number of recent log lines to include (default 40, max 200)",
                        "default": 40,
                    },
                },
            },
        ),
        types.Tool(
            name="bridge_clear_exceptions",
            description=(
                "Clear the exception ring buffer. Use before a test run to get a clean baseline, "
                "then check bridge_get_exceptions afterwards to see only new exceptions."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_clear_events",
            description=(
                "Clear the event ring buffer. Use before a test run to get a clean baseline, "
                "then check bridge_get_events afterwards to see only new events."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        # ── AutoSlay Tools (Built-in Automated Runner) ──
        types.Tool(
            name="bridge_autoslay_start",
            description=(
                "Start the game's built-in AutoSlay automated runner. "
                "AutoSlay plays through entire runs automatically — handling combat, events, shops, "
                "rest sites, rewards, and map navigation with built-in AI. "
                "Use for: smoke testing mods across many runs, crash/stability testing, "
                "regression testing with specific seeds, performance profiling. "
                "Complements manual bridge testing: AutoSlay = fire-and-forget full runs, "
                "bridge actions = precise step-by-step control."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "character": {
                        "type": "string",
                        "description": "Character to play (Ironclad, Silent, Defect, Necrobinder, Regent, etc.)",
                        "default": "Ironclad",
                    },
                    "seed": {
                        "type": "string",
                        "description": "Specific seed for deterministic runs. Omit for random seed.",
                    },
                    "runs": {
                        "type": "integer",
                        "description": "Number of runs to play (default 1). Each run plays to completion or failure.",
                        "default": 1,
                    },
                    "loop": {
                        "type": "boolean",
                        "description": "If true, run indefinitely until stopped with bridge_autoslay_stop.",
                        "default": False,
                    },
                },
            },
        ),
        types.Tool(
            name="bridge_autoslay_stop",
            description=(
                "Stop the currently running AutoSlay session. "
                "The current run will be cancelled and control returns to the main menu."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_autoslay_status",
            description=(
                "Get the current AutoSlay status. Returns whether it's running, "
                "runs completed, current floor/act/room, elapsed time, recent log entries, "
                "and any errors. Use to monitor progress of automated runs."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_autoslay_configure",
            description=(
                "Configure AutoSlay timeouts and behavior before starting runs. "
                "Settings persist until changed or the game restarts. "
                "Useful for adjusting for slow mods (increase timeouts) or speed testing (decrease)."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "run_timeout_seconds": {
                        "type": "integer",
                        "description": "Max seconds for an entire run (default ~1500 / 25min)",
                    },
                    "room_timeout_seconds": {
                        "type": "integer",
                        "description": "Max seconds per room (default ~120 / 2min)",
                    },
                    "screen_timeout_seconds": {
                        "type": "integer",
                        "description": "Max seconds per screen/overlay (default ~30)",
                    },
                    "polling_interval_ms": {
                        "type": "integer",
                        "description": "Polling interval in milliseconds (default ~100)",
                    },
                    "watchdog_timeout_seconds": {
                        "type": "integer",
                        "description": "Stall detection timeout in seconds (default ~30)",
                    },
                    "max_floor": {
                        "type": "integer",
                        "description": "Maximum floor to play to (default ~49)",
                    },
                },
            },
        ),
        # ── Navigation & Window Helpers ──
        types.Tool(
            name="bridge_navigate_to_combat",
            description=(
                "Automatically navigate from any screen to the first combat encounter. "
                "Handles Neow events, card selections, reward screens, map navigation, and other "
                "intermediate screens. Focuses the game window first. "
                "Useful for quickly getting to combat for testing mods."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "timeout": {"type": "integer", "default": 60, "description": "Max seconds to reach combat"},
                    "neow_choice_index": {"type": "integer", "default": 0, "description": "Which Neow option to pick"},
                },
            },
        ),
        types.Tool(
            name="bridge_focus_game",
            description=(
                "Bring the Slay the Spire 2 window to the foreground (Windows only). "
                "Call before bridge commands if scene transitions aren't executing — "
                "the game may not process transitions when its window is unfocused."
            ),
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="bridge_wait_for_screen",
            description=(
                "Wait until the game reaches a specific screen (case-insensitive substring match). "
                "Use after actions that trigger screen transitions (e.g. after navigate_map, wait for COMBAT). "
                "Tip: Use bridge_auto_proceed instead if you just want to get past intermediate screens."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "target_screen": {"type": "string", "description": "Screen name to wait for"},
                    "timeout": {"type": "integer", "default": 15, "description": "Max seconds to wait"},
                },
                "required": ["target_screen"],
            },
        ),
        # ── Test Runner Tools ──
        types.Tool(
            name="run_test_scenario",
            description=(
                "Run an automated test scenario against the live game (requires bridge mod running). "
                "Structure: {setup: {run params}, steps: [{action, assertions}], verify: {final checks}}. "
                "Step actions: play_card, end_turn, console, manipulate_state, navigate_map, use_potion, "
                "make_event_choice, wait_for_screen, wait_idle. "
                "Assertions check game state: hp, gold, energy, block, hand_size, enemy_N_hp, has_power_X, "
                "power_X (with operators: eq, gt, lt, gte, lte). "
                "See get_modding_guide topic 'testing' for full assertion syntax and example scenarios."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "scenario": {
                        "type": "object",
                        "description": "Test scenario with name, setup, and steps",
                        "properties": {
                            "name": {"type": "string"},
                            "setup": {
                                "type": "object",
                                "description": "Run start params (character, ascension, seed, relics, cards, etc.)",
                            },
                            "steps": {
                                "type": "array",
                                "items": {
                                    "type": "object",
                                    "properties": {
                                        "action": {"type": "string", "description": "Action: play_card, end_turn, console, manipulate_state, navigate_map, event_choice, rest_choice, wait, noop"},
                                        "params": {"type": "object"},
                                        "assert": {"type": "object", "description": "Assertions: {field: expected_value} or {field: {op: 'gt', value: N}}"},
                                        "wait_for_screen": {"type": "string"},
                                        "wait_idle": {"type": "boolean"},
                                        "delay": {"type": "number"},
                                        "stop_on_fail": {"type": "boolean", "default": True},
                                    },
                                },
                            },
                        },
                    },
                },
                "required": ["scenario"],
            },
        ),
        # ── File Watcher Tools ──
        types.Tool(
            name="watch_project",
            description=(
                "Start watching a mod project for file changes and auto-rebuild+deploy on save. "
                "Monitors .cs, .json, .tscn, .tres files. Debounces builds by 1.5s. "
                "Eliminates the manual build-deploy cycle during development."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "project_dir": {"type": "string", "description": "Path to the mod project directory"},
                    "mods_dir": {"type": "string", "description": "Game mods directory path"},
                    "mod_name": {"type": "string", "description": "Install name override"},
                    "configuration": {"type": "string", "default": "Debug"},
                },
                "required": ["project_dir", "mods_dir"],
            },
        ),
        types.Tool(
            name="stop_watching",
            description="Stop the active file watcher for auto-rebuild.",
            inputSchema={"type": "object", "properties": {}},
        ),
        types.Tool(
            name="watcher_status",
            description="Get the status of the active file watcher (running, build count, last result).",
            inputSchema={"type": "object", "properties": {}},
        ),
        # ── Analysis Tools ──
        types.Tool(
            name="reverse_hook_lookup",
            description=(
                "Find what hooks fire when an entity is used or triggered. "
                "Inverse of get_entity_relationships: shows hooks relevant to a card/relic/power/monster, "
                "which hooks the entity overrides, and which hooks accept its base class as a parameter."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "entity_name": {
                        "type": "string",
                        "description": "Entity class name (e.g., 'Bash', 'StrengthPower', 'JawWorm')",
                    },
                },
                "required": ["entity_name"],
            },
        ),
        # ── Project Workflow Tools ──
        types.Tool(
            name="package_mod",
            description=(
                "Package a built mod into a distributable zip archive. "
                "Includes DLL, manifest, PCK, and mod image."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "project_dir": {"type": "string", "description": "Path to the mod project"},
                    "output_path": {"type": "string", "description": "Output zip path (default: project_dir/mod_name.zip)"},
                },
                "required": ["project_dir"],
            },
        ),
        types.Tool(
            name="check_dependencies",
            description=(
                "Check mod project dependencies from .csproj. "
                "Lists NuGet packages, DLL references, and validates they exist on disk."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "project_dir": {"type": "string", "description": "Path to the mod project"},
                },
                "required": ["project_dir"],
            },
        ),
        types.Tool(
            name="discover_mod_projects",
            description=(
                "Discover all mod projects in a workspace directory. "
                "Finds directories with .csproj files and inspects their mod structure."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "workspace_dir": {"type": "string", "description": "Root directory to search"},
                },
                "required": ["workspace_dir"],
            },
        ),
        # ── Advanced Generators ──
        types.Tool(
            name="generate_net_message",
            description=(
                "Generate a multiplayer network message class implementing INetMessage and IPacketSerializable. "
                "Used for syncing mod state across players. Supports typed fields with auto-generated "
                "serialization. Common in multiplayer mods (chat, drawing sync, damage tracking)."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "Message class name (PascalCase)"},
                    "transfer_mode": {"type": "string", "enum": ["Reliable", "Unreliable", "ReliableOrdered"], "default": "Reliable"},
                    "should_broadcast": {"type": "boolean", "default": True},
                    "fields": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "name": {"type": "string"},
                                "type": {"type": "string", "enum": ["string", "int", "float", "bool", "decimal"]},
                            },
                            "required": ["name", "type"],
                        },
                        "description": "Fields to serialize/deserialize",
                    },
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_godot_ui",
            description=(
                "Generate a custom Godot UI panel built programmatically in C#. "
                "Creates a styled panel with configurable controls (labels, buttons, sliders, checkboxes). "
                "Used by mods that need custom in-game interfaces without .tscn files."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "UI class name (PascalCase)"},
                    "title": {"type": "string", "default": "My Panel"},
                    "base_type": {"type": "string", "enum": ["Control", "CanvasLayer", "Node2D"], "default": "Control"},
                    "controls": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "type": {"type": "string", "enum": ["Label", "Button", "CheckBox", "Slider"]},
                                "name": {"type": "string"},
                                "text": {"type": "string"},
                            },
                            "required": ["type", "name"],
                        },
                    },
                    "show_in_process": {"type": "boolean", "default": False, "description": "Add _Process for real-time updates"},
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_settings_panel",
            description=(
                "Generate a mod settings class with optional ModConfig integration (via reflection, no hard dependency). "
                "Falls back to JSON file config if ModConfig isn't installed. "
                "Supports bool, int, float, and string settings with persistence."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "default": "ModSettings"},
                    "mod_id": {"type": "string", "description": "Mod identifier for config file path"},
                    "properties": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "name": {"type": "string"},
                                "type": {"type": "string", "enum": ["bool", "int", "float", "string"]},
                                "default": {"type": "string"},
                            },
                            "required": ["name", "type", "default"],
                        },
                    },
                },
                "required": ["mod_namespace", "mod_id"],
            },
        ),
        types.Tool(
            name="generate_hover_tip",
            description=(
                "Generate a hover tooltip utility class for showing contextual information. "
                "Provides static methods to show/hide HoverTip at positions or attached to nodes."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "default": "ModHoverTips"},
                },
                "required": ["mod_namespace"],
            },
        ),
        types.Tool(
            name="generate_overlay",
            description=(
                "Generate a Godot Control node overlay that auto-injects into a game scene via Harmony patch. "
                "The overlay updates each frame via _Process() and cleans up automatically when the scene changes. "
                "Injection targets: NCombatRoom (combat), NMapRoom (map), NShopRoom (shops), NRestSiteRoom, NEventRoom. "
                "Use for debug displays, stat trackers, custom HUD elements, or mod-specific UI. "
                "See get_modding_guide topic 'overlays' for positioning and game state access."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "Overlay class name"},
                    "mod_id": {"type": "string", "default": "mymod"},
                    "overlay_description": {"type": "string", "default": "Custom overlay"},
                    "inject_target": {"type": "string", "default": "NCombatRoom", "description": "Game node to inject into"},
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_floating_panel",
            description=(
                "Generate a mouse-following info panel with BBCode rich text, fade animation, and hotkey toggle. "
                "Great for showing contextual info, card details, or debug data that follows the cursor. "
                "Uses RichTextLabel with BBCode for colors, bold, italics. "
                "See get_modding_guide topic 'ui_elements' for patterns."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "Panel class name"},
                    "mod_id": {"type": "string", "default": "mymod"},
                    "panel_title": {"type": "string", "default": "Info Panel"},
                    "initial_content": {"type": "string", "default": "Panel content here."},
                    "hotkey": {"type": "string", "default": "F7", "description": "Toggle key (Godot Key enum name)"},
                    "inject_target": {"type": "string", "default": "NCombatRoom"},
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_animated_bar",
            description=(
                "Generate an animated progress bar with smooth tweens, color gradients (green→red), "
                "flash-on-damage, and optional low-value pulse effect. "
                "Use for HP trackers, XP bars, charge meters, or any numeric display. "
                "See get_modding_guide topic 'ui_elements' for patterns."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "Bar class name"},
                    "mod_id": {"type": "string", "default": "mymod"},
                    "bar_label": {"type": "string", "default": "Health"},
                    "bar_width": {"type": "string", "default": "200"},
                    "bar_height": {"type": "string", "default": "20"},
                    "color_low": {"type": "string", "default": "0.9f, 0.2f, 0.15f", "description": "RGB when empty"},
                    "color_high": {"type": "string", "default": "0.2f, 0.85f, 0.3f", "description": "RGB when full"},
                    "pulse_enabled": {"type": "string", "default": "true"},
                    "inject_target": {"type": "string", "default": "NCombatRoom"},
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_scrollable_list",
            description=(
                "Generate a toggleable scrollable list panel that slides in from the right edge. "
                "Supports color-coded item rows with optional count badges. "
                "Use for deck trackers, log viewers, inventory lists, or any dynamic list display. "
                "See get_modding_guide topic 'ui_elements' for patterns."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "List class name"},
                    "mod_id": {"type": "string", "default": "mymod"},
                    "list_title": {"type": "string", "default": "Item List"},
                    "hotkey": {"type": "string", "default": "F9", "description": "Toggle key"},
                    "panel_width": {"type": "string", "default": "250"},
                    "inject_target": {"type": "string", "default": "NCombatRoom"},
                },
                "required": ["mod_namespace", "class_name"],
            },
        ),
        types.Tool(
            name="generate_transpiler_patch",
            description=(
                "Generate a Harmony IL Transpiler patch for modifying method bytecode. "
                "More powerful than prefix/postfix — can change specific IL instructions. "
                "Used for value modifications, conditional injection, and protocol changes."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "Patch class name"},
                    "target_type": {"type": "string", "description": "Class to patch (e.g. 'DamageCmd')"},
                    "target_method": {"type": "string", "description": "Method to patch (e.g. 'Attack')"},
                    "description": {"type": "string", "default": ""},
                    "search_opcode": {"type": "string", "default": "Callvirt"},
                    "search_method": {"type": "string", "default": ""},
                    "mod_id": {"type": "string", "default": "mymod"},
                },
                "required": ["mod_namespace", "class_name", "target_type", "target_method"],
            },
        ),
        types.Tool(
            name="generate_reflection_accessor",
            description=(
                "Generate a utility class with cached reflection accessors for private fields/properties. "
                "Uses Harmony's AccessTools for safe access. Essential for mods needing private game state."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "Accessor class name (e.g. 'CombatAccessor')"},
                    "target_type": {"type": "string", "description": "Game class to access (e.g. 'NMapDrawings')"},
                    "fields": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "name": {"type": "string", "description": "Field/property name"},
                                "type": {"type": "string", "description": "C# type"},
                                "is_property": {"type": "boolean", "default": False},
                            },
                            "required": ["name", "type"],
                        },
                    },
                },
                "required": ["mod_namespace", "class_name", "target_type"],
            },
        ),
        types.Tool(
            name="generate_custom_keyword",
            description=(
                "Generate a custom card keyword using BaseLib's [CustomEnum] attribute (requires BaseLib dependency). "
                "Creates a new CardKeyword enum value that can be added to cards via Keywords property "
                "and shown in tooltips. Use with generate_custom_tooltip to add a hover explanation. "
                "See get_modding_guide topic 'custom_keywords_and_piles'."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "keyword_name": {"type": "string", "description": "Keyword display name (e.g. 'Stitch', 'Woven')"},
                },
                "required": ["mod_namespace", "keyword_name"],
            },
        ),
        types.Tool(
            name="generate_custom_pile",
            description=(
                "Generate a custom card pile type using BaseLib's [CustomEnum] attribute (requires BaseLib dependency). "
                "Creates a new PileType enum value for routing cards to custom locations beyond the standard "
                "hand/draw/discard/exhaust piles. Use when your mechanic needs a separate card zone. "
                "See get_modding_guide topic 'custom_keywords_and_piles'."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "pile_name": {"type": "string", "description": "Pile name (e.g. 'Stitch', 'Void')"},
                },
                "required": ["mod_namespace", "pile_name"],
            },
        ),
        types.Tool(
            name="generate_spire_field",
            description=(
                "Generate a SpireField for attaching custom data to game model instances. "
                "Like a dictionary keyed by instance — attach ints, bools, or objects to any CardModel, "
                "Creature, etc. without modifying the class. Requires BaseLib."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "Container class name"},
                    "target_type": {"type": "string", "description": "Game type to attach to (e.g. 'CardModel')"},
                    "field_name": {"type": "string", "default": "Value"},
                    "field_type": {"type": "string", "default": "int"},
                    "default_value": {"type": "string", "default": "0"},
                },
                "required": ["mod_namespace", "class_name", "target_type"],
            },
        ),
        types.Tool(
            name="generate_dynamic_var",
            description=(
                "Generate a custom DynamicVar subclass for use in card/power/enchantment descriptions. "
                "DynamicVars are named numeric values referenced in localization strings as {var_name} that "
                "resolve at display time and update dynamically (e.g., scaling with Strength via ValueProp.Move). "
                "Use when built-in vars (MoveVar, BlockVar, MagicVar, UrMagicVar) aren't enough. "
                "See get_modding_guide topic 'dynamic_vars' for ValueProp options and upgrade integration."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "mod_namespace": {"type": "string"},
                    "class_name": {"type": "string", "description": "Var class name (e.g. 'FuryVar')"},
                    "var_name": {"type": "string", "description": "Display name in descriptions"},
                    "default_value": {"type": "integer", "default": 0},
                },
                "required": ["mod_namespace", "class_name", "var_name"],
            },
        ),
        # ── Image Generation & Processing ──
        types.Tool(
            name="generate_art",
            description=(
                "Generate game art using Google Gemini Nano Banana 2 and process it into "
                "all required size variants for the given asset type. Produces ready-to-use "
                "PNG files in the mod project's image directories. "
                "Requires GOOGLE_API_KEY env var."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "description": {
                        "type": "string",
                        "description": "Plain-English description of the desired art (e.g. 'A flaming sword with purple runes')",
                    },
                    "asset_type": {
                        "type": "string",
                        "enum": ["card", "card_fullscreen", "relic", "power", "character"],
                        "description": "Type of game asset — determines output sizes and variants",
                    },
                    "name": {
                        "type": "string",
                        "description": "Asset name used in file paths (e.g. 'flame_slash', 'iron_shell')",
                    },
                    "project_dir": {
                        "type": "string",
                        "description": "Absolute path to the mod project root directory",
                    },
                    "model": {
                        "type": "string",
                        "description": "Optional model override (default: gemini-3.1-flash-image-preview)",
                    },
                },
                "required": ["description", "asset_type", "name", "project_dir"],
            },
        ),
        types.Tool(
            name="process_art",
            description=(
                "Process an existing image file into game-ready variants for a given asset type. "
                "Handles background removal (for relics/powers), resizing, outline generation, "
                "and locked-state effects. Use this when you already have source art and just "
                "need the correctly-sized variants placed into the mod project."
            ),
            inputSchema={
                "type": "object",
                "properties": {
                    "image_path": {
                        "type": "string",
                        "description": "Absolute path to the source image file (PNG, JPG, etc.)",
                    },
                    "asset_type": {
                        "type": "string",
                        "enum": ["card", "card_fullscreen", "relic", "power", "character"],
                        "description": "Type of game asset — determines output sizes, variants, and effects",
                    },
                    "name": {
                        "type": "string",
                        "description": "Asset name used in file paths (e.g. 'flame_slash')",
                    },
                    "project_dir": {
                        "type": "string",
                        "description": "Absolute path to the mod project root directory",
                    },
                },
                "required": ["image_path", "asset_type", "name", "project_dir"],
            },
        ),
        types.Tool(
            name="list_art_profiles",
            description=(
                "List all supported asset types and their image variant specifications. "
                "Shows output sizes, file paths, background modes, and effects for each type."
            ),
            inputSchema={
                "type": "object",
                "properties": {},
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
        return [
            types.TextContent(
                type="text",
                text=json.dumps(
                    {
                        "success": False,
                        "error": f"{type(e).__name__}: {e}",
                        "tool": name,
                    },
                    indent=2,
                ),
            )
        ]


def _lookup_hook_signature(trigger_hook: str) -> dict | None:
    if not trigger_hook:
        return None

    signature = analyzer.get_hook_signature(trigger_hook)
    if signature.get("found"):
        return signature
    return None


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
        results = game_data.search_code_smart(
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

    elif name == "get_setup_status":
        return _get_setup_status(GAME_DIR, DECOMPILED_DIR)

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
        hook_signature = _lookup_hook_signature(args.get("trigger_hook", ""))
        return mod_gen.generate_relic(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            rarity=args.get("rarity", "Common"),
            pool=args.get("pool", "SharedRelicPool"),
            description=args.get("description", ""),
            flavor=args.get("flavor", ""),
            trigger_hook=args.get("trigger_hook", ""),
            use_baselib=args.get("use_baselib", True),
            hook_signature=hook_signature,
        )

    elif name == "generate_power":
        hook_signature = _lookup_hook_signature(args.get("trigger_hook", ""))
        return mod_gen.generate_power(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            power_type=args.get("power_type", "Buff"),
            stack_type=args.get("stack_type", "Counter"),
            description=args.get("description", ""),
            trigger_hook=args.get("trigger_hook", ""),
            use_baselib=args.get("use_baselib", True),
            mod_name=args.get("mod_name", ""),
            hook_signature=hook_signature,
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
        return mod_gen.build_mod(
            args["project_dir"],
            configuration=args.get("configuration", "Debug"),
            build_pck_artifact=args.get("build_pck_artifact", False),
        )

    elif name == "install_mod":
        return mod_gen.install_mod(
            args["project_dir"],
            mod_name=args.get("mod_name", ""),
            configuration=args.get("configuration", "Debug"),
            include_pck=args.get("include_pck"),
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

    # ── Asset & PCK ──
    elif name == "build_pck":
        return build_pck(
            source_dir=args["source_dir"],
            output_path=args["output_path"],
            base_prefix=args.get("base_prefix", ""),
            convert_pngs=args.get("convert_pngs", True),
        )

    elif name == "list_pck":
        return list_pck_contents(args["pck_path"])

    elif name == "scaffold_character_assets":
        return scaffold_character_assets(
            mod_name=args["mod_name"],
            class_name=args["class_name"],
            output_dir=args["output_dir"],
            sprite_size=args.get("sprite_size", 300),
        )

    elif name == "get_character_asset_paths":
        return get_character_asset_paths(
            char_id=args["char_id"],
            mod_name=args["mod_name"],
        )

    # ── GDRE Tools ──
    elif name == "list_game_assets":
        return await asyncio.to_thread(
            gdre_tools.list_game_assets,
            GAME_DIR,
            filter_glob=args.get("filter_glob", ""),
            filter_ext=args.get("filter_ext", ""),
        )

    elif name == "search_game_assets":
        return await asyncio.to_thread(
            gdre_tools.search_game_assets,
            GAME_DIR,
            pattern=args["pattern"],
            extensions=args.get("extensions"),
        )

    elif name == "extract_game_assets":
        return await asyncio.to_thread(
            gdre_tools.extract_game_assets,
            GAME_DIR,
            output_dir=args["output_dir"],
            include=args.get("include", ""),
            exclude=args.get("exclude", ""),
            scripts_only=args.get("scripts_only", False),
        )

    elif name == "recover_game_project":
        output_dir = args.get("output_dir", "")
        if not output_dir:
            output_dir = os.path.join(os.path.dirname(DECOMPILED_DIR), "recovered")
        return await asyncio.to_thread(
            gdre_tools.recover_game_project,
            GAME_DIR,
            output_dir=output_dir,
        )

    elif name == "decompile_gdscript":
        return await asyncio.to_thread(
            gdre_tools.decompile_gdscript,
            input_path=args["input_path"],
            output_dir=args.get("output_dir", ""),
        )

    elif name == "convert_resource":
        return await asyncio.to_thread(
            gdre_tools.convert_resource,
            input_path=args["input_path"],
            output_dir=args.get("output_dir", ""),
            direction=args.get("direction", "bin_to_txt"),
        )

    # ── Live Bridge ──
    elif name == "bridge_ping":
        from . import bridge_client
        return await _call_bridge(bridge_client.ping)

    elif name == "bridge_get_screen":
        from . import bridge_client
        return await _call_bridge(bridge_client.get_screen)

    elif name == "bridge_get_run_state":
        from . import bridge_client
        return await _call_bridge(bridge_client.get_run_state)

    elif name == "bridge_get_combat_state":
        from . import bridge_client
        return await _call_bridge(bridge_client.get_combat_state)

    elif name == "bridge_get_player_state":
        from . import bridge_client
        return await _call_bridge(bridge_client.get_player_state)

    elif name == "bridge_get_map_state":
        from . import bridge_client
        return await _call_bridge(bridge_client.get_map_state)

    elif name == "bridge_get_available_actions":
        from . import bridge_client
        return await _call_bridge(bridge_client.get_available_actions)

    elif name == "bridge_get_full_state":
        from . import bridge_client
        return await _call_bridge(bridge_client.get_full_state)

    elif name == "bridge_auto_proceed":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.auto_proceed,
            skip_cards=args.get("skip_cards", True),
            skip_rewards=args.get("skip_rewards", False),
            timeout_seconds=args.get("timeout_seconds", 15),
        )

    elif name == "bridge_act_and_wait":
        from . import bridge_client
        action = args.pop("action")
        settle_timeout = args.pop("settle_timeout", 5.0)
        return await _call_bridge(
            bridge_client.act_and_wait,
            action=action,
            settle_timeout=settle_timeout,
            **args,
        )

    elif name == "bridge_play_card":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.play_card,
            card_index=args["card_index"],
            target_index=args.get("target_index", -1),
        )

    elif name == "bridge_end_turn":
        from . import bridge_client
        return await _call_bridge(bridge_client.end_turn)

    elif name == "bridge_start_run":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.start_run,
            character=args.get("character", "Ironclad"),
            ascension=args.get("ascension", 0),
            seed=args.get("seed"),
            fixture=args.get("fixture"),
            modifiers=args.get("modifiers"),
            acts=args.get("acts"),
            relics=args.get("relics"),
            cards=args.get("cards"),
            potions=args.get("potions"),
            powers=args.get("powers"),
            gold=args.get("gold"),
            hp=args.get("hp"),
            energy=args.get("energy"),
            draw_cards=args.get("draw_cards"),
            fight=args.get("fight"),
            event=args.get("event"),
            godmode=args.get("godmode", False),
            fixture_commands=args.get("fixture_commands"),
        )

    elif name == "bridge_console":
        from . import bridge_client
        return await _call_bridge(bridge_client.execute_console_command, args["command"])

    # ── New Generators ──
    elif name == "generate_event":
        return mod_gen.generate_event(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            is_shared=args.get("is_shared", False),
            choices=args.get("choices"),
        )

    elif name == "generate_orb":
        return mod_gen.generate_orb(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            passive_amount=args.get("passive_amount", 3),
            evoke_amount=args.get("evoke_amount", 9),
            passive_description=args.get("passive_description", ""),
            evoke_description=args.get("evoke_description", ""),
        )

    elif name == "generate_enchantment":
        hook_signature = _lookup_hook_signature(args.get("trigger_hook", ""))
        return mod_gen.generate_enchantment(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            trigger_hook=args.get("trigger_hook", ""),
            description=args.get("description", ""),
            hook_signature=hook_signature,
        )

    elif name == "generate_modifier":
        return mod_gen.generate_modifier(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            modifier_type=args.get("modifier_type", "Bad"),
            description=args.get("description", ""),
            hook=args.get("hook", ""),
        )

    elif name == "generate_ancient":
        return mod_gen.generate_ancient(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            option_relics=args.get("option_relics"),
            min_act_number=args.get("min_act_number", 2),
        )

    elif name == "generate_create_visuals_patch":
        return mod_gen.generate_create_visuals_patch(
            mod_namespace=args["mod_namespace"],
        )

    elif name == "generate_act_encounter_patch":
        return mod_gen.generate_act_encounter_patch(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            act_class=args["act_class"],
            encounter_class=args["encounter_class"],
        )

    elif name == "generate_game_action":
        return mod_gen.generate_game_action(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            description=args.get("description", ""),
            parameters=args.get("parameters"),
        )

    elif name == "generate_mechanic":
        return mod_gen.generate_mechanic(
            mod_namespace=args["mod_namespace"],
            mod_name=args["mod_name"],
            keyword_name=args["keyword_name"],
            keyword_description=args.get("keyword_description", ""),
            sample_card_name=args.get("sample_card_name", ""),
            sample_relic_name=args.get("sample_relic_name", ""),
        )

    elif name == "generate_custom_tooltip":
        return mod_gen.generate_custom_tooltip(
            mod_namespace=args["mod_namespace"],
            tag_name=args["tag_name"],
            title=args["title"],
            tooltip_description=args["tooltip_description"],
        )

    elif name == "generate_save_data":
        return mod_gen.generate_save_data(
            mod_namespace=args["mod_namespace"],
            mod_id=args["mod_id"],
            class_name=args.get("class_name", "ModSaveData"),
            fields=args.get("fields"),
        )

    elif name == "generate_test_scenario":
        return mod_gen.generate_test_scenario(
            scenario_name=args["scenario_name"],
            relics=args.get("relics"),
            cards=args.get("cards"),
            gold=args.get("gold", 0),
            hp=args.get("hp", 0),
            powers=args.get("powers"),
            fight=args.get("fight", ""),
            event=args.get("event", ""),
            godmode=args.get("godmode", False),
        )

    elif name == "generate_vfx_scene":
        return mod_gen.generate_vfx_scene(
            node_name=args["node_name"],
            particle_count=args.get("particle_count", 30),
            lifetime=args.get("lifetime", 0.5),
            one_shot=args.get("one_shot", True),
            explosiveness=args.get("explosiveness", 0.8),
        )

    # ── Code Intelligence ──
    elif name == "suggest_patches":
        return analyzer.suggest_patches(
            desired_behavior=args["desired_behavior"],
            max_suggestions=args.get("max_suggestions", 10),
        )

    elif name == "analyze_method_callers":
        return analyzer.analyze_method_callers(
            class_name=args["class_name"],
            method_name=args["method_name"],
            max_results=args.get("max_results", 30),
        )

    elif name == "get_entity_relationships":
        return analyzer.get_entity_relationships(args["entity_name"])

    elif name == "search_hooks_by_signature":
        results = analyzer.search_hooks_by_signature(args["param_type"])
        return {"count": len(results), "hooks": results}

    elif name == "get_hook_signature":
        return analyzer.get_hook_signature(args["hook_name"])

    elif name == "suggest_hooks":
        return analyzer.suggest_hooks(
            intent=args["intent"],
            max_suggestions=args.get("max_suggestions", 10),
        )

    elif name == "analyze_build_output":
        return analyzer.analyze_build_output(
            stdout=args.get("stdout", ""),
            stderr=args.get("stderr", ""),
        )

    # ── Mod Validation & Compatibility ──
    elif name == "validate_mod":
        return analyzer.validate_mod(args["project_dir"])

    elif name == "diff_game_versions":
        return analyzer.diff_game_versions(
            old_decompiled_dir=args["old_decompiled_dir"],
            new_decompiled_dir=args["new_decompiled_dir"],
        )

    elif name == "check_mod_compatibility":
        return analyzer.check_mod_compatibility(args["project_dir"])

    elif name == "list_game_vfx":
        return analyzer.list_game_vfx(query=args.get("query", ""))

    # ── Extended Bridge ──
    elif name == "bridge_use_potion":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.use_potion,
            potion_index=args["potion_index"],
            target_index=args.get("target_index", -1),
        )

    elif name == "bridge_make_event_choice":
        from . import bridge_client
        return await _call_bridge(bridge_client.make_event_choice, args["choice_index"])

    elif name == "bridge_navigate_map":
        from . import bridge_client
        return await _call_bridge(bridge_client.navigate_map, row=args["row"], col=args["col"])

    elif name == "bridge_rest_site_choice":
        from . import bridge_client
        return await _call_bridge(bridge_client.rest_site_choice, args["choice"])

    elif name == "bridge_shop_action":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.shop_action,
            action=args["action"],
            index=args.get("index", 0),
        )

    # ── Screen Interaction Handlers ──
    elif name == "bridge_execute_action":
        from . import bridge_client
        action = args.pop("action")
        return await _call_bridge(bridge_client.execute_action, action, **args)

    elif name == "bridge_reward_select":
        from . import bridge_client
        return await _call_bridge(bridge_client.reward_select, args["reward_index"])

    elif name == "bridge_reward_skip":
        from . import bridge_client
        return await _call_bridge(bridge_client.execute_action, "reward_skip")

    elif name == "bridge_reward_proceed":
        from . import bridge_client
        return await _call_bridge(bridge_client.reward_proceed)

    elif name == "bridge_card_select":
        from . import bridge_client
        if "card_indices" in args:
            return await _call_bridge(
                bridge_client.card_select,
                args["card_indices"],
                confirm=args.get("confirm", False),
            )
        return await _call_bridge(
            bridge_client.card_select,
            args.get("card_index", 0),
            confirm=args.get("confirm", False),
        )

    elif name == "bridge_card_skip":
        from . import bridge_client
        return await _call_bridge(bridge_client.card_skip)

    elif name == "bridge_card_confirm":
        from . import bridge_client
        return await _call_bridge(bridge_client.card_confirm)

    elif name == "bridge_treasure_pick":
        from . import bridge_client
        return await _call_bridge(bridge_client.treasure_pick, args.get("treasure_index", 0))

    elif name == "bridge_treasure_proceed":
        from . import bridge_client
        return await _call_bridge(bridge_client.treasure_proceed)

    elif name == "bridge_proceed":
        from . import bridge_client
        return await _call_bridge(bridge_client.proceed)

    elif name == "bridge_discard_potion":
        from . import bridge_client
        return await _call_bridge(bridge_client.discard_potion, args["potion_index"])

    elif name == "bridge_shop_buy":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.shop_buy,
            item_type=args["item_type"],
            index=args.get("index", 0),
        )

    elif name == "bridge_shop_proceed":
        from . import bridge_client
        return await _call_bridge(bridge_client.shop_proceed)

    elif name == "bridge_rest_site_proceed":
        from . import bridge_client
        return await _call_bridge(bridge_client.rest_site_proceed)

    elif name == "bridge_get_card_piles":
        from . import bridge_client
        return await _call_bridge(bridge_client.get_card_piles)

    elif name == "bridge_manipulate_state":
        from . import bridge_client
        return await _call_bridge(bridge_client.manipulate_state, args)

    # ── Live Coding & Iteration ──
    elif name == "bridge_hot_swap_patches":
        from . import bridge_client
        return await _call_bridge(bridge_client.hot_swap_patches, args["dll_path"])

    elif name == "bridge_set_game_speed":
        from . import bridge_client
        return await _call_bridge(bridge_client.set_game_speed, args.get("speed", 1.0))

    elif name == "bridge_restart_run":
        from . import bridge_client
        return await _call_bridge(bridge_client.restart_run)

    elif name == "bridge_get_state_diff":
        from . import bridge_client
        return await _call_bridge(bridge_client.get_state_diff)

    elif name == "bridge_get_exceptions":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.get_exceptions,
            max_count=args.get("max_count", 20),
            since_id=args.get("since_id", 0),
        )

    elif name == "bridge_get_events":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.get_events,
            since_id=args.get("since_id", 0),
            max_count=args.get("max_count", 100),
        )

    elif name == "bridge_capture_screenshot":
        from . import bridge_client
        return await _call_bridge(bridge_client.capture_screenshot, args.get("save_path", ""))

    elif name == "bridge_save_snapshot":
        from . import bridge_client
        return await _call_bridge(bridge_client.save_snapshot, args.get("name", "default"))

    elif name == "bridge_restore_snapshot":
        from . import bridge_client
        return await _call_bridge(bridge_client.restore_snapshot, args.get("name", "default"))

    # ── Breakpoints & Stepping ──
    elif name == "bridge_debug_pause":
        from . import bridge_client
        return await _call_bridge(bridge_client.debug_pause)

    elif name == "bridge_debug_resume":
        from . import bridge_client
        return await _call_bridge(bridge_client.debug_resume)

    elif name == "bridge_debug_step":
        from . import bridge_client
        return await _call_bridge(bridge_client.debug_step, args.get("mode", "action"))

    elif name == "bridge_debug_set_breakpoint":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.debug_set_breakpoint,
            bp_type=args.get("type", "action"),
            target=args.get("target", ""),
            condition=args.get("condition"),
        )

    elif name == "bridge_debug_remove_breakpoint":
        from . import bridge_client
        return await _call_bridge(bridge_client.debug_remove_breakpoint, args["id"])

    elif name == "bridge_debug_list_breakpoints":
        from . import bridge_client
        return await _call_bridge(bridge_client.debug_list_breakpoints)

    elif name == "bridge_debug_clear_breakpoints":
        from . import bridge_client
        return await _call_bridge(bridge_client.debug_clear_breakpoints)

    elif name == "bridge_debug_get_context":
        from . import bridge_client
        return await _call_bridge(bridge_client.debug_get_context)

    # ── Debugging & Logging ──
    elif name == "bridge_get_game_log":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.get_game_log,
            max_count=args.get("max_count", 100),
            since_id=args.get("since_id", 0),
            level=args.get("level"),
            contains=args.get("contains"),
        )

    elif name == "bridge_set_log_level":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.set_log_level,
            log_type=args.get("type"),
            level=args.get("level"),
            global_level=args.get("global_level"),
            capture_level=args.get("capture_level"),
        )

    elif name == "bridge_get_log_levels":
        from . import bridge_client
        return await _call_bridge(bridge_client.get_log_levels)

    elif name == "bridge_get_diagnostics":
        from . import bridge_client
        return await _call_bridge(bridge_client.get_diagnostics, args.get("log_lines", 40))

    elif name == "bridge_clear_exceptions":
        from . import bridge_client
        return await _call_bridge(bridge_client.clear_exceptions)

    elif name == "bridge_clear_events":
        from . import bridge_client
        return await _call_bridge(bridge_client.clear_events)

    # ── AutoSlay ──
    elif name == "bridge_autoslay_start":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.autoslay_start,
            character=args.get("character", "Ironclad"),
            seed=args.get("seed"),
            runs=args.get("runs", 1),
            loop=args.get("loop", False),
        )

    elif name == "bridge_autoslay_stop":
        from . import bridge_client
        return await _call_bridge(bridge_client.autoslay_stop)

    elif name == "bridge_autoslay_status":
        from . import bridge_client
        return await _call_bridge(bridge_client.autoslay_status)

    elif name == "bridge_autoslay_configure":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.autoslay_configure,
            run_timeout_seconds=args.get("run_timeout_seconds"),
            room_timeout_seconds=args.get("room_timeout_seconds"),
            screen_timeout_seconds=args.get("screen_timeout_seconds"),
            polling_interval_ms=args.get("polling_interval_ms"),
            watchdog_timeout_seconds=args.get("watchdog_timeout_seconds"),
            max_floor=args.get("max_floor"),
        )

    # ── Navigation & Window Helpers ──
    elif name == "bridge_navigate_to_combat":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.navigate_to_combat,
            timeout=args.get("timeout", 60),
            neow_choice_index=args.get("neow_choice_index", 0),
        )

    elif name == "bridge_focus_game":
        from . import bridge_client
        return await _call_bridge(bridge_client.focus_game_window)

    elif name == "bridge_wait_for_screen":
        from . import bridge_client
        return await _call_bridge(
            bridge_client.wait_for_screen,
            args["target_screen"],
            timeout_seconds=args.get("timeout", 15),
        )

    # ── Test Runner ──
    elif name == "run_test_scenario":
        from .test_runner import run_test_scenario
        return run_test_scenario(args["scenario"])

    # ── File Watcher ──
    elif name == "watch_project":
        from .file_watcher import start_watching
        return start_watching(
            project_dir=args["project_dir"],
            mods_dir=args["mods_dir"],
            mod_name=args.get("mod_name", ""),
            configuration=args.get("configuration", "Debug"),
        )

    elif name == "stop_watching":
        from .file_watcher import stop_watching
        return stop_watching()

    elif name == "watcher_status":
        from .file_watcher import watcher_status
        return watcher_status()

    # ── Analysis ──
    elif name == "reverse_hook_lookup":
        return analyzer.reverse_hook_lookup(args["entity_name"])

    # ── Project Workflow ──
    elif name == "package_mod":
        from .project_workflow import package_mod
        return package_mod(
            project_dir=args["project_dir"],
            output_path=args.get("output_path", ""),
        )

    elif name == "check_dependencies":
        from .project_workflow import check_dependencies
        return check_dependencies(args["project_dir"])

    elif name == "discover_mod_projects":
        from .project_workflow import discover_mod_projects
        return discover_mod_projects(args["workspace_dir"])

    # ── Advanced Generators ──
    elif name == "generate_net_message":
        return mod_gen.generate_net_message(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            transfer_mode=args.get("transfer_mode", "Reliable"),
            should_broadcast=args.get("should_broadcast", True),
            fields=args.get("fields"),
        )

    elif name == "generate_godot_ui":
        return mod_gen.generate_godot_ui(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            title=args.get("title", "My Panel"),
            base_type=args.get("base_type", "Control"),
            controls=args.get("controls"),
            show_in_process=args.get("show_in_process", False),
        )

    elif name == "generate_settings_panel":
        return mod_gen.generate_settings_panel(
            mod_namespace=args["mod_namespace"],
            class_name=args.get("class_name", "ModSettings"),
            mod_id=args["mod_id"],
            properties=args.get("properties"),
        )

    elif name == "generate_hover_tip":
        return mod_gen.generate_hover_tip(
            mod_namespace=args["mod_namespace"],
            class_name=args.get("class_name", "ModHoverTips"),
        )

    elif name == "generate_overlay":
        return mod_gen.generate_overlay(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            mod_id=args.get("mod_id", "mymod"),
            overlay_description=args.get("overlay_description", "Custom overlay"),
            inject_target=args.get("inject_target", "NCombatRoom"),
        )

    elif name == "generate_floating_panel":
        return mod_gen.generate_floating_panel(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            mod_id=args.get("mod_id", "mymod"),
            panel_title=args.get("panel_title", "Info Panel"),
            initial_content=args.get("initial_content", "Panel content here."),
            hotkey=args.get("hotkey", "F7"),
            inject_target=args.get("inject_target", "NCombatRoom"),
        )

    elif name == "generate_animated_bar":
        return mod_gen.generate_animated_bar(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            mod_id=args.get("mod_id", "mymod"),
            bar_label=args.get("bar_label", "Health"),
            bar_width=args.get("bar_width", "200"),
            bar_height=args.get("bar_height", "20"),
            color_low=args.get("color_low", "0.9f, 0.2f, 0.15f"),
            color_high=args.get("color_high", "0.2f, 0.85f, 0.3f"),
            pulse_enabled=args.get("pulse_enabled", "true"),
            inject_target=args.get("inject_target", "NCombatRoom"),
        )

    elif name == "generate_scrollable_list":
        return mod_gen.generate_scrollable_list(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            mod_id=args.get("mod_id", "mymod"),
            list_title=args.get("list_title", "Item List"),
            hotkey=args.get("hotkey", "F9"),
            panel_width=args.get("panel_width", "250"),
            inject_target=args.get("inject_target", "NCombatRoom"),
        )

    elif name == "generate_transpiler_patch":
        return mod_gen.generate_transpiler_patch(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            target_type=args["target_type"],
            target_method=args["target_method"],
            description=args.get("description", ""),
            search_opcode=args.get("search_opcode", "Callvirt"),
            search_method=args.get("search_method", ""),
            mod_id=args.get("mod_id", "mymod"),
        )

    elif name == "generate_reflection_accessor":
        return mod_gen.generate_reflection_accessor(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            target_type=args["target_type"],
            fields=args.get("fields"),
        )

    elif name == "generate_custom_keyword":
        return mod_gen.generate_custom_keyword(
            mod_namespace=args["mod_namespace"],
            keyword_name=args["keyword_name"],
        )

    elif name == "generate_custom_pile":
        return mod_gen.generate_custom_pile(
            mod_namespace=args["mod_namespace"],
            pile_name=args["pile_name"],
        )

    elif name == "generate_spire_field":
        return mod_gen.generate_spire_field(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            target_type=args["target_type"],
            field_name=args.get("field_name", "Value"),
            field_type=args.get("field_type", "int"),
            default_value=args.get("default_value", "0"),
        )

    elif name == "generate_dynamic_var":
        return mod_gen.generate_dynamic_var(
            mod_namespace=args["mod_namespace"],
            class_name=args["class_name"],
            var_name=args["var_name"],
            default_value=args.get("default_value", 0),
        )

    # ── Image Generation & Processing ──
    elif name == "generate_art":
        ig = _get_image_gen()
        return await ig.generate_and_process(
            description=args["description"],
            asset_type=args["asset_type"],
            name=args["name"],
            project_dir=args["project_dir"],
            model=args.get("model"),
        )

    elif name == "process_art":
        ig = _get_image_gen()
        return await ig.process_existing_image(
            image_path=args["image_path"],
            asset_type=args["asset_type"],
            name=args["name"],
            project_dir=args["project_dir"],
        )

    elif name == "list_art_profiles":
        ig = _get_image_gen()
        profiles = {}
        for atype, profile in ig.PROFILES.items():
            profiles[atype] = {
                "background": profile.get("bg", "per-variant"),
                "generation_size": ig.GEN_SIZES.get(atype, (512, 512)),
                "variants": [
                    {
                        "path": v["rel_path"],
                        "size": v["size"],
                        "background": v.get("bg", profile.get("bg", "opaque")),
                        "effect": v.get("effect"),
                    }
                    for v in profile.get("variants", [])
                ],
            }
        return profiles

    else:
        return f"Unknown tool: {name}"


# ─── Guides ──────────────────────────────────────────────────────────────────

def _get_guide(topic: str) -> str:
    guides_dir = Path(__file__).parent / "docs" / "guides"
    guide_file = guides_dir / f"{topic}.md"
    if guide_file.exists():
        return guide_file.read_text(encoding="utf-8")
    available = [f.stem for f in guides_dir.glob("*.md")]
    return f"Unknown topic: {topic}. Available: {', '.join(sorted(available))}"



# ─── Utility Functions ───────────────────────────────────────────────────────

def _get_baselib_reference(topic: str) -> str:
    baselib_dir = Path(__file__).parent / "docs" / "baselib"
    ref_file = baselib_dir / f"{topic}.md"
    if ref_file.exists():
        return ref_file.read_text(encoding="utf-8")
    available = [f.stem for f in baselib_dir.glob("*.md")]
    return f"Unknown BaseLib topic: {topic}. Available: {', '.join(sorted(available))}"


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
    from .setup import _find_ilspycmd

    dll_path = Path(GAME_DIR) / "data_sts2_windows_x86_64" / "sts2.dll"
    if not dll_path.exists():
        return {"success": False, "error": f"sts2.dll not found at {dll_path}"}

    exe = _find_ilspycmd()
    if not exe:
        return {"success": False, "error": "ilspycmd not found. Install: dotnet tool install -g ilspycmd"}

    output_dir = Path(DECOMPILED_DIR)
    # Clear existing
    if output_dir.exists():
        import shutil
        shutil.rmtree(str(output_dir))
    output_dir.mkdir(parents=True, exist_ok=True)

    try:
        result = subprocess.run(
            [exe, "-p", "-o", str(output_dir), str(dll_path)],
            capture_output=True,
            text=True,
            timeout=300,
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
    except subprocess.TimeoutExpired:
        return {"success": False, "error": "Decompilation timed out after 5 minutes"}


# ─── Entry Point ─────────────────────────────────────────────────────────────

async def main():
    # Auto-detect game path on first run (writes to stderr only, no MCP interference)
    auto_detect_on_startup()

    async with stdio_server() as (read_stream, write_stream):
        await server.run(
            read_stream,
            write_stream,
            server.create_initialization_options(),
        )
