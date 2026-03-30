"""Character asset scaffolding for STS2 modding.

Generates complete directory structures, placeholder scenes, and checklists
for creating new playable characters.
"""

import json
import re
from pathlib import Path


def _snake(name: str) -> str:
    s = re.sub(r'(.)([A-Z][a-z]+)', r'\1_\2', name)
    return re.sub(r'([a-z0-9])([A-Z])', r'\1_\2', s).lower()


# ─── Scene Templates ──────────────────────────────────────────────────────────

CREATURE_VISUALS_TSCN = """\
[gd_scene load_steps=3 format=3]

[ext_resource type="Script" path="res://src/Core/Nodes/Combat/NCreatureVisuals.cs" id="1_script"]
[ext_resource type="Texture2D" path="res://{mod_name}/Characters/{class_name}/{snake_name}.png" id="2_texture"]

[node name="{class_name}" type="Node2D"]
script = ExtResource("1_script")

[node name="Visuals" type="Sprite2D" parent="."]
unique_name_in_owner = true
position = Vector2(0, -{center_y})
scale = Vector2({scale}, {scale})
texture = ExtResource("2_texture")

[node name="Bounds" type="Control" parent="."]
unique_name_in_owner = true
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
offset_left = -{half_width}
offset_top = -{full_height}
offset_right = {half_width}
grow_horizontal = 2
grow_vertical = 2
mouse_filter = 2

[node name="CenterPos" type="Marker2D" parent="."]
unique_name_in_owner = true
position = Vector2(0, -{center_y})

[node name="IntentPos" type="Marker2D" parent="."]
unique_name_in_owner = true
position = Vector2(0, -{intent_y})

[node name="OrbPos" type="Marker2D" parent="."]
unique_name_in_owner = true
position = Vector2(-80, -{center_y})
"""

ENERGY_COUNTER_TSCN = """\
[gd_scene format=3]

[node name="{class_name}EnergyCounter" type="Control"]
layout_mode = 3
anchors_preset = 15
"""

CHAR_SELECT_BG_TSCN = """\
[gd_scene load_steps=2 format=3]

[ext_resource type="Texture2D" path="res://{mod_name}/Characters/{class_name}/char_select_bg.png" id="1_bg"]

[node name="CharSelectBg" type="TextureRect"]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
texture = ExtResource("1_bg")
expand_mode = 1
stretch_mode = 6
"""

REST_SITE_TSCN = """\
[gd_scene load_steps=2 format=3]

[ext_resource type="Texture2D" path="res://{mod_name}/Characters/{class_name}/{snake_name}.png" id="1_sprite"]

[node name="{class_name}RestSite" type="Node2D"]

[node name="Sprite" type="Sprite2D" parent="."]
texture = ExtResource("1_sprite")
position = Vector2(0, -100)
scale = Vector2(0.5, 0.5)
"""

MERCHANT_TSCN = """\
[gd_scene load_steps=2 format=3]

[ext_resource type="Texture2D" path="res://{mod_name}/Characters/{class_name}/{snake_name}.png" id="1_sprite"]

[node name="{class_name}Merchant" type="Node2D"]

[node name="Sprite" type="Sprite2D" parent="."]
texture = ExtResource("1_sprite")
position = Vector2(0, -100)
scale = Vector2(0.5, 0.5)
"""

CARD_TRAIL_TSCN = """\
[gd_scene format=3]

[node name="CardTrail" type="GPUParticles2D"]
emitting = false
amount = 20
lifetime = 0.5
"""

ICON_TSCN = """\
[gd_scene load_steps=2 format=3]

[ext_resource type="Texture2D" path="res://{mod_name}/Characters/{class_name}/icon.png" id="1_icon"]

[node name="{class_name}Icon" type="TextureRect"]
texture = ExtResource("1_icon")
expand_mode = 1
"""


def get_character_asset_paths(char_id: str, mod_name: str) -> dict:
    """Get ALL required asset paths for a character.

    Args:
        char_id: Character class name in lowercase (e.g., "alchemist")
        mod_name: Mod folder name (e.g., "MyMod")
    """
    cid = char_id.lower()
    return {
        "combat_visuals": {
            "scene": f"res://{mod_name}/Characters/{char_id}/{cid}.tscn",
            "sprite": f"res://{mod_name}/Characters/{char_id}/{cid}.png",
            "note": "NCreatureVisuals scene with Visuals, Bounds, CenterPos, IntentPos nodes. For Spine animations, replace Sprite2D with SpineSprite.",
        },
        "energy_counter": {
            "scene": f"res://{mod_name}/Characters/{char_id}/{cid}_energy_counter.tscn",
            "note": "Energy orb display. Can be a simple Control with TextureRects for layers.",
        },
        "character_select": {
            "background": f"res://{mod_name}/Characters/{char_id}/char_select_bg.png",
            "icon": f"res://{mod_name}/Characters/{char_id}/char_select_{cid}.png",
            "icon_locked": f"res://{mod_name}/Characters/{char_id}/char_select_{cid}_locked.png",
            "scene": f"res://{mod_name}/Characters/{char_id}/char_select_bg_{cid}.tscn",
        },
        "icons": {
            "top_panel": f"res://{mod_name}/Characters/{char_id}/character_icon_{cid}.png",
            "top_panel_outline": f"res://{mod_name}/Characters/{char_id}/character_icon_{cid}_outline.png",
            "icon_scene": f"res://{mod_name}/Characters/{char_id}/{cid}_icon.tscn",
            "map_marker": f"res://{mod_name}/Characters/{char_id}/map_marker_{cid}.png",
        },
        "animations": {
            "rest_site": f"res://{mod_name}/Characters/{char_id}/{cid}_rest_site.tscn",
            "merchant": f"res://{mod_name}/Characters/{char_id}/{cid}_merchant.tscn",
            "card_trail": f"res://{mod_name}/Characters/{char_id}/card_trail_{cid}.tscn",
        },
        "required_spine_animations": [
            "idle_loop (looping)",
            "attack",
            "cast",
            "hurt",
            "die",
            "relaxed_loop (looping, for rest site)",
        ],
        "sfx_events": [
            f"event:/sfx/characters/{cid}/{cid}_select",
            f"event:/sfx/characters/{cid}/{cid}_attack",
            f"event:/sfx/characters/{cid}/{cid}_cast",
            f"event:/sfx/characters/{cid}/{cid}_die",
        ],
        "localization_keys": {
            f"{cid}.title": f"{char_id}",
            f"{cid}.titleObject": f"the {char_id}",
            f"{cid}.pronounSubject": "they",
            f"{cid}.pronounObject": "them",
            f"{cid}.possessiveAdjective": "their",
            f"{cid}.pronounPossessive": "theirs",
            f"{char_id.upper()}.title": char_id,
            f"{char_id.upper()}.description": f"TODO: {char_id} character description",
        },
        "energy_counter_layers": {
            "note": "Energy counter needs 5 layer PNGs (index 0-4) stacked to form the orb",
            "layers": [
                f"res://{mod_name}/Characters/{char_id}/energy_counters/{cid}_orb_layer_0.png — Background/shadow",
                f"res://{mod_name}/Characters/{char_id}/energy_counters/{cid}_orb_layer_1.png — Orb body",
                f"res://{mod_name}/Characters/{char_id}/energy_counters/{cid}_orb_layer_2.png — Inner glow",
                f"res://{mod_name}/Characters/{char_id}/energy_counters/{cid}_orb_layer_3.png — Highlight/detail",
                f"res://{mod_name}/Characters/{char_id}/energy_counters/{cid}_orb_layer_4.png — Foreground overlay",
            ],
        },
        "card_pool_energy_icons": {
            "big": f"res://{mod_name}/Characters/{char_id}/ui/{cid}_energy_icon.png",
            "text": f"res://{mod_name}/Characters/{char_id}/ui/text_{cid}_energy_icon.png",
            "note": "Energy icons displayed on card cost. Big ~48x48, text ~16x16.",
        },
        "multiplayer_hands": {
            "pointing": f"res://{mod_name}/Characters/{char_id}/hands/multiplayer_hand_{cid}_point.png",
            "rock": f"res://{mod_name}/Characters/{char_id}/hands/multiplayer_hand_{cid}_rock.png",
            "paper": f"res://{mod_name}/Characters/{char_id}/hands/multiplayer_hand_{cid}_paper.png",
            "scissors": f"res://{mod_name}/Characters/{char_id}/hands/multiplayer_hand_{cid}_scissors.png",
            "note": "Hand gesture textures for Rock-Paper-Scissors in multiplayer mode.",
        },
    }


def scaffold_character_assets(
    mod_name: str,
    class_name: str,
    output_dir: str,
    sprite_size: int = 300,
) -> dict:
    """Create the complete directory structure and placeholder files for a new character.

    Args:
        mod_name: Mod namespace/folder name
        class_name: Character class name (PascalCase)
        output_dir: Root output directory
        sprite_size: Size of placeholder sprite in pixels

    Returns:
        Dict with created files and remaining checklist
    """
    snake_name = _snake(class_name)
    cid = snake_name
    out = Path(output_dir)
    char_dir = out / mod_name / "Characters" / class_name
    char_dir.mkdir(parents=True, exist_ok=True)

    created_files = []

    # Scene dimensions for a character sprite
    center_y = sprite_size // 2 + 30
    half_width = sprite_size // 2 + 20
    full_height = sprite_size + 20
    intent_y = full_height + 70

    # 1. Combat visuals scene
    scene = CREATURE_VISUALS_TSCN.format(
        mod_name=mod_name, class_name=class_name, snake_name=snake_name,
        center_y=center_y, half_width=half_width, full_height=full_height,
        intent_y=intent_y, scale=1,
    )
    (char_dir / f"{snake_name}.tscn").write_text(scene)
    created_files.append(f"{mod_name}/Characters/{class_name}/{snake_name}.tscn")

    # 2. Energy counter scene
    ec = ENERGY_COUNTER_TSCN.format(class_name=class_name)
    (char_dir / f"{snake_name}_energy_counter.tscn").write_text(ec)
    created_files.append(f"{mod_name}/Characters/{class_name}/{snake_name}_energy_counter.tscn")

    # 3. Character select background scene
    bg = CHAR_SELECT_BG_TSCN.format(mod_name=mod_name, class_name=class_name)
    (char_dir / f"char_select_bg_{snake_name}.tscn").write_text(bg)
    created_files.append(f"{mod_name}/Characters/{class_name}/char_select_bg_{snake_name}.tscn")

    # 4. Rest site scene
    rs = REST_SITE_TSCN.format(mod_name=mod_name, class_name=class_name, snake_name=snake_name)
    (char_dir / f"{snake_name}_rest_site.tscn").write_text(rs)
    created_files.append(f"{mod_name}/Characters/{class_name}/{snake_name}_rest_site.tscn")

    # 5. Merchant scene
    ms = MERCHANT_TSCN.format(mod_name=mod_name, class_name=class_name, snake_name=snake_name)
    (char_dir / f"{snake_name}_merchant.tscn").write_text(ms)
    created_files.append(f"{mod_name}/Characters/{class_name}/{snake_name}_merchant.tscn")

    # 6. Card trail scene
    ct = CARD_TRAIL_TSCN
    (char_dir / f"card_trail_{snake_name}.tscn").write_text(ct)
    created_files.append(f"{mod_name}/Characters/{class_name}/card_trail_{snake_name}.tscn")

    # 7. Icon scene
    ic = ICON_TSCN.format(mod_name=mod_name, class_name=class_name)
    (char_dir / f"{snake_name}_icon.tscn").write_text(ic)
    created_files.append(f"{mod_name}/Characters/{class_name}/{snake_name}_icon.tscn")

    # 8. Localization
    loc_dir = out / mod_name / "localization" / "eng"
    loc_dir.mkdir(parents=True, exist_ok=True)
    char_loc = {
        f"{snake_name}.title": class_name,
        f"{snake_name}.titleObject": f"the {class_name}",
        f"{snake_name}.pronounSubject": "they",
        f"{snake_name}.pronounObject": "them",
        f"{snake_name}.possessiveAdjective": "their",
        f"{snake_name}.pronounPossessive": "theirs",
        f"{class_name.upper()}.title": class_name,
        f"{class_name.upper()}.description": "A new character joins the Spire.",
    }
    (loc_dir / "characters.json").write_text(json.dumps(char_loc, indent=2))
    created_files.append(f"{mod_name}/localization/eng/characters.json")

    # Build the checklist of images that need to be created
    image_checklist = [
        f"{mod_name}/Characters/{class_name}/{snake_name}.png — Character combat sprite (at least {sprite_size}x{sprite_size}px). Replace with Spine animation for full quality.",
        f"{mod_name}/Characters/{class_name}/char_select_bg.png — Character select background (1920x1080)",
        f"{mod_name}/Characters/{class_name}/char_select_{snake_name}.png — Character select portrait",
        f"{mod_name}/Characters/{class_name}/char_select_{snake_name}_locked.png — Locked portrait variant",
        f"{mod_name}/Characters/{class_name}/character_icon_{snake_name}.png — Top panel icon (small, ~64x64)",
        f"{mod_name}/Characters/{class_name}/character_icon_{snake_name}_outline.png — Top panel icon outline",
        f"{mod_name}/Characters/{class_name}/icon.png — General icon (64x64)",
        f"{mod_name}/Characters/{class_name}/map_marker_{snake_name}.png — Map marker icon (small)",
        f"{mod_name}/Characters/{class_name}/energy_counters/{snake_name}_orb_layer_0..4.png — 5 energy counter layer PNGs (background, body, inner glow, highlight, overlay)",
        f"{mod_name}/Characters/{class_name}/ui/{snake_name}_energy_icon.png — Card pool energy icon (~48x48)",
        f"{mod_name}/Characters/{class_name}/ui/text_{snake_name}_energy_icon.png — Card pool text energy icon (~16x16)",
        f"{mod_name}/Characters/{class_name}/hands/multiplayer_hand_{snake_name}_point/rock/paper/scissors.png — Multiplayer RPS hand gestures (optional)",
    ]

    return {
        "character_dir": str(char_dir),
        "created_files": created_files,
        "created_count": len(created_files),
        "image_checklist": image_checklist,
        "next_steps": [
            f"1. Create the character sprite PNG at {mod_name}/Characters/{class_name}/{snake_name}.png",
            "2. Create character select background, portrait, and icon PNGs",
            "3. Optionally replace static sprite with Spine animation (.skel + .atlas + .png)",
            f"4. Build PCK: use build_pck tool with source_dir pointing to {mod_name}/ folder",
            "5. Build the C# DLL with CustomCharacterModel class (use generate_character tool)",
            "6. Install both .dll and .pck to game's mods/ directory",
        ],
    }
