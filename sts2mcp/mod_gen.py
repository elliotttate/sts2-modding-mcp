"""Mod project generation, scaffolding, and building for Slay the Spire 2."""

import json
import os
import re
import subprocess
from pathlib import Path
from typing import Optional

# ─── Templates ────────────────────────────────────────────────────────────────

CSPROJ_TEMPLATE = """\
<Project Sdk="Godot.NET.Sdk/4.4.0">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <RootNamespace>{namespace}</RootNamespace>
    <AssemblyName>{assembly_name}</AssemblyName>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Sts2Dir>{sts2_data_dir}</Sts2Dir>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="GodotSharp" Version="4.4.0" />
    <PackageReference Include="Lib.Harmony" Version="2.4.2" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="sts2">
      <HintPath>$(Sts2Dir)\\sts2.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
"""

MOD_ENTRY_TEMPLATE = """\
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace {namespace};

[ModInitializer("Init")]
public static class ModEntry
{{
    private static Harmony? _harmony;

    public static void Init()
    {{
        Log.Warn("[{mod_name}] Initializing...");

        _harmony = new Harmony("{harmony_id}");
        _harmony.PatchAll();

        Log.Warn("[{mod_name}] Loaded successfully!");
    }}
}}
"""

CARD_TEMPLATE = """\
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Cards;

[Pool(typeof({pool}))]
public sealed class {class_name} : CardModel
{{
    public override CardType Type => CardType.{card_type};
    public override CardRarity Rarity => CardRarity.{rarity};
    public override TargetType TargetType => TargetType.{target_type};
    public override CardEnergyCost EnergyCost => {energy_cost};
{keywords_block}
    protected override IEnumerable<DynamicVar> CanonicalVars
    {{
        get
        {{
            return new DynamicVar[]
            {{
{dynamic_vars}
            }};
        }}
    }}

    public override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {{
{on_play_body}
    }}
{upgrade_block}}}
"""

RELIC_TEMPLATE = """\
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Relics;

[Pool(typeof({pool}))]
public sealed class {class_name} : RelicModel
{{
    public override RelicRarity Rarity => RelicRarity.{rarity};
{extra_fields}
    protected override IEnumerable<DynamicVar> CanonicalVars
    {{
        get
        {{
            return new DynamicVar[]
            {{
{dynamic_vars}
            }};
        }}
    }}
{hook_methods}}}
"""

POWER_TEMPLATE = """\
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Powers;

public sealed class {class_name} : PowerModel
{{
    public override PowerType Type => PowerType.{power_type};
    public override PowerStackType StackType => PowerStackType.{stack_type};
{hook_methods}}}
"""

POTION_TEMPLATE = """\
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Potions;

[Pool(typeof({pool}))]
public sealed class {class_name} : PotionModel
{{
    public override PotionRarity Rarity => PotionRarity.{rarity};
    public override PotionUsage Usage => PotionUsage.{usage};
    public override TargetType TargetType => TargetType.{target_type};

    protected override IEnumerable<DynamicVar> CanonicalVars
    {{
        get
        {{
            return new DynamicVar[]
            {{
{dynamic_vars}
            }};
        }}
    }}

    public override async Task OnUse(PlayerChoiceContext choiceContext, Creature? target)
    {{
{on_use_body}
    }}
}}
"""

MONSTER_TEMPLATE = """\
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Intents;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.MonsterMoves;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Monsters;

public sealed class {class_name} : MonsterModel
{{
    public override int MinInitialHp => {min_hp};
    public override int MaxInitialHp => {max_hp};
{extra_fields}
    protected override string VisualsPath => "res://{mod_name}/MonsterResources/{class_name}/{snake_name}.tscn";

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {{
{move_state_machine}
    }}
{move_methods}}}
"""

ENCOUNTER_TEMPLATE = """\
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Rooms;

namespace {namespace}.Encounters;

public sealed class {class_name} : EncounterModel
{{
    public override RoomType RoomType => RoomType.{room_type};

    public override IEnumerable<MonsterModel> AllPossibleMonsters
    {{
        get
        {{
{all_monsters}
        }}
    }}

    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
    {{
        return new List<(MonsterModel, string?)>
        {{
{generate_monsters}
        }};
    }}
}}
"""

HARMONY_PATCH_TEMPLATE = """\
using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace {namespace}.Patches;

[HarmonyPatch(typeof({target_type}), nameof({target_type}.{target_method}))]
public static class {class_name}
{{
    public static {patch_type} {patch_method}({params})
    {{
{body}
    }}
}}
"""

LOCALIZATION_TEMPLATE = {
    "card": {
        "{KEY}.title": "{title}",
        "{KEY}.description": "{description}",
        "{KEY}.upgrade.description": "{upgrade_description}",
    },
    "relic": {
        "{KEY}.title": "{title}",
        "{KEY}.description": "{description}",
        "{KEY}.flavor": "{flavor}",
    },
    "power": {
        "{KEY}.title": "{title}",
        "{KEY}.smartDescription": "{description}",
        "{KEY}.description": "{description}",
    },
    "potion": {
        "{KEY}.title": "{title}",
        "{KEY}.description": "{description}",
    },
    "monster": {
        "{KEY}.name": "{title}",
    },
    "encounter": {
        "{KEY}.title": "{title}",
        "{KEY}.loss": "{loss_text}",
    },
}

MONSTER_SCENE_TEMPLATE = """\
[gd_scene load_steps=3 format=3]

[ext_resource type="Script" path="res://src/Core/Nodes/Combat/NCreatureVisuals.cs" id="1_script"]
[ext_resource type="Texture2D" path="res://{mod_name}/MonsterResources/{class_name}/{image_file}" id="2_texture"]

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
"""

CREATE_VISUALS_PATCH = """\
using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace {namespace}.Patches;

/// <summary>
/// Required patch for custom static-image enemies. Apply once in your mod.
/// Enables loading custom .tscn scenes for monster visuals.
/// </summary>
[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.CreateVisuals))]
public static class CreateVisualsPatch
{{
    private static readonly MethodInfo _visualsPathGetter = typeof(MonsterModel)
        .GetProperty("VisualsPath", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetGetMethod(true)!;

    public static bool Prefix(MonsterModel __instance, ref NCreatureVisuals __result)
    {{
        var path = (string)_visualsPathGetter.Invoke(__instance, null)!;
        var scene = PreloadManager.Cache.GetScene(path);

        try
        {{
            __result = scene.Instantiate<NCreatureVisuals>();
            return false;
        }}
        catch (InvalidCastException)
        {{
        }}

        var raw = scene.Instantiate<Node2D>();
        var visuals = new NCreatureVisuals();
        visuals.Name = raw.Name;

        foreach (var child in raw.GetChildren())
        {{
            raw.RemoveChild(child);
            visuals.AddChild(child);
            if (child is Node n && n.UniqueNameInOwner)
            {{
                n.Owner = visuals;
                n.UniqueNameInOwner = true;
            }}
        }}

        raw.QueueFree();
        __result = visuals;
        return false;
    }}
}}
"""

# ─── BaseLib Templates ────────────────────────────────────────────────────────

CSPROJ_BASELIB_TEMPLATE = """\
<Project Sdk="Godot.NET.Sdk/4.4.0">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <RootNamespace>{namespace}</RootNamespace>
    <AssemblyName>{assembly_name}</AssemblyName>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <Sts2Dir>{sts2_data_dir}</Sts2Dir>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="GodotSharp" Version="4.4.0" />
    <PackageReference Include="Lib.Harmony" Version="2.4.2" />
    <PackageReference Include="Alchyr.Sts2.BaseLib" Version="0.1.*" />
  </ItemGroup>

  <ItemGroup>
    <Reference Include="sts2">
      <HintPath>$(Sts2Dir)\\sts2.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
"""

BASELIB_CARD_TEMPLATE = """\
using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Cards;

[Pool(typeof({pool}))]
public sealed class {class_name} : CustomCardModel
{{
    public override CardType Type => CardType.{card_type};
    public override CardRarity Rarity => CardRarity.{rarity};
    public override TargetType TargetType => TargetType.{target_type};
    public override CardEnergyCost EnergyCost => {energy_cost};
{keywords_block}
    protected override IEnumerable<DynamicVar> CanonicalVars
    {{
        get
        {{
            return new DynamicVar[]
            {{
{dynamic_vars}
            }};
        }}
    }}

    public override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {{
{on_play_body}
    }}
{upgrade_block}}}
"""

BASELIB_RELIC_TEMPLATE = """\
using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Relics;

[Pool(typeof({pool}))]
public sealed class {class_name} : CustomRelicModel
{{
    public override RelicRarity Rarity => RelicRarity.{rarity};
{extra_fields}
    protected override IEnumerable<DynamicVar> CanonicalVars
    {{
        get
        {{
            return new DynamicVar[]
            {{
{dynamic_vars}
            }};
        }}
    }}
{hook_methods}}}
"""

BASELIB_POWER_TEMPLATE = """\
using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Powers;

public sealed class {class_name} : CustomPowerModel, ICustomPower
{{
    public override PowerType Type => PowerType.{power_type};
    public override PowerStackType StackType => PowerStackType.{stack_type};

    public string PackedIcon => "res://{mod_name}/images/powers/{snake_name}_packed.png";
    public string BigIcon => "res://{mod_name}/images/powers/{snake_name}.png";
    public string? BigBetaIcon => null;
{hook_methods}}}
"""

BASELIB_POTION_TEMPLATE = """\
using System.Collections.Generic;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.ValueProps;

namespace {namespace}.Potions;

[Pool(typeof({pool}))]
public sealed class {class_name} : CustomPotionModel
{{
    public override PotionRarity Rarity => PotionRarity.{rarity};
    public override PotionUsage Usage => PotionUsage.{usage};
    public override TargetType TargetType => TargetType.{target_type};

    protected override IEnumerable<DynamicVar> CanonicalVars
    {{
        get
        {{
            return new DynamicVar[]
            {{
{dynamic_vars}
            }};
        }}
    }}

    public override async Task OnUse(PlayerChoiceContext choiceContext, Creature? target)
    {{
{on_use_body}
    }}
}}
"""

MOD_CONFIG_TEMPLATE = """\
using BaseLib.Config;

namespace {namespace};

public class {class_name} : SimpleModConfig
{{
    public override string FileName => "{config_filename}";
{properties}}}
"""

CHARACTER_TEMPLATE = """\
using System.Collections.Generic;
using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Characters;

namespace {namespace}.Characters;

public sealed class {class_name} : CustomCharacterModel
{{
    // Pool models must be static readonly and instantiated here
    public static readonly {class_name}CardPool CardPool = new();
    public static readonly {class_name}RelicPool RelicPool = new();
    public static readonly {class_name}PotionPool PotionPool = new();

    public override CardPoolModel CardPoolModel => CardPool;

    // Visual Assets
    public override string VisualsPath => "res://{mod_name}/Characters/{class_name}/{snake_name}.tscn";
    public override string SelectScreenBgPath => "res://{mod_name}/Characters/{class_name}/select_bg.png";
    public override string EnergyCounterPath => "res://{mod_name}/Characters/{class_name}/energy_counter.tscn";

    // Character Info
    public override int StartingMaxHp => {starting_hp};
    public override int StartingGold => {starting_gold};
    public override int OrbSlots => {orb_slots};

    // Override these for custom animations/SFX:
    // public override string AttackSfx => "res://...";
    // public override string CastSfx => "res://...";
    // public override string DeathSfx => "res://...";

    protected override IReadOnlyList<CardModel> StarterDeck()
    {{
        var deck = new List<CardModel>();
        // TODO: Add starter cards
        // deck.Add(ModelDb.Card<YourStarterCard>().ToMutable());
        return deck;
    }}

    protected override IReadOnlyList<RelicModel> StarterRelics()
    {{
        var relics = new List<RelicModel>();
        // TODO: Add starter relics
        return relics;
    }}
}}

// Card Pool
public sealed class {class_name}CardPool : CustomCardPoolModel
{{
    // Override for custom card frames, energy icons, etc.
}}

// Relic Pool
public sealed class {class_name}RelicPool : CustomRelicPoolModel {{ }}

// Potion Pool
public sealed class {class_name}PotionPool : CustomPotionPoolModel {{ }}
"""

CUSTOM_POOL_TEMPLATE = """\
using BaseLib.Abstracts;
using Godot;

namespace {namespace}.Pools;

public sealed class {class_name} : Custom{pool_type}PoolModel
{{
{pool_body}}}
"""

ACT_ENCOUNTER_PATCH = """\
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Encounters;

namespace {namespace}.Patches;

/// <summary>
/// Adds the custom encounter to the specified act.
/// </summary>
[HarmonyPatch(typeof({act_class}), nameof({act_class}.GenerateAllEncounters))]
public static class {class_name}
{{
    public static void Postfix(ref IEnumerable<EncounterModel> __result)
    {{
        var list = __result.ToList();
        list.Add(ModelDb.Encounter<{encounter_class}>());
        __result = list;
    }}
}}
"""


def to_snake_case(name: str) -> str:
    s1 = re.sub(r"(.)([A-Z][a-z]+)", r"\1_\2", name)
    return re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", s1).lower()


def to_screaming_snake(name: str) -> str:
    return to_snake_case(name).upper()


def to_model_id(mod_id: str, name: str) -> str:
    return f"{mod_id.upper()}-{to_screaming_snake(name)}"


class ModGenerator:
    def __init__(self, game_dir: str):
        self.game_dir = Path(game_dir)
        self.data_dir = self.game_dir / "data_sts2_windows_x86_64"
        self.mods_dir = self.game_dir / "mods"

    def create_mod_project(
        self,
        mod_name: str,
        author: str,
        description: str = "",
        output_dir: str = "",
        use_baselib: bool = True,
    ) -> dict:
        """Create a complete mod project scaffold."""
        mod_id = to_snake_case(mod_name).replace("_", "")
        namespace = mod_name.replace(" ", "").replace("-", "")
        assembly_name = namespace

        if output_dir:
            project_dir = Path(output_dir)
        else:
            project_dir = self.game_dir / "mod_projects" / mod_name

        project_dir.mkdir(parents=True, exist_ok=True)

        # Subdirectories
        code_dir = project_dir / "Code"
        code_dir.mkdir(exist_ok=True)
        subdirs = ["Cards", "Relics", "Powers", "Potions", "Monsters", "Encounters", "Patches"]
        if use_baselib:
            subdirs.extend(["Characters", "Config"])
        for sub in subdirs:
            (code_dir / sub).mkdir(exist_ok=True)

        loc_dir = project_dir / namespace / "localization" / "eng"
        loc_dir.mkdir(parents=True, exist_ok=True)

        images_dir = project_dir / namespace / "images"
        for sub in ["relics", "powers", "cards", "potions"]:
            (images_dir / sub).mkdir(parents=True, exist_ok=True)

        monster_res = project_dir / namespace / "MonsterResources"
        monster_res.mkdir(parents=True, exist_ok=True)

        if use_baselib:
            (project_dir / namespace / "Characters").mkdir(parents=True, exist_ok=True)

        # .csproj
        template = CSPROJ_BASELIB_TEMPLATE if use_baselib else CSPROJ_TEMPLATE
        csproj_content = template.format(
            namespace=namespace,
            assembly_name=assembly_name,
            sts2_data_dir=str(self.data_dir).replace("\\", "\\\\"),
        )
        (project_dir / f"{namespace}.csproj").write_text(csproj_content)

        # ModEntry.cs
        entry_content = MOD_ENTRY_TEMPLATE.format(
            namespace=namespace,
            mod_name=mod_name,
            harmony_id=f"com.{author.lower().replace(' ', '')}.{mod_id}",
        )
        (code_dir / "ModEntry.cs").write_text(entry_content)

        # mod_manifest.json
        manifest = {
            "id": mod_id,
            "pck_name": namespace,
            "name": mod_name,
            "author": author,
            "description": description,
            "version": "1.0.0",
            "has_pck": True,
            "has_dll": True,
            "affects_gameplay": True,
        }
        (project_dir / "mod_manifest.json").write_text(
            json.dumps(manifest, indent=2)
        )

        # Empty localization files
        for loc_type in ["cards", "relics", "powers", "potions", "monsters", "encounters"]:
            (loc_dir / f"{loc_type}.json").write_text("{}\n")

        created_files = []
        for f in project_dir.rglob("*"):
            if f.is_file():
                created_files.append(str(f.relative_to(project_dir)))

        return {
            "project_dir": str(project_dir),
            "namespace": namespace,
            "mod_id": mod_id,
            "use_baselib": use_baselib,
            "created_files": created_files,
        }

    def generate_card(
        self,
        mod_namespace: str,
        class_name: str,
        card_type: str = "Attack",
        rarity: str = "Common",
        target_type: str = "AnyEnemy",
        energy_cost: int = 1,
        damage: int = 0,
        block: int = 0,
        magic_number: int = 0,
        keywords: list[str] | None = None,
        pool: str = "ColorlessCardPool",
        description: str = "",
        upgrade_description: str = "",
        use_baselib: bool = True,
    ) -> dict:
        """Generate a card class and localization."""
        kw_block = ""
        if keywords:
            kw_items = ", ".join(f"CardKeyword.{k}" for k in keywords)
            kw_block = f"\n    public override HashSet<CardKeyword> Keywords => new() {{ {kw_items} }};\n"

        # Build dynamic vars
        dyn_vars = []
        if damage > 0:
            dyn_vars.append(f"                new DamageVar({damage}M)")
        if block > 0:
            dyn_vars.append(f"                new BlockVar({block}M)")
        if magic_number > 0:
            dyn_vars.append(f"                new MagicNumberVar({magic_number}M)")
        if not dyn_vars:
            dyn_vars.append("                // Add DynamicVar entries here")

        # Build OnPlay body
        on_play_lines = []
        if damage > 0 and card_type == "Attack":
            on_play_lines.append(
                "        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)\n"
                "            .FromCard(this, cardPlay)\n"
                "            .Execute(choiceContext);"
            )
        if block > 0:
            on_play_lines.append(
                "        await CreatureCmd.GainBlock(\n"
                "            Owner.Creature,\n"
                "            DynamicVars.Block.BaseValue,\n"
                "            ValueProp.Powered,\n"
                "            this);"
            )
        if not on_play_lines:
            on_play_lines.append("        // TODO: Implement card effect")
            on_play_lines.append("        await Task.CompletedTask;")

        # Upgrade block
        upgrade_block = ""
        if damage > 0 or block > 0:
            upgrade_lines = []
            if damage > 0:
                upgrade_lines.append(f"        DynamicVars.Damage.Upgrade({max(damage // 3, 1)}M);")
            if block > 0:
                upgrade_lines.append(f"        DynamicVars.Block.Upgrade({max(block // 3, 1)}M);")
            upgrade_block = f"""
    public override void OnUpgrade()
    {{
{chr(10).join(upgrade_lines)}
    }}
"""

        card_template = BASELIB_CARD_TEMPLATE if use_baselib else CARD_TEMPLATE
        source = card_template.format(
            namespace=mod_namespace,
            class_name=class_name,
            card_type=card_type,
            rarity=rarity,
            target_type=target_type,
            energy_cost=energy_cost,
            pool=pool,
            keywords_block=kw_block,
            dynamic_vars=",\n".join(dyn_vars),
            on_play_body="\n".join(on_play_lines),
            upgrade_block=upgrade_block,
        )

        model_id = to_screaming_snake(class_name)
        loc = {}
        loc[f"{model_id}.title"] = class_name.replace("_", " ")
        loc[f"{model_id}.description"] = description or "TODO: Add card description"
        if upgrade_description:
            loc[f"{model_id}.upgrade.description"] = upgrade_description

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Cards",
            "localization": {"cards.json": loc},
        }

    def generate_relic(
        self,
        mod_namespace: str,
        class_name: str,
        rarity: str = "Common",
        pool: str = "SharedRelicPool",
        description: str = "",
        flavor: str = "",
        trigger_hook: str = "",
        use_baselib: bool = True,
    ) -> dict:
        """Generate a relic class and localization."""
        extra_fields = ""
        hook_methods = ""

        if trigger_hook == "AfterDamageReceived":
            extra_fields = "\n    private bool _usedThisCombat;\n"
            hook_methods = """
    public override async Task AfterDamageReceived(
        PlayerChoiceContext choiceContext,
        Creature target,
        DamageResult result,
        ValueProp props,
        Creature? dealer,
        CardModel? cardSource)
    {
        if (!CombatManager.Instance.IsInProgress || target != Owner.Creature
            || result.UnblockedDamage <= 0 || _usedThisCombat)
            return;

        Flash();
        _usedThisCombat = true;

        // TODO: Implement relic effect
    }

    public override Task AfterCombatEnd(CombatRoom _)
    {
        _usedThisCombat = false;
        return Task.CompletedTask;
    }
"""
        elif trigger_hook == "BeforeCombatStart":
            hook_methods = """
    public override async Task BeforeCombatStart()
    {
        Flash();
        // TODO: Implement relic effect
        await Task.CompletedTask;
    }
"""
        elif trigger_hook == "AfterCardPlayed":
            hook_methods = """
    public override async Task AfterCardPlayed(
        CombatState combatState,
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner != Owner) return;

        Flash();
        // TODO: Implement relic effect
        await Task.CompletedTask;
    }
"""
        elif trigger_hook:
            hook_methods = f"""
    public override async Task {trigger_hook}(/* TODO: add parameters */)
    {{
        Flash();
        // TODO: Implement relic effect
        await Task.CompletedTask;
    }}
"""
        else:
            hook_methods = """
    // TODO: Override hook methods to implement relic behavior
    // Common hooks: BeforeCombatStart, AfterCardPlayed, AfterDamageReceived,
    //               AfterTurnEnd, AfterBlockGained, ModifyDamageAdditive, etc.
"""

        relic_template = BASELIB_RELIC_TEMPLATE if use_baselib else RELIC_TEMPLATE
        source = relic_template.format(
            namespace=mod_namespace,
            class_name=class_name,
            rarity=rarity,
            pool=pool,
            extra_fields=extra_fields,
            dynamic_vars="                // Add DynamicVar entries here",
            hook_methods=hook_methods,
        )

        model_id = to_screaming_snake(class_name)
        loc = {
            f"{model_id}.title": class_name.replace("_", " "),
            f"{model_id}.description": description or "TODO: Add relic description",
            f"{model_id}.flavor": flavor or "",
        }

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Relics",
            "localization": {"relics.json": loc},
        }

    def generate_power(
        self,
        mod_namespace: str,
        class_name: str,
        power_type: str = "Buff",
        stack_type: str = "Counter",
        description: str = "",
        trigger_hook: str = "",
        use_baselib: bool = True,
        mod_name: str = "",
    ) -> dict:
        """Generate a power class and localization."""
        hook_methods = ""

        if trigger_hook == "ModifyDamageAdditive":
            hook_methods = """
    public override decimal ModifyDamageAdditive(
        CombatState combatState,
        Creature? target,
        Creature? dealer,
        decimal currentDamage,
        ValueProp props,
        CardModel? cardSource,
        ModifyDamageHookType hookType)
    {
        if (dealer != Owner || !props.IsPowered()) return currentDamage;
        return currentDamage + Amount;
    }
"""
        elif trigger_hook == "ModifyDamageMultiplicative":
            hook_methods = """
    public override decimal ModifyDamageMultiplicative(
        CombatState combatState,
        Creature? target,
        Creature? dealer,
        decimal currentDamage,
        ValueProp props,
        CardModel? cardSource,
        ModifyDamageHookType hookType)
    {
        if (target != Owner || !props.IsPowered()) return currentDamage;
        return currentDamage * 1.5M;
    }
"""
        elif trigger_hook == "BeforeHandDraw":
            hook_methods = """
    public override async Task BeforeHandDraw(
        Player player,
        PlayerChoiceContext choiceContext,
        CombatState combatState)
    {
        if (player != Owner.Player) return;

        Flash();
        // TODO: Implement power effect
    }
"""
        elif trigger_hook == "AfterTurnEnd":
            hook_methods = """
    public override async Task AfterTurnEnd(CombatState combatState, CombatSide side)
    {
        if (side != Owner.Side) return;

        Flash();
        // TODO: Implement power effect
        await PowerCmd.Decrement(this);
    }
"""
        elif trigger_hook:
            hook_methods = f"""
    public override async Task {trigger_hook}(/* TODO: add parameters */)
    {{
        Flash();
        // TODO: Implement power effect
        await Task.CompletedTask;
    }}
"""
        else:
            hook_methods = """
    // TODO: Override hook methods to implement power behavior
    // Common hooks: ModifyDamageAdditive, ModifyDamageMultiplicative,
    //               BeforeHandDraw, AfterTurnEnd, BeforeTurnEnd,
    //               AfterCardPlayed, AfterDamageReceived, etc.
"""

        if use_baselib:
            snake_name = to_snake_case(class_name)
            source = BASELIB_POWER_TEMPLATE.format(
                namespace=mod_namespace,
                class_name=class_name,
                power_type=power_type,
                stack_type=stack_type,
                hook_methods=hook_methods,
                mod_name=mod_name or mod_namespace,
                snake_name=snake_name,
            )
        else:
            source = POWER_TEMPLATE.format(
                namespace=mod_namespace,
                class_name=class_name,
                power_type=power_type,
                stack_type=stack_type,
                hook_methods=hook_methods,
            )

        model_id = to_screaming_snake(class_name)
        loc = {
            f"{model_id}.title": class_name.replace("Power", "").replace("_", " ").strip(),
            f"{model_id}.smartDescription": description or "TODO: Add power description with {{Amount}} for stack count",
            f"{model_id}.description": description or "TODO: Add base description",
        }

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Powers",
            "localization": {"powers.json": loc},
        }

    def generate_potion(
        self,
        mod_namespace: str,
        class_name: str,
        rarity: str = "Common",
        usage: str = "CombatOnly",
        target_type: str = "None",
        pool: str = "SharedPotionPool",
        block: int = 0,
        description: str = "",
        use_baselib: bool = True,
    ) -> dict:
        """Generate a potion class and localization."""
        dyn_vars = []
        on_use_lines = []

        if block > 0:
            dyn_vars.append(f"                new BlockVar({block}M)")
            on_use_lines.append(
                "        await CreatureCmd.GainBlock(\n"
                "            target ?? Owner.Creature,\n"
                "            DynamicVars.Block.BaseValue,\n"
                "            ValueProp.Unpowered,\n"
                "            null);"
            )

        if not dyn_vars:
            dyn_vars.append("                // Add DynamicVar entries here")
        if not on_use_lines:
            on_use_lines.append("        // TODO: Implement potion effect")
            on_use_lines.append("        await Task.CompletedTask;")

        potion_template = BASELIB_POTION_TEMPLATE if use_baselib else POTION_TEMPLATE
        source = potion_template.format(
            namespace=mod_namespace,
            class_name=class_name,
            rarity=rarity,
            usage=usage,
            target_type=target_type,
            pool=pool,
            dynamic_vars=",\n".join(dyn_vars),
            on_use_body="\n".join(on_use_lines),
        )

        model_id = to_screaming_snake(class_name)
        loc = {
            f"{model_id}.title": class_name.replace("_", " "),
            f"{model_id}.description": description or "TODO: Add potion description",
        }

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Potions",
            "localization": {"potions.json": loc},
        }

    def generate_monster(
        self,
        mod_namespace: str,
        mod_name: str,
        class_name: str,
        min_hp: int = 50,
        max_hp: int = 55,
        moves: list[dict] | None = None,
        image_size: int = 200,
    ) -> dict:
        """Generate a monster class, scene, and localization.

        moves: list of dicts with keys: name, damage (optional), block (optional), type (attack/defend/buff/debuff)
        """
        snake_name = to_snake_case(class_name)

        if not moves:
            moves = [{"name": "STRIKE", "damage": 10, "type": "attack"}]

        # Build extra fields
        extra_fields_lines = []
        for move in moves:
            if move.get("damage"):
                extra_fields_lines.append(f"    private int {move['name'].title().replace('_', '')}Damage => {move['damage']};")
            if move.get("block"):
                extra_fields_lines.append(f"    private int {move['name'].title().replace('_', '')}Block => {move['block']};")
        extra_fields = "\n".join(extra_fields_lines) + "\n" if extra_fields_lines else ""

        # Build move state machine
        sm_lines = []
        for i, move in enumerate(moves):
            var_name = move["name"].lower()
            intent = self._get_intent_for_move(move)
            sm_lines.append(f"        var {var_name} = new MoveState(\"{move['name']}\", {move['name'].title().replace('_', '')}, {intent});")

        # Chain moves
        for i in range(len(moves)):
            next_idx = (i + 1) % len(moves)
            sm_lines.append(f"        {moves[i]['name'].lower()}.FollowUpState = {moves[next_idx]['name'].lower()};")

        first_move = moves[0]["name"].lower()
        all_moves = ", ".join(m["name"].lower() for m in moves)
        sm_lines.append(f"        return new MonsterMoveStateMachine(new List<MonsterState> {{ {all_moves} }}, {first_move});")

        # Build move methods
        method_lines = []
        for move in moves:
            method_name = move["name"].title().replace("_", "")
            body_lines = []
            if move.get("damage"):
                field_name = f"{method_name}Damage"
                body_lines.append(
                    f"        await DamageCmd.Attack({field_name})\n"
                    f"            .FromMonster(this)\n"
                    f"            .Execute(null);"
                )
            if move.get("block"):
                field_name = f"{method_name}Block"
                body_lines.append(
                    f"        await CreatureCmd.GainBlock(Creature, {field_name}, ValueProp.Move, null);"
                )
            if not body_lines:
                body_lines.append("        await Task.CompletedTask;")

            method_lines.append(f"""
    private async Task {method_name}(IReadOnlyList<Creature> targets)
    {{
{chr(10).join(body_lines)}
    }}""")

        # Scene file
        center_y = image_size // 2 + 15
        half_width = image_size // 2 + 10
        full_height = image_size + 10
        intent_y = full_height + 60

        scene = MONSTER_SCENE_TEMPLATE.format(
            mod_name=mod_name,
            class_name=class_name,
            image_file=f"{snake_name}.png",
            center_y=center_y,
            scale=1,
            half_width=half_width,
            full_height=full_height,
            intent_y=intent_y,
        )

        source = MONSTER_TEMPLATE.format(
            namespace=mod_namespace,
            class_name=class_name,
            min_hp=min_hp,
            max_hp=max_hp,
            mod_name=mod_name,
            snake_name=snake_name,
            extra_fields=extra_fields,
            move_state_machine="\n".join(sm_lines),
            move_methods="\n".join(method_lines),
        )

        model_id = to_screaming_snake(class_name)
        loc = {f"{model_id}.name": class_name.replace("_", " ")}

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Monsters",
            "scene": scene,
            "scene_file_name": f"{snake_name}.tscn",
            "scene_folder": f"{mod_name}/MonsterResources/{class_name}",
            "localization": {"monsters.json": loc},
            "image_note": f"Place a {image_size}x{image_size} PNG at {mod_name}/MonsterResources/{class_name}/{snake_name}.png",
        }

    def generate_encounter(
        self,
        mod_namespace: str,
        class_name: str,
        room_type: str = "Monster",
        monsters: list[str] | None = None,
    ) -> dict:
        """Generate an encounter class and localization."""
        if not monsters:
            monsters = ["MonsterClassName"]

        all_monsters_lines = []
        generate_lines = []
        for m in monsters:
            all_monsters_lines.append(f"            yield return ModelDb.Monster<{m}>();")
            generate_lines.append(f"            (ModelDb.Monster<{m}>().ToMutable(), null),")

        source = ENCOUNTER_TEMPLATE.format(
            namespace=mod_namespace,
            class_name=class_name,
            room_type=room_type,
            all_monsters="\n".join(all_monsters_lines),
            generate_monsters="\n".join(generate_lines),
        )

        model_id = to_screaming_snake(class_name)
        loc = {
            f"{model_id}.title": class_name.replace("_", " "),
            f"{model_id}.loss": "The [gold]{{encounter}}[/gold] proved too much for {{character}}.",
        }

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Encounters",
            "localization": {"encounters.json": loc},
        }

    def generate_harmony_patch(
        self,
        mod_namespace: str,
        class_name: str,
        target_type: str,
        target_method: str,
        patch_type: str = "Postfix",
        description: str = "",
    ) -> dict:
        """Generate a Harmony patch class."""
        if patch_type == "Prefix":
            params = f"{target_type} __instance"
            body = "        // Return false to skip original method, true to continue\n        // TODO: Implement patch logic\n        return true;"
            patch_method = "bool Prefix"
        else:
            params = f"{target_type} __instance"
            body = "        // TODO: Implement patch logic"
            patch_method = "void Postfix"

        source = HARMONY_PATCH_TEMPLATE.format(
            namespace=mod_namespace,
            class_name=class_name,
            target_type=target_type,
            target_method=target_method,
            patch_type=patch_type.lower(),
            patch_method=patch_method,
            params=params,
            body=body,
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Patches",
        }

    def generate_localization(
        self,
        mod_id: str,
        entity_type: str,
        entity_name: str,
        title: str = "",
        description: str = "",
        flavor: str = "",
        upgrade_description: str = "",
        loss_text: str = "",
    ) -> dict:
        """Generate localization entries for an entity."""
        key = to_model_id(mod_id, entity_name)
        template = LOCALIZATION_TEMPLATE.get(entity_type, {})
        loc = {}

        replacements = {
            "{KEY}": key,
            "{title}": title or entity_name.replace("_", " "),
            "{description}": description or "TODO",
            "{flavor}": flavor or "",
            "{upgrade_description}": upgrade_description or "",
            "{loss_text}": loss_text or "",
        }

        for loc_key, loc_val in template.items():
            final_key = loc_key
            final_val = loc_val
            for old, new in replacements.items():
                final_key = final_key.replace(old, new)
                final_val = final_val.replace(old, new)
            if final_val:
                loc[final_key] = final_val

        file_name = f"{entity_type}s.json" if not entity_type.endswith("s") else f"{entity_type}.json"
        # Normalize file names
        type_to_file = {
            "card": "cards.json",
            "relic": "relics.json",
            "power": "powers.json",
            "potion": "potions.json",
            "monster": "monsters.json",
            "encounter": "encounters.json",
            "event": "events.json",
        }
        file_name = type_to_file.get(entity_type, file_name)

        return {
            "file_name": file_name,
            "entries": loc,
        }

    def generate_create_visuals_patch(self, mod_namespace: str) -> dict:
        """Generate the CreateVisuals patch required for custom static-image enemies."""
        source = CREATE_VISUALS_PATCH.format(namespace=mod_namespace)
        return {
            "source": source,
            "file_name": "CreateVisualsPatch.cs",
            "folder": "Code/Patches",
        }

    def generate_act_encounter_patch(
        self,
        mod_namespace: str,
        class_name: str,
        act_class: str,
        encounter_class: str,
    ) -> dict:
        """Generate a patch to add an encounter to an act."""
        source = ACT_ENCOUNTER_PATCH.format(
            namespace=mod_namespace,
            class_name=class_name,
            act_class=act_class,
            encounter_class=encounter_class,
        )
        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Patches",
        }

    def build_mod(self, project_dir: str) -> dict:
        """Build a mod project using dotnet build."""
        project_path = Path(project_dir)
        if not project_path.exists():
            return {"success": False, "error": f"Project directory not found: {project_dir}"}

        csproj_files = list(project_path.glob("*.csproj"))
        if not csproj_files:
            return {"success": False, "error": "No .csproj file found in project directory"}

        try:
            result = subprocess.run(
                ["dotnet", "build", str(csproj_files[0]), "-c", "Debug"],
                capture_output=True,
                text=True,
                cwd=str(project_path),
                timeout=120,
            )
            return {
                "success": result.returncode == 0,
                "stdout": result.stdout,
                "stderr": result.stderr,
                "return_code": result.returncode,
            }
        except subprocess.TimeoutExpired:
            return {"success": False, "error": "Build timed out after 120 seconds"}
        except FileNotFoundError:
            return {"success": False, "error": "dotnet CLI not found. Install .NET SDK 9.0."}

    def install_mod(self, project_dir: str, mod_name: str = "") -> dict:
        """Install a built mod to the game's mods directory."""
        project_path = Path(project_dir)
        if not mod_name:
            manifest_path = project_path / "mod_manifest.json"
            if manifest_path.exists():
                manifest = json.loads(manifest_path.read_text())
                mod_name = manifest.get("id", project_path.name)
            else:
                mod_name = project_path.name

        mod_dir = self.mods_dir / mod_name
        mod_dir.mkdir(parents=True, exist_ok=True)

        copied = []

        # Copy DLL from build output
        for dll_path in project_path.rglob("bin/Debug/**/*.dll"):
            if dll_path.stem not in ("GodotSharp", "0Harmony", "sts2"):
                import shutil
                dest = mod_dir / dll_path.name
                shutil.copy2(str(dll_path), str(dest))
                copied.append(str(dll_path.name))

        # Copy manifest
        manifest_src = project_path / "mod_manifest.json"
        if manifest_src.exists():
            import shutil
            shutil.copy2(str(manifest_src), str(mod_dir / "mod_manifest.json"))
            copied.append("mod_manifest.json")

        # Copy PCK if exists
        for pck in project_path.glob("*.pck"):
            import shutil
            shutil.copy2(str(pck), str(mod_dir / pck.name))
            copied.append(pck.name)

        # Copy mod image if exists
        for img_name in ("mod_image.png", "icon.png"):
            img = project_path / img_name
            if img.exists():
                import shutil
                shutil.copy2(str(img), str(mod_dir / "mod_image.png"))
                copied.append("mod_image.png")
                break

        return {
            "mod_dir": str(mod_dir),
            "copied_files": copied,
        }

    def uninstall_mod(self, mod_name: str) -> dict:
        """Remove a mod from the game's mods directory."""
        mod_dir = self.mods_dir / mod_name
        if not mod_dir.exists():
            return {"success": False, "error": f"Mod not found: {mod_name}"}

        import shutil
        shutil.rmtree(str(mod_dir))
        return {"success": True, "removed": str(mod_dir)}

    def list_installed_mods(self) -> list[dict]:
        """List all installed mods."""
        if not self.mods_dir.exists():
            return []

        mods = []
        for entry in sorted(self.mods_dir.iterdir()):
            if not entry.is_dir():
                continue
            mod_info: dict = {"name": entry.name, "path": str(entry)}
            manifest = entry / "mod_manifest.json"
            if manifest.exists():
                try:
                    data = json.loads(manifest.read_text())
                    mod_info.update(data)
                except Exception:
                    pass
            files = [f.name for f in entry.iterdir() if f.is_file()]
            mod_info["files"] = files
            mods.append(mod_info)
        return mods

    def _get_intent_for_move(self, move: dict) -> str:
        mtype = move.get("type", "attack")
        if mtype == "attack" and move.get("damage"):
            return f"new SingleAttackIntent({move['name'].title().replace('_', '')}Damage)"
        elif mtype == "defend":
            return "new DefendIntent()"
        elif mtype == "buff":
            return "new BuffIntent()"
        elif mtype == "debuff":
            return "new DebuffIntent()"
        elif mtype == "attack_defend":
            intents = []
            if move.get("damage"):
                intents.append(f"new SingleAttackIntent({move['name'].title().replace('_', '')}Damage)")
            intents.append("new DefendIntent()")
            return f"new AbstractIntent[] {{ {', '.join(intents)} }}"
        else:
            return f"new SingleAttackIntent({move.get('damage', 0)})"

    # ─── BaseLib-specific generators ──────────────────────────────────────────

    def generate_character(
        self,
        mod_namespace: str,
        mod_name: str,
        class_name: str,
        starting_hp: int = 80,
        starting_gold: int = 99,
        orb_slots: int = 0,
    ) -> dict:
        """Generate a custom character class with pool models (requires BaseLib)."""
        snake_name = to_snake_case(class_name)
        source = CHARACTER_TEMPLATE.format(
            namespace=mod_namespace,
            mod_name=mod_name or mod_namespace,
            class_name=class_name,
            snake_name=snake_name,
            starting_hp=starting_hp,
            starting_gold=starting_gold,
            orb_slots=orb_slots,
        )

        model_id = to_screaming_snake(class_name)
        loc = {
            f"{model_id}.name": class_name.replace("_", " "),
            f"{model_id}.description": f"TODO: {class_name} description",
        }

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Characters",
            "localization": {"characters.json": loc},
            "notes": [
                "Requires BaseLib (Alchyr.Sts2.BaseLib NuGet package)",
                f"Create visual assets at {mod_name}/Characters/{class_name}/",
                "Need: character .tscn scene, select_bg.png, energy_counter.tscn",
                "Cards use [Pool(typeof({0}CardPool))] attribute".format(class_name),
                "Relics use [Pool(typeof({0}RelicPool))] attribute".format(class_name),
            ],
        }

    def generate_mod_config(
        self,
        mod_namespace: str,
        class_name: str = "MyModConfig",
        properties: list[dict] | None = None,
    ) -> dict:
        """Generate a mod configuration class with auto-UI (requires BaseLib).

        properties: list of dicts with keys: name, type (bool/double/enum), default, section, slider_min, slider_max, slider_step
        """
        if not properties:
            properties = [
                {"name": "EnableFeatureX", "type": "bool", "default": "true", "section": "General"},
                {"name": "Multiplier", "type": "double", "default": "1.0", "section": "Tuning",
                 "slider_min": 0.5, "slider_max": 3.0, "slider_step": 0.1},
            ]

        prop_lines = []
        current_section = ""
        for prop in properties:
            section = prop.get("section", "")
            if section and section != current_section:
                prop_lines.append(f'    [ConfigSection("{section}")]')
                current_section = section

            ptype = prop.get("type", "bool")
            pname = prop["name"]
            default = prop.get("default", "")

            if ptype == "double":
                smin = prop.get("slider_min", 0)
                smax = prop.get("slider_max", 10)
                step = prop.get("slider_step", 0.1)
                prop_lines.append(f"    [SliderRange({smin}, {smax}, {step})]")
                prop_lines.append(f"    public double {pname} {{ get; set; }} = {default};")
            elif ptype == "bool":
                prop_lines.append(f"    public bool {pname} {{ get; set; }} = {default};")
            elif ptype == "enum":
                enum_type = prop.get("enum_type", pname + "Option")
                prop_lines.append(f"    public {enum_type} {pname} {{ get; set; }} = {default};")
            prop_lines.append("")

        config_filename = to_snake_case(class_name.replace("Config", "").replace("Mod", "")) or "config"

        source = MOD_CONFIG_TEMPLATE.format(
            namespace=mod_namespace,
            class_name=class_name,
            config_filename=config_filename,
            properties="\n".join(prop_lines),
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Config",
            "notes": [
                "Requires BaseLib (Alchyr.Sts2.BaseLib NuGet package)",
                "Register in ModEntry.Init(): ModConfigRegistry.Register(\"mymodid\", new MyModConfig());",
                "Access: var config = ModConfigRegistry.Get<MyModConfig>(\"mymodid\");",
                f"Config saved to %APPDATA%\\.baselib\\{{ModName}}\\{config_filename}.cfg",
                "Auto-generates in-game settings UI with the config button",
            ],
        }
