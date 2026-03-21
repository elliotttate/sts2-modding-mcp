"""Mod project generation, scaffolding, and building for Slay the Spire 2."""

import json
import re
from pathlib import Path

from .project_workflow import (
    apply_generator_output as apply_generator_output_to_project,
    apply_generator_outputs as apply_generator_outputs_to_project,
    build_and_deploy_project,
    build_project,
    build_project_pck,
    deploy_project,
    inspect_project as inspect_project_impl,
    validate_project as validate_project_impl,
    validate_project_assets as validate_project_assets_impl,
    validate_project_localization as validate_project_localization_impl,
)

# ─── Templates ────────────────────────────────────────────────────────────────

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
    "orb": {
        "{KEY}.title": "{title}",
        "{KEY}.description": "{description}",
    },
    "enchantment": {
        "{KEY}.title": "{title}",
        "{KEY}.description": "{description}",
    },
    "character": {
        "{KEY}.name": "{title}",
        "{KEY}.description": "{description}",
    },
    "ancient": {
        "{KEY}.intro.text": "{description}",
        "{KEY}.option_1.text": "{title} Option 1",
        "{KEY}.option_2.text": "{title} Option 2",
        "{KEY}.option_3.text": "{title} Option 3",
    },
    "modifier": {
        "{KEY}.title": "{title}",
        "{KEY}.description": "{description}",
    },
}

_TEMPLATES_DIR = Path(__file__).parent / "templates"
_template_cache: dict[str, str] = {}


def _load_template(name: str) -> str:
    """Load a template from the templates directory, with caching."""
    if name not in _template_cache:
        path = _TEMPLATES_DIR / f"{name}.cs.tpl"
        _template_cache[name] = path.read_text(encoding="utf-8")
    return _template_cache[name]


def to_snake_case(name: str) -> str:
    name = re.sub(r"[^A-Za-z0-9]+", "_", name).strip("_")
    s1 = re.sub(r"(.)([A-Z][a-z]+)", r"\1_\2", name)
    return re.sub(r"_+", "_", re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", s1)).lower()


def to_screaming_snake(name: str) -> str:
    return to_snake_case(name).upper()


def to_model_id(mod_id: str, name: str) -> str:
    return f"{mod_id.upper()}-{to_screaming_snake(name)}"


def _normalize_identifier(name: str, *, fallback: str) -> str:
    sanitized = re.sub(r"[^A-Za-z0-9_]+", "", name)
    if sanitized and sanitized[0].isdigit():
        sanitized = f"_{sanitized}"
    return sanitized or fallback


def _normalize_slug(name: str, *, fallback: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "", to_snake_case(name))
    return slug or fallback


def _escape_csharp_string(value: str) -> str:
    return value.replace("\\", "\\\\").replace('"', '\\"')


def _extract_param_names(params: str) -> list[str]:
    names: list[str] = []
    for raw_param in [piece.strip() for piece in params.split(",") if piece.strip()]:
        parts = raw_param.replace("?", "").split()
        if parts:
            names.append(parts[-1])
    return names


def _default_hook_return_expression(return_type: str, params: str) -> str:
    for preferred_name in (
        "currentDamage",
        "currentBlock",
        "currentValue",
        "currentAmount",
        "current",
        "amount",
        "value",
    ):
        if preferred_name in _extract_param_names(params):
            return preferred_name

    normalized = return_type.replace("?", "")
    if normalized == "bool":
        return "true"
    if normalized in {"int", "uint", "long", "short", "byte"}:
        return "0"
    if normalized in {"decimal", "double", "float"}:
        return "0"
    return "default!"


def _build_hook_method_stub(hook_signature: dict, comment: str, include_flash: bool = False) -> str:
    hook = hook_signature.get("hook", hook_signature)
    method_name = hook["name"]
    return_type = hook["return_type"]
    params = hook["params"]
    async_prefix = "async " if return_type.startswith("Task") else ""

    body_lines = []
    if include_flash:
        body_lines.append("        Flash();")
    body_lines.append(f"        // TODO: Implement {comment}")
    if return_type == "Task":
        body_lines.append("        await Task.CompletedTask;")
    elif return_type.startswith("Task<"):
        inner = return_type[5:-1]
        body_lines.append("        await Task.CompletedTask;")
        body_lines.append(f"        return {_default_hook_return_expression(inner, params)};")
    else:
        body_lines.append(f"        return {_default_hook_return_expression(return_type, params)};")

    return (
        f"\n    public override {async_prefix}{return_type} {method_name}(\n"
        f"        {params.replace(', ', ',\n        ')})\n"
        "    {\n"
        f"{chr(10).join(body_lines)}\n"
        "    }\n"
    )


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
        mod_id = _normalize_slug(mod_name, fallback="mymod")
        namespace = _normalize_identifier(mod_name, fallback="MyMod")
        assembly_name = namespace
        author_id = _normalize_slug(author, fallback="author")

        if output_dir:
            project_dir = Path(output_dir)
        else:
            project_dir = self.game_dir / "mod_projects" / mod_name

        project_dir.mkdir(parents=True, exist_ok=True)
        existing_markers = [
            project_dir / "mod_manifest.json",
            project_dir / "Code" / "ModEntry.cs",
            *project_dir.glob("*.csproj"),
        ]
        if any(path.exists() for path in existing_markers):
            return {
                "success": False,
                "error": (
                    "Project directory already contains an STS2 mod scaffold. "
                    "Choose a new output directory or remove the existing scaffold first."
                ),
                "project_dir": str(project_dir),
            }

        # Subdirectories
        code_dir = project_dir / "Code"
        code_dir.mkdir(exist_ok=True)
        subdirs = ["Cards", "Relics", "Powers", "Potions", "Monsters", "Encounters", "Patches", "Events", "Orbs", "Enchantments", "Actions", "Networking", "UI", "Overlays", "Utils", "Keywords", "Piles", "Fields", "Vars"]
        if use_baselib:
            subdirs.extend(["Characters", "Config", "Ancients"])
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
        template = _load_template("csproj_baselib_template") if use_baselib else _load_template("csproj_template")
        csproj_content = template.format(
            namespace=namespace,
            assembly_name=assembly_name,
            sts2_data_dir=str(self.data_dir).replace("\\", "\\\\"),
        )
        (project_dir / f"{namespace}.csproj").write_text(csproj_content, encoding="utf-8", newline="\n")
        (project_dir / "NuGet.config").write_text(_load_template("nuget_config_template"), encoding="utf-8", newline="\n")

        # ModEntry.cs
        entry_content = _load_template("mod_entry_template").format(
            namespace=namespace,
            mod_name=mod_name,
            harmony_id=f"com.{author_id}.{mod_id}",
        )
        (code_dir / "ModEntry.cs").write_text(entry_content, encoding="utf-8", newline="\n")

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
            json.dumps(manifest, indent=2) + "\n",
            encoding="utf-8",
            newline="\n",
        )

        # Empty localization files
        loc_types = [
            "cards",
            "relics",
            "powers",
            "potions",
            "monsters",
            "encounters",
            "events",
            "orbs",
            "enchantments",
        ]
        if use_baselib:
            loc_types.extend(["characters", "ancients"])
        for loc_type in loc_types:
            (loc_dir / f"{loc_type}.json").write_text("{}\n", encoding="utf-8", newline="\n")

        created_files = []
        for f in project_dir.rglob("*"):
            if f.is_file():
                created_files.append(str(f.relative_to(project_dir)))

        return {
            "success": True,
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

        card_template = _load_template("baselib_card_template") if use_baselib else _load_template("card_template")
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
        hook_signature: dict | None = None,
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
            if hook_signature:
                hook_methods = _build_hook_method_stub(hook_signature, "relic effect", include_flash=True)
            else:
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

        relic_template = _load_template("baselib_relic_template") if use_baselib else _load_template("relic_template")
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
        hook_signature: dict | None = None,
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
            if hook_signature:
                hook_methods = _build_hook_method_stub(hook_signature, "power effect", include_flash=True)
            else:
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
            source = _load_template("baselib_power_template").format(
                namespace=mod_namespace,
                class_name=class_name,
                power_type=power_type,
                stack_type=stack_type,
                hook_methods=hook_methods,
                mod_name=mod_name or mod_namespace,
                snake_name=snake_name,
            )
        else:
            source = _load_template("power_template").format(
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

        potion_template = _load_template("baselib_potion_template") if use_baselib else _load_template("potion_template")
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

        scene = _load_template("monster_scene_template").format(
            mod_name=mod_name,
            class_name=class_name,
            image_file=f"{snake_name}.png",
            center_y=center_y,
            scale=1,
            half_width=half_width,
            full_height=full_height,
            intent_y=intent_y,
        )

        source = _load_template("monster_template").format(
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

        source = _load_template("encounter_template").format(
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

        source = _load_template("harmony_patch_template").format(
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
            "orb": "orbs.json",
            "enchantment": "enchantments.json",
            "character": "characters.json",
            "ancient": "ancients.json",
        }
        file_name = type_to_file.get(entity_type, file_name)

        return {
            "file_name": file_name,
            "entries": loc,
        }

    def generate_create_visuals_patch(self, mod_namespace: str) -> dict:
        """Generate the CreateVisuals patch required for custom static-image enemies."""
        source = _load_template("create_visuals_patch").format(namespace=mod_namespace)
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
        source = _load_template("act_encounter_patch").format(
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

    def build_mod(
        self,
        project_dir: str,
        configuration: str = "Debug",
        build_pck_artifact: bool = False,
    ) -> dict:
        """Build a mod project and optionally build its PCK."""
        result = build_project(project_dir, configuration=configuration)
        if build_pck_artifact and result.get("success"):
            result["pck"] = build_project_pck(project_dir)
            result["success"] = bool(result["success"]) and bool(result["pck"].get("success"))
        return result

    def inspect_project(self, project_dir: str) -> dict:
        """Inspect a mod project and infer its project-aware layout."""
        return inspect_project_impl(project_dir)

    def apply_generator_output(
        self,
        project_dir: str,
        generation_output: dict,
        overwrite: bool = False,
        dry_run: bool = False,
    ) -> dict:
        """Apply a single generator output into an existing project."""
        return apply_generator_output_to_project(
            project_dir,
            generation_output,
            overwrite=overwrite,
            dry_run=dry_run,
        )

    def apply_generator_outputs(
        self,
        project_dir: str,
        generation_outputs: list[dict],
        overwrite: bool = False,
        dry_run: bool = False,
    ) -> dict:
        """Apply multiple generator outputs into an existing project transactionally."""
        return apply_generator_outputs_to_project(
            project_dir,
            generation_outputs,
            overwrite=overwrite,
            dry_run=dry_run,
        )

    def build_project_pck(
        self,
        project_dir: str,
        output_path: str = "",
        convert_pngs: bool = True,
    ) -> dict:
        """Build a PCK using project-aware defaults."""
        return build_project_pck(
            project_dir,
            output_path=output_path,
            convert_pngs=convert_pngs,
        )

    def deploy_mod(
        self,
        project_dir: str,
        mod_name: str = "",
        configuration: str = "Debug",
        build_pck_artifact: bool | None = None,
    ) -> dict:
        """Build and deploy a project into the configured game mods directory."""
        return build_and_deploy_project(
            project_dir,
            mods_dir=self.mods_dir,
            mod_name=mod_name,
            configuration=configuration,
            build_pck_first=build_pck_artifact,
        )

    def validate_project_localization(self, project_dir: str) -> dict:
        """Validate localization files for a project."""
        return validate_project_localization_impl(project_dir)

    def validate_project_assets(self, project_dir: str) -> dict:
        """Validate project-owned asset references."""
        return validate_project_assets_impl(project_dir)

    def validate_project(self, project_dir: str) -> dict:
        """Run full project validation."""
        return validate_project_impl(project_dir)

    def install_mod(
        self,
        project_dir: str,
        mod_name: str = "",
        configuration: str = "Debug",
        include_pck: bool | None = None,
    ) -> dict:
        """Install a built mod to the game's mods directory."""
        return deploy_project(
            project_dir,
            mods_dir=self.mods_dir,
            mod_name=mod_name,
            configuration=configuration,
            include_pck=include_pck,
        )

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
        source = _load_template("character_template").format(
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

        source = _load_template("mod_config_template").format(
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

    def generate_event(
        self,
        mod_namespace: str,
        class_name: str,
        is_shared: bool = False,
        choices: list[dict] | None = None,
    ) -> dict:
        """Generate an event class with choice tree.

        choices: list of dicts with keys: label (str), method_name (str),
                 effect_description (str, optional)
        """
        if not choices:
            choices = [
                {"label": "Accept the offer", "method_name": "ChoiceAccept", "effect_description": "Gain 50 gold"},
                {"label": "Refuse", "method_name": "ChoiceRefuse", "effect_description": "Nothing happens"},
                {"label": "[Leave]", "method_name": "ChoiceLeave"},
            ]

        model_id = to_screaming_snake(class_name)
        option_lines = []
        loc = {
            f"{model_id}.title": class_name.replace("Event", "").replace("_", " ").strip(),
            f"{model_id}.pages.INITIAL.description": "TODO: Event intro text shown to the player.",
        }

        for choice in choices:
            label = choice.get("label", "Choice")
            method = choice.get("method_name", "Choice")
            option_key = to_screaming_snake(method.replace("Choice", "") or label)
            text_key = f"{model_id}.pages.INITIAL.options.{option_key}"
            option_lines.append(f'            new EventOption(this, {method}, "{text_key}"),')
            loc[f"{text_key}.title"] = label
            loc[f"{text_key}.description"] = choice.get("effect_description", "TODO: Describe this option.")
            loc[f"{model_id}.pages.RESULTS.{option_key}.description"] = (
                "You move on." if "leave" in method.lower() else f"TODO: Resolve {label.lower()}."
            )

        # Build option methods
        method_blocks = []
        for choice in choices:
            method = choice.get("method_name", "Choice")
            effect = choice.get("effect_description", "")
            option_key = to_screaming_snake(method.replace("Choice", "") or choice.get("label", "Choice"))
            body_lines = []
            if "leave" in method.lower():
                body_lines.append(f'        SetEventFinished(L10NLookup("{model_id}.pages.RESULTS.{option_key}.description"));')
            else:
                if effect:
                    body_lines.append(f'        // {effect}')
                body_lines.append(f'        // TODO: Implement {method} effect')
                body_lines.append(f'        SetEventFinished(L10NLookup("{model_id}.pages.RESULTS.{option_key}.description"));')

            method_blocks.append(f"""    private Task {method}()
    {{
{chr(10).join(body_lines)}
        return Task.CompletedTask;
    }}""")

        source = _load_template("event_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            is_shared="true" if is_shared else "false",
            options="\n".join(option_lines),
            option_methods="\n\n".join(method_blocks),
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Events",
            "localization": {"events.json": loc},
            "notes": [
                "Events need a Harmony patch to add them to an act's event pool.",
                "Use bridge_console with 'event EVENT_ID' to test.",
            ],
        }

    def generate_ancient(
        self,
        mod_namespace: str,
        class_name: str,
        option_relics: list[str] | None = None,
        min_act_number: int = 2,
    ) -> dict:
        """Generate a BaseLib ancient scaffold with three option pools."""
        if not option_relics:
            option_relics = ["BurningBlood", "Anchor", "BagOfPreparation"]

        while len(option_relics) < 3:
            option_relics.append(option_relics[-1])

        option_pool_lines = [
            f"        MakePool(ModelDb.Relic<{relic}>())"
            for relic in option_relics[:3]
        ]

        source = _load_template("ancient_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            option_pools=",\n".join(option_pool_lines),
            min_act_number=max(1, min_act_number),
        )

        model_id = to_screaming_snake(class_name)
        loc = {
            f"{model_id}.intro.text": "TODO: Ancient introduction dialogue.",
            f"{model_id}.option_1.text": f"Take {option_relics[0]}",
            f"{model_id}.option_2.text": f"Take {option_relics[1]}",
            f"{model_id}.option_3.text": f"Take {option_relics[2]}",
        }

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Ancients",
            "localization": {"ancients.json": loc},
            "notes": [
                "Requires BaseLib's CustomAncientModel and OptionPools support.",
                "Ancients use option-pool relic choices rather than standard event buttons.",
                "Add dialogue/SFX keys to ancients.json if you want richer presentation.",
            ],
        }

    def generate_orb(
        self,
        mod_namespace: str,
        class_name: str,
        passive_amount: int = 3,
        evoke_amount: int = 9,
        passive_description: str = "",
        evoke_description: str = "",
    ) -> dict:
        """Generate a custom orb class with passive and evoke effects."""
        dyn_vars = [
            f'        new DynamicVar("Passive", {passive_amount}m)',
            f'        new DynamicVar("Evoke", {evoke_amount}m)',
        ]

        passive_body = (
            "        Trigger();\n"
            "        // Passive: triggers each turn end\n"
            "        // TODO: Implement passive orb effect\n"
            "        await Task.CompletedTask;"
        )

        evoke_body = (
            "        // Evoke: triggered when pushed out or manually evoked\n"
            "        // TODO: Implement evoke orb effect\n"
            "        await Task.CompletedTask;\n"
            "        return Array.Empty<Creature>();"
        )

        source = _load_template("orb_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            dynamic_vars=",\n".join(dyn_vars),
            darkened_color="4c667a",
            passive_amount=passive_amount,
            evoke_amount=evoke_amount,
            passive_body=passive_body,
            evoke_body=evoke_body,
            extra_methods="",
        )

        model_id = to_screaming_snake(class_name)
        loc = {
            f"{model_id}.title": class_name.replace("Orb", "").replace("_", " ").strip(),
            f"{model_id}.description": "Passive: {Passive}. Evoke: {Evoke}.",
            f"{model_id}.smartDescription": (
                f"Passive: {passive_description or 'TODO: implement passive effect.'} "
                f"Evoke: {evoke_description or 'TODO: implement evoke effect.'}"
            ),
        }

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Orbs",
            "localization": {"orbs.json": loc},
            "notes": [
                "Channel with: OrbCmd.Channel<YourOrb>(player, choiceContext)",
                "Orbs need a 64x64 icon image.",
                "Test by adding a card that channels this orb.",
            ],
        }

    def generate_enchantment(
        self,
        mod_namespace: str,
        class_name: str,
        trigger_hook: str = "",
        description: str = "",
        hook_signature: dict | None = None,
    ) -> dict:
        """Generate a custom enchantment class."""
        dyn_vars = ['        new DynamicVar("Amount", 1m)']

        hook_methods = ""
        if trigger_hook == "ModifyDamageAdditive":
            hook_methods = """
    public override decimal EnchantDamageAdditive(decimal originalDamage, ValueProp props)
    {
        if (Status != EnchantmentStatus.Normal)
        {
            return 0m;
        }
        return Amount;
    }
"""
        elif trigger_hook == "AfterCardPlayed":
            hook_methods = """
    public override Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
    {
        if (cardPlay.Card != Card)
        {
            return Task.CompletedTask;
        }

        // TODO: Implement enchantment effect when enchanted card is played
        return Task.CompletedTask;
    }
"""
        elif trigger_hook:
            if hook_signature:
                hook_methods = _build_hook_method_stub(
                    hook_signature,
                    "Implement enchantment effect",
                )
            else:
                hook_methods = f"""
    public override async Task {trigger_hook}(/* TODO: add parameters */)
    {{
        // TODO: Implement enchantment effect
        await Task.CompletedTask;
    }}
"""
        else:
            hook_methods = """
    // TODO: Override hook methods. The enchanted card is available as Card.
    // Common hooks: AfterCardPlayed, EnchantDamageAdditive, EnchantPlayCount
"""

        source = _load_template("enchantment_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            dynamic_vars=",\n".join(dyn_vars),
            hook_methods=hook_methods,
        )

        model_id = to_screaming_snake(class_name)
        loc = {
            f"{model_id}.title": class_name.replace("Enchantment", "").replace("_", " ").strip(),
            f"{model_id}.description": description or "TODO: Enchantment description. Use {Amount} for value.",
        }

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Enchantments",
            "localization": {"enchantments.json": loc},
        }

    def generate_game_action(
        self,
        mod_namespace: str,
        class_name: str,
        description: str = "",
        parameters: list[dict] | None = None,
    ) -> dict:
        """Generate a custom GameAction class.

        parameters: list of dicts with keys: name (str), type (str)
        """
        if not parameters:
            parameters = [
                {"name": "amount", "type": "int"},
            ]

        # Build fields, constructor params, constructor body
        field_lines = ["    private readonly ulong _ownerId;"]
        param_parts = ["ulong ownerId"]
        body_lines = ["        _ownerId = ownerId;"]
        enqueue_args: list[str] = []
        for p in parameters:
            pname = p["name"]
            ptype = p["type"]
            if pname == "ownerId":
                continue
            field_name = f"_{pname}" if not pname.startswith("_") else pname
            field_lines.append(f"    private readonly {ptype} {field_name};")
            param_parts.append(f"{ptype} {pname}")
            body_lines.append(f"        {field_name} = {pname};")
            enqueue_args.append(f", {pname}")

        source = _load_template("game_action_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            description=description or f"Custom game action: {class_name}",
            fields="\n".join(field_lines),
            constructor_params=", ".join(param_parts),
            constructor_body="\n".join(body_lines),
            action_type="Any",
            enqueue_args="".join(enqueue_args),
            execute_body="        // TODO: Implement action logic\n        await Task.CompletedTask;",
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Actions",
            "notes": [
                "Enqueue: RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new YourAction(ownerId, ...));",
                "Actions execute on the game action queue, which handles ordering and async properly.",
                "Implement ToNetAction() before using this action in multiplayer or replay-sensitive flows.",
            ],
        }

    def generate_mechanic(
        self,
        mod_namespace: str,
        mod_name: str,
        keyword_name: str,
        keyword_description: str = "",
        sample_card_name: str = "",
        sample_relic_name: str = "",
    ) -> dict:
        """Generate a complete cross-cutting mechanic: keyword, power, sample card, sample relic, and localization."""
        snake = to_snake_case(keyword_name)
        pascal = keyword_name.replace(" ", "").replace("_", "")
        power_name = f"{pascal}Power"
        card_name = sample_card_name or f"{pascal}Strike"
        relic_name = sample_relic_name or f"{pascal}Talisman"
        screaming = to_screaming_snake(keyword_name)

        files = {}

        # 1. Power (tracks the mechanic's stacks)
        power_source = _load_template("power_template").format(
            namespace=mod_namespace,
            class_name=power_name,
            power_type="Buff",
            stack_type="Counter",
            hook_methods=f"""
    public override async Task AfterTurnEnd(CombatState combatState, CombatSide side)
    {{
        if (side != Owner.Side) return;

        Flash();
        // TODO: Implement {keyword_name} turn-end effect based on Amount (stack count)
        await Task.CompletedTask;
    }}
""",
        )
        files[f"Code/Powers/{power_name}.cs"] = power_source

        # 2. Sample card (applies the mechanic)
        card_source = _load_template("card_template").format(
            namespace=mod_namespace,
            class_name=card_name,
            card_type="Attack",
            rarity="Common",
            target_type="AnyEnemy",
            energy_cost=1,
            pool="ColorlessCardPool",
            keywords_block="",
            dynamic_vars=f"                new DamageVar(6M),\n                new MagicNumberVar(2M)",
            on_play_body=f"        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)\n            .FromCard(this, cardPlay)\n            .Execute(choiceContext);\n\n        // Apply {keyword_name} stacks\n        await PowerCmd.Apply<{mod_namespace}.Powers.{power_name}>(Owner.Creature, (int)DynamicVars.MagicNumber.BaseValue, Owner.Creature, this);",
            upgrade_block=f"""
    public override void OnUpgrade()
    {{
        DynamicVars.Damage.Upgrade(3M);
    }}
""",
        )
        files[f"Code/Cards/{card_name}.cs"] = card_source

        # 3. Sample relic (interacts with the mechanic)
        relic_source = _load_template("relic_template").format(
            namespace=mod_namespace,
            class_name=relic_name,
            rarity="Uncommon",
            pool="SharedRelicPool",
            extra_fields="",
            dynamic_vars="                new MagicNumberVar(1M)",
            hook_methods=f"""
    public override async Task AfterCardPlayed(
        CombatState combatState,
        PlayerChoiceContext choiceContext,
        CardPlay cardPlay)
    {{
        if (cardPlay.Card.Owner != Owner) return;

        // Check if the played card has {keyword_name} effect (applies {power_name})
        // Award bonus for using the mechanic
        var creature = Owner.Creature;
        var power = creature.GetPower<{mod_namespace}.Powers.{power_name}>();
        if (power != null && power.Amount >= 3)
        {{
            Flash();
            // TODO: Bonus effect when {keyword_name} stacks >= 3
        }}

        await Task.CompletedTask;
    }}
""",
        )
        files[f"Code/Relics/{relic_name}.cs"] = relic_source

        # 4. Localization
        loc_entries = {
            "cards.json": {
                f"{to_screaming_snake(card_name)}.title": card_name.replace("_", " "),
                f"{to_screaming_snake(card_name)}.description": f"Deal [blue]{{Damage}}[/blue] damage.\\nApply [blue]{{MagicNumber}}[/blue] [gold]{keyword_name}[/gold].",
                f"{to_screaming_snake(card_name)}.upgrade.description": f"Deal [blue]{{Damage}}[/blue] damage.\\nApply [blue]{{MagicNumber}}[/blue] [gold]{keyword_name}[/gold].",
            },
            "relics.json": {
                f"{to_screaming_snake(relic_name)}.title": relic_name.replace("_", " "),
                f"{to_screaming_snake(relic_name)}.description": f"When you have 3+ [gold]{keyword_name}[/gold] stacks, TODO bonus effect.",
                f"{to_screaming_snake(relic_name)}.flavor": f"It hums with {keyword_name.lower()} energy.",
            },
            "powers.json": {
                f"{to_screaming_snake(power_name)}.title": keyword_name,
                f"{to_screaming_snake(power_name)}.smartDescription": keyword_description or f"At end of turn, TODO effect based on [blue]{{Amount}}[/blue] {{Amount:plural:stack|stacks}}.",
                f"{to_screaming_snake(power_name)}.description": keyword_description or f"At end of turn, TODO effect.",
            },
        }

        return {
            "files": files,
            "localization": loc_entries,
            "mechanic_name": keyword_name,
            "components": {
                "power": power_name,
                "sample_card": card_name,
                "sample_relic": relic_name,
            },
            "notes": [
                f"The '{keyword_name}' mechanic consists of a power ({power_name}), a sample card ({card_name}), and a sample relic ({relic_name}).",
                f"The power tracks {keyword_name} stacks. Cards apply stacks, the relic rewards them.",
                "Add more cards that reference the power to build out the mechanic.",
                "Consider adding a custom tooltip patch so the keyword shows a hover tip.",
                f"Test with: card {to_screaming_snake(card_name)}, relic add {to_screaming_snake(relic_name)}",
            ],
        }

    def generate_custom_tooltip(
        self,
        mod_namespace: str,
        tag_name: str,
        title: str,
        tooltip_description: str,
    ) -> dict:
        """Generate a custom keyword/tooltip that appears on hover in card descriptions."""
        class_name = tag_name.replace("_", " ").title().replace(" ", "")
        tooltip_key = to_screaming_snake(tag_name)

        source = _load_template("custom_tooltip_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            tag=tag_name,
            title=_escape_csharp_string(title),
            tooltip_key=tooltip_key,
        )

        return {
            "source": source,
            "file_name": f"{class_name}Tooltip.cs",
            "folder": "Code/Tooltips",
            "localization": {
                "tooltips.json": {
                    "tooltips": {
                        f"{tooltip_key}.title": title,
                        f"{tooltip_key}.description": tooltip_description,
                    }
                }
            },
            "usage": f"Attach via ExtraHoverTips => {class_name}Tooltip.AsSingleTip();",
            "notes": [
                f"This creates a localization-backed hover tip helper for '{title}'.",
                "Use the generated helper from cards, relics, powers, or enchantments that expose ExtraHoverTips.",
                "If you need inline rich-text tags, layer keyword parsing or card-text formatting on top of this helper.",
            ],
        }

    def generate_save_data(
        self,
        mod_namespace: str,
        mod_id: str,
        class_name: str = "ModSaveData",
        fields: list[dict] | None = None,
    ) -> dict:
        """Generate a save data class for persisting mod state.

        fields: list of dicts with keys: name (str), type (str), default (str)
        """
        if not fields:
            fields = [
                {"name": "TotalRuns", "type": "int", "default": "0"},
                {"name": "UnlockedFeatures", "type": "List<string>", "default": "new()"},
            ]

        field_lines = []
        load_lines = []
        save_lines = []
        reset_lines = []

        for f in fields:
            fname = f["name"]
            ftype = f["type"]
            fdefault = f.get("default", "default")

            field_lines.append(f"    public static {ftype} {fname} {{ get; set; }} = {fdefault};")

            # Load
            if ftype == "int":
                load_lines.append(f'                    if (data.TryGetValue("{fname}", out var {fname.lower()}Val)) {fname} = {fname.lower()}Val.GetInt32();')
            elif ftype == "string":
                load_lines.append(f'                    if (data.TryGetValue("{fname}", out var {fname.lower()}Val)) {fname} = {fname.lower()}Val.GetString() ?? {fdefault};')
            elif ftype == "bool":
                load_lines.append(f'                    if (data.TryGetValue("{fname}", out var {fname.lower()}Val)) {fname} = {fname.lower()}Val.GetBoolean();')
            elif ftype == "double":
                load_lines.append(f'                    if (data.TryGetValue("{fname}", out var {fname.lower()}Val)) {fname} = {fname.lower()}Val.GetDouble();')
            else:
                load_lines.append(f'                    // TODO: Deserialize {fname} ({ftype})')

            save_lines.append(f'                {{ "{fname}", {fname} }},')
            reset_lines.append(f"        {fname} = {fdefault};")

        source = _load_template("save_data_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            mod_id=mod_id,
            fields="\n".join(field_lines),
            load_body="\n".join(load_lines),
            save_body="\n".join(save_lines),
            reset_body="\n".join(reset_lines),
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code",
            "notes": [
                f"Call {class_name}.Load() in ModEntry.Init()",
                f"Call {class_name}.Save() whenever data changes",
                f"Data persists to %APPDATA%/.sts2mods/{mod_id}/save_data.json",
                f"Call {class_name}.Reset() to clear saved data",
            ],
        }

    def generate_test_scenario(
        self,
        scenario_name: str,
        relics: list[str] | None = None,
        cards: list[str] | None = None,
        gold: int = 0,
        hp: int = 0,
        powers: list[dict] | None = None,
        fight: str = "",
        event: str = "",
        godmode: bool = False,
    ) -> dict:
        """Generate a console command sequence for setting up a specific test scenario."""
        commands = []

        if godmode:
            commands.append("godmode")
        if gold > 0:
            commands.append(f"gold {gold}")
        if hp > 0:
            commands.append(f"heal {hp}")

        for relic in (relics or []):
            commands.append(f"relic add {to_screaming_snake(relic) if not relic.isupper() else relic}")

        for card in (cards or []):
            commands.append(f"card {to_screaming_snake(card) if not card.isupper() else card}")

        for power in (powers or []):
            power_id = to_screaming_snake(power["name"]) if not power["name"].isupper() else power["name"]
            stacks = power.get("stacks", 1)
            target = power.get("target", 0)
            commands.append(f"power {power_id} {stacks} {target}")

        if fight:
            fight_id = to_screaming_snake(fight) if not fight.isupper() else fight
            commands.append(f"fight {fight_id}")
        elif event:
            event_id = to_screaming_snake(event) if not event.isupper() else event
            commands.append(f"event {event_id}")

        return {
            "scenario": scenario_name,
            "commands": commands,
            "command_count": len(commands),
            "usage": "Execute each command via bridge_console or paste into the in-game console.",
            "combined": "; ".join(commands),
        }

    def generate_vfx_scene(
        self,
        node_name: str,
        particle_count: int = 30,
        lifetime: float = 0.5,
        one_shot: bool = True,
        explosiveness: float = 0.8,
    ) -> dict:
        """Generate a .tscn scene file for combat VFX."""
        scene = _load_template("vfx_scene_template").format(
            node_name=node_name,
            particle_count=particle_count,
            lifetime=lifetime,
            one_shot="true" if one_shot else "false",
            explosiveness=explosiveness,
        )

        snake = to_snake_case(node_name)
        return {
            "scene": scene,
            "file_name": f"{snake}_vfx.tscn",
            "notes": [
                "Place in your mod's asset folder and pack into PCK.",
                "Load in code: var scene = PreloadManager.Cache.GetScene(\"res://ModName/vfx/name_vfx.tscn\");",
                "Instantiate and add to combat scene tree for playback.",
                "Customize particle properties (process_material, texture, etc.) in the Godot editor for best results.",
            ],
        }

    def generate_net_message(
        self,
        mod_namespace: str,
        class_name: str,
        transfer_mode: str = "Reliable",
        should_broadcast: bool = True,
        fields: list[dict] | None = None,
    ) -> dict:
        """Generate a multiplayer network message with serialization."""
        fields = fields or [{"name": "Data", "type": "string"}]

        serialize_map = {
            "string": "        writer.WriteString({name});",
            "int": "        writer.WriteInt({name});",
            "uint": "        writer.WriteUInt({name});",
            "float": "        writer.WriteFloat({name});",
            "bool": "        writer.WriteBool({name});",
            "decimal": "        writer.WriteDouble((double){name});",
            "double": "        writer.WriteDouble({name});",
            "long": "        writer.WriteLong({name});",
            "ulong": "        writer.WriteULong({name});",
        }
        deserialize_map = {
            "string": "        {name} = reader.ReadString();",
            "int": "        {name} = reader.ReadInt();",
            "uint": "        {name} = reader.ReadUInt();",
            "float": "        {name} = reader.ReadFloat();",
            "bool": "        {name} = reader.ReadBool();",
            "decimal": "        {name} = (decimal)reader.ReadDouble();",
            "double": "        {name} = reader.ReadDouble();",
            "long": "        {name} = reader.ReadLong();",
            "ulong": "        {name} = reader.ReadULong();",
        }

        field_decls = []
        ser_lines = []
        deser_lines = []
        for f in fields:
            fname = f["name"]
            ftype = f["type"]
            default_initializer = ' = string.Empty;' if ftype == "string" else ";"
            field_decls.append(f"    public {ftype} {fname}{default_initializer}")
            ser_lines.append(serialize_map.get(ftype, f"        // TODO: serialize {fname}").format(name=fname))
            deser_lines.append(deserialize_map.get(ftype, f"        // TODO: deserialize {fname}").format(name=fname))

        source = _load_template("net_message_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            should_broadcast="true" if should_broadcast else "false",
            transfer_mode=transfer_mode,
            fields_declarations="\n".join(field_decls),
            serialize_body="\n".join(ser_lines),
            deserialize_body="\n".join(deser_lines),
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Networking",
            "notes": [
                "Register handler in ModEntry.Init(): RunManager.Instance.NetService?.RegisterMessageHandler<{cls}>(OnReceived);".format(cls=class_name),
                "Send via: RunManager.Instance.NetService?.SendMessage(new {cls} {{ ... }});".format(cls=class_name),
                f"Transfer mode: {transfer_mode}, Broadcast: {should_broadcast}. Handler registration remains game-specific.",
            ],
        }

    def generate_godot_ui(
        self,
        mod_namespace: str,
        class_name: str,
        title: str = "My Panel",
        base_type: str = "Control",
        controls: list[dict] | None = None,
        show_in_process: bool = False,
    ) -> dict:
        """Generate a Godot UI panel with configurable controls."""
        controls = controls or []

        extra_fields_lines = []
        controls_init_lines = []

        for ctrl in controls:
            ctype = ctrl.get("type", "Label")
            cname = ctrl.get("name", ctype)
            ctext = ctrl.get("text", "")
            field_name = f"_{cname[0].lower()}{cname[1:]}"

            extra_fields_lines.append(f"    private {ctype} {field_name};")

            controls_init_lines.append(f"        {field_name} = new {ctype}();")
            if ctype in ("Label", "Button", "CheckBox"):
                controls_init_lines.append(f'        {field_name}.Text = "{ctext}";')
            if ctype == "Button":
                controls_init_lines.append(f"        {field_name}.Pressed += () => {{ /* TODO: handle click */ }};")
            if ctype == "Slider":
                controls_init_lines.append(f"        {field_name}.MinValue = 0;")
                controls_init_lines.append(f"        {field_name}.MaxValue = 100;")
            controls_init_lines.append(f"        _container.AddChild({field_name});")
            controls_init_lines.append("")

        extra_fields = "\n".join(extra_fields_lines) if extra_fields_lines else ""
        controls_init = "\n".join(controls_init_lines) if controls_init_lines else "        // TODO: Add controls"

        position_setup = '        SetAnchorsPreset(LayoutPreset.TopLeft);\n        OffsetRight = 300;\n        OffsetBottom = 400;'

        if show_in_process:
            process_body = """
    public override void _Process(double delta)
    {{
        // TODO: Update UI each frame
    }}"""
        else:
            process_body = ""

        source = _load_template("godot_ui_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            base_type=base_type,
            title=title,
            extra_fields=extra_fields,
            position_setup=position_setup,
            controls_init=controls_init,
            process_body=process_body,
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/UI",
            "notes": [
                f"Add to scene tree: NGame.Instance.AddChild(new {class_name}());",
                "Or inject via Harmony patch on a scene's _Ready method.",
                f"Base type: {base_type}. Override _Process for dynamic updates.",
            ],
        }

    def generate_settings_panel(
        self,
        mod_namespace: str,
        class_name: str = "ModSettings",
        mod_id: str = "mymod",
        properties: list[dict] | None = None,
    ) -> dict:
        """Generate a settings manager with JSON persistence and optional ModConfig integration."""
        properties = properties or [
            {"name": "Enabled", "type": "bool", "default": "true"},
            {"name": "Intensity", "type": "int", "default": "5"},
        ]

        type_map = {
            "bool": "bool",
            "int": "int",
            "float": "float",
            "string": "string",
        }
        json_getter_map = {
            "bool": "GetBoolean()",
            "int": "GetInt32()",
            "float": "GetSingle()",
            "string": "GetString() ?? {default}",
        }

        settings_fields_lines = []
        load_lines = []
        save_lines = []
        modconfig_lines = []

        for prop in properties:
            pname = prop["name"]
            ptype = type_map.get(prop["type"], prop["type"])
            pdefault = prop["default"]
            settings_fields_lines.append(f"    public static {ptype} {pname} = {pdefault};")

            getter = json_getter_map.get(prop["type"], "GetRawText()").replace("{default}", pdefault)
            load_lines.append(f'            if (data.TryGetValue("{pname}", out var {pname.lower()}Val)) {pname} = {pname.lower()}Val.{getter};')
            save_lines.append(f'                {{ "{pname}", {pname} }},')
            modconfig_lines.append(f'                // register.Invoke(null, new object[] {{ "{mod_id}", "{pname}", {pname} }});')

        source = _load_template("settings_panel_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            mod_id=mod_id,
            settings_fields="\n".join(settings_fields_lines),
            load_body="\n".join(load_lines),
            save_body="\n".join(save_lines),
            modconfig_registration="\n".join(modconfig_lines),
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Config",
            "project_edits": [
                {
                    "type": "ensure_using",
                    "path": "Code/ModEntry.cs",
                    "namespace": f"{mod_namespace}.Config",
                },
                {
                    "type": "insert_text",
                    "path": "Code/ModEntry.cs",
                    "anchor": "        _harmony.PatchAll();\n",
                    "position": "after",
                    "content": f"        {class_name}.Initialize();\n",
                },
            ],
            "notes": [
                f"{class_name}.Initialize() will be inserted into ModEntry.Init() when applied through apply_generated_output.",
                f"Config saved to %APPDATA%/.sts2mods/{mod_id}/config.json",
                "Integrates with ModConfig mod if available (via reflection).",
            ],
        }

    def generate_hover_tip(
        self,
        mod_namespace: str,
        class_name: str = "ModHoverTips",
    ) -> dict:
        """Generate a hover tooltip utility class."""
        source = _load_template("hover_tip_template").format(
            namespace=mod_namespace,
            class_name=class_name,
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/UI",
            "notes": [
                f"Show tooltip: {class_name}.Show(position, \"Title\", \"Body\");",
                f"Attach to node: {class_name}.ShowForNode(control, \"Title\", \"Body\");",
                "Uses the game's built-in HoverTip system.",
            ],
        }

    def generate_overlay(
        self,
        mod_namespace: str,
        class_name: str,
        mod_id: str = "mymod",
        overlay_description: str = "Custom overlay",
        inject_target: str = "NCombatRoom",
    ) -> dict:
        """Generate an overlay control that auto-injects into a game scene via Harmony."""
        source = _load_template("overlay_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            mod_id=mod_id,
            overlay_description=overlay_description,
            inject_target=inject_target,
            patch_target=inject_target,
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Overlays",
            "notes": [
                f"Overlay auto-injects into {inject_target}._Ready via Harmony patch.",
                f"Access singleton: {class_name}.Instance",
                "Modify GetDisplayText() to show your content.",
                "Reposition by changing Anchor/Offset values in _Ready.",
            ],
        }

    def generate_transpiler_patch(
        self,
        mod_namespace: str,
        class_name: str,
        target_type: str,
        target_method: str,
        description: str = "",
        search_opcode: str = "Callvirt",
        search_method: str = "",
        mod_id: str = "mymod",
    ) -> dict:
        """Generate an IL transpiler Harmony patch."""
        source = _load_template("transpiler_patch_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            target_type=target_type,
            target_method=target_method,
            description=description or f"Transpiler patch for {target_type}.{target_method}",
            search_opcode=search_opcode,
            search_method=search_method or target_method,
            mod_id=mod_id,
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Patches",
            "notes": [
                f"Transpiler patches {target_type}.{target_method} at IL level.",
                "Modify the Transpiler method to find the right insertion point.",
                "Implement Modify() with your custom logic.",
                "Use dnSpy or ILSpy to inspect the target method's IL before writing transpilers.",
            ],
        }

    def generate_reflection_accessor(
        self,
        mod_namespace: str,
        class_name: str,
        target_type: str,
        fields: list[dict] | None = None,
    ) -> dict:
        """Generate cached reflection accessors for private members of a target type."""
        fields = fields or [{"name": "health", "type": "int", "is_property": False}]

        accessor_lines = []
        validation_lines = []
        first_field = fields[0]["name"] if fields else "Field"

        for f in fields:
            fname = f["name"]
            ftype = f["type"]
            is_prop = f.get("is_property", False)
            cap_name = fname[0].upper() + fname[1:]

            if is_prop:
                accessor_lines.append(
                    f"    private static readonly PropertyInfo _{fname}Prop = "
                    f"AccessTools.Property(typeof({target_type}), \"{fname}\");"
                )
                accessor_lines.append(
                    f"    public static {ftype} Get{cap_name}({target_type} instance) "
                    f"=> ({ftype})_{fname}Prop.GetValue(instance);"
                )
                accessor_lines.append(
                    f"    public static void Set{cap_name}({target_type} instance, {ftype} value) "
                    f"=> _{fname}Prop.SetValue(instance, value);"
                )
                accessor_lines.append("")
                validation_lines.append(
                    f"        if (_{fname}Prop == null) "
                    f"Log.Warn(\"[{class_name}] Could not find property '{fname}' on {target_type}\");"
                )
            else:
                accessor_lines.append(
                    f"    private static readonly FieldInfo _{fname}Field = "
                    f"AccessTools.Field(typeof({target_type}), \"{fname}\");"
                )
                accessor_lines.append(
                    f"    public static {ftype} Get{cap_name}({target_type} instance) "
                    f"=> ({ftype})_{fname}Field.GetValue(instance);"
                )
                accessor_lines.append(
                    f"    public static void Set{cap_name}({target_type} instance, {ftype} value) "
                    f"=> _{fname}Field.SetValue(instance, value);"
                )
                accessor_lines.append("")
                validation_lines.append(
                    f"        if (_{fname}Field == null) "
                    f"Log.Warn(\"[{class_name}] Could not find field '{fname}' on {target_type}\");"
                )

        first_cap = first_field[0].upper() + first_field[1:]
        source = _load_template("reflection_accessor_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            target_type=target_type,
            first_field=first_cap,
            field_accessors="\n".join(accessor_lines),
            validation="\n".join(validation_lines),
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Utils",
            "notes": [
                f"Provides cached reflection access to {target_type} private members.",
                f"Usage: {class_name}.Get{first_cap}(instance)",
                "All FieldInfo/PropertyInfo are resolved once at class load time.",
            ],
        }

    def generate_custom_keyword(
        self,
        mod_namespace: str,
        keyword_name: str,
    ) -> dict:
        """Generate a custom card keyword using BaseLib's CustomEnum."""
        keyword_field = keyword_name.replace(" ", "")

        source = _load_template("custom_keyword_template").format(
            namespace=mod_namespace,
            keyword_name=keyword_name,
            keyword_field=keyword_field,
        )

        screaming = to_screaming_snake(keyword_field)
        loc = {
            f"{screaming}.title": keyword_name,
            f"{screaming}.description": f"TODO: Describe what {keyword_name} does.",
        }

        return {
            "source": source,
            "file_name": f"{keyword_field}.cs",
            "folder": "Code/Keywords",
            "localization": {"card_keywords.json": loc},
            "notes": [
                f"Keyword '{keyword_name}' registered via BaseLib [CustomEnum].",
                f"Add to cards: Keywords = new HashSet<CardKeyword> {{ {keyword_field}.CustomType }};",
                "Add localization entry for the keyword tooltip.",
            ],
        }

    def generate_custom_pile(
        self,
        mod_namespace: str,
        pile_name: str,
    ) -> dict:
        """Generate a custom card pile type using BaseLib's CustomEnum."""
        class_name = pile_name.replace(" ", "") + "Pile"

        source = _load_template("custom_pile_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            pile_name=pile_name,
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Piles",
            "notes": [
                f"Custom pile '{pile_name}' registered via BaseLib [CustomEnum].",
                f"Access: {class_name}.CustomType",
                "Route cards by patching GetResultPileType or card destination logic.",
            ],
        }

    def generate_spire_field(
        self,
        mod_namespace: str,
        class_name: str,
        target_type: str,
        field_name: str = "Value",
        field_type: str = "int",
        default_value: str = "0",
    ) -> dict:
        """Generate a SpireField to attach custom data to existing game objects."""
        source = _load_template("spire_field_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            target_type=target_type,
            field_name=field_name,
            field_type=field_type,
            default_value=default_value,
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Fields",
            "notes": [
                f"Attaches a {field_type} '{field_name}' to {target_type} instances.",
                f"Get: {class_name}.{field_name}.Get(instance)",
                f"Set: {class_name}.{field_name}.Set(instance, value)",
                "Requires BaseLib's SpireField utility.",
            ],
        }

    def generate_dynamic_var(
        self,
        mod_namespace: str,
        class_name: str,
        var_name: str,
        default_value: int = 0,
    ) -> dict:
        """Generate a custom DynamicVar for card/power descriptions."""
        source = _load_template("dynamic_var_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            var_name=var_name,
            default_value=default_value,
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Vars",
            "notes": [
                f"Dynamic variable '{var_name}' for use in localization strings.",
                f"Usage in localization: {{{{{var_name}}}}}",
                f"Add to CanonicalVars: new {class_name}({default_value})",
            ],
        }

    def generate_modifier(
        self,
        mod_namespace: str,
        class_name: str,
        modifier_type: str = "Bad",
        description: str = "",
        hook: str = "",
    ) -> dict:
        """Generate a custom run modifier (Good or Bad) with registration patch and localization.

        modifier_type: 'Good' or 'Bad'
        hook: Primary lifecycle hook. Options:
          - 'TryModifyRewardsLate' - Modify combat rewards (add/remove/replace)
          - 'AfterRunCreated' - Setup when a new run starts
          - 'AfterRunLoaded' - Re-apply when a saved run is loaded
          - 'ModifyCardRewardCreationOptions' - Filter card reward pools
          - 'ModifyMerchantCardPool' - Filter shop card offerings
          - 'GenerateNeowOption' - Custom Neow (first event) option
        """
        modifier_list = "GoodModifiers" if modifier_type == "Good" else "BadModifiers"
        model_id = to_screaming_snake(class_name)

        # Build modifier body based on hook
        body_lines = []
        if hook == "TryModifyRewardsLate":
            body_lines = [
                "    public override bool TryModifyRewardsLate(Player player, List<Reward> rewards, AbstractRoom? room)",
                "    {",
                "        if (room is not CombatRoom combatRoom)",
                "            return false;",
                "",
                "        // TODO: Modify the rewards list. Return true if modified.",
                "        return false;",
                "    }",
            ]
        elif hook == "AfterRunCreated":
            body_lines = [
                "    protected override void AfterRunCreated(RunState runState)",
                "    {",
                "        foreach (var player in runState.Players)",
                "        {",
                "            // TODO: Modify player state at run start",
                "        }",
                "    }",
                "",
                "    protected override void AfterRunLoaded(RunState runState)",
                "    {",
                "        // Re-apply any state that needs to persist across save/load",
                "    }",
            ]
        elif hook == "ModifyCardRewardCreationOptions":
            body_lines = [
                "    public override CardCreationOptions ModifyCardRewardCreationOptions(",
                "        Player player, CardCreationOptions options)",
                "    {",
                "        if (options.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications))",
                "            return options;",
                "",
                "        // TODO: Return options.WithCustomPool(yourFilteredCards);",
                "        return options;",
                "    }",
            ]
        elif hook == "ModifyMerchantCardPool":
            body_lines = [
                "    public override IEnumerable<CardModel> ModifyMerchantCardPool(",
                "        Player player, IEnumerable<CardModel> options)",
                "    {",
                "        // TODO: Filter or replace the merchant card pool",
                "        return options;",
                "    }",
            ]
        elif hook == "GenerateNeowOption":
            body_lines = [
                "    public override Func<Task> GenerateNeowOption(EventModel eventModel)",
                "    {",
                "        return () =>",
                "        {",
                "            var player = eventModel.Owner!;",
                "            // TODO: Apply modifier effect at Neow",
                "            return Task.CompletedTask;",
                "        };",
                "    }",
            ]
        else:
            body_lines = [
                "    // TODO: Override ModifierModel lifecycle methods:",
                "    //   TryModifyRewardsLate(Player, List<Reward>, AbstractRoom?) - modify combat rewards",
                "    //   AfterRunCreated(RunState) - setup at run start",
                "    //   AfterRunLoaded(RunState) - re-apply on save load",
                "    //   ModifyCardRewardCreationOptions(Player, CardCreationOptions) - filter card pools",
                "    //   ModifyMerchantCardPool(Player, IEnumerable<CardModel>) - filter shop",
                "    //   GenerateNeowOption(EventModel) - custom Neow option",
            ]

        source = _load_template("modifier_template").format(
            namespace=mod_namespace,
            class_name=class_name,
            description=description or f"Custom {modifier_type.lower()} modifier: {class_name}",
            body="\n".join(body_lines) + "\n",
        )

        # Registration patch
        reg_source = _load_template("modifier_registration_patch").format(
            namespace=mod_namespace,
            class_name=class_name,
            modifier_type=modifier_type.lower(),
            modifier_list=modifier_list,
            modifier_full_type=f"{mod_namespace}.Modifiers.{class_name}",
        )

        # Localization patch
        loc_entries = (
            f'                {{"{model_id}.title", "{class_name}"}},\n'
            f'                {{"{model_id}.description", "{description or "TODO: Modifier description."}"}},\n'
        )
        loc_source = _load_template("modifier_loc_patch").format(
            namespace=mod_namespace,
            loc_entries=loc_entries,
        )

        return {
            "source": source,
            "file_name": f"{class_name}.cs",
            "folder": "Code/Modifiers",
            "extra_files": [
                {
                    "source": reg_source,
                    "file_name": f"{class_name}RegistrationPatch.cs",
                    "folder": "Code/Patches",
                },
                {
                    "source": loc_source,
                    "file_name": "ModifierLocPatch.cs",
                    "folder": "Code/Patches",
                },
            ],
            "localization": {
                "modifiers.json": {
                    f"{model_id}.title": class_name,
                    f"{model_id}.description": description or "TODO: Modifier description.",
                }
            },
            "notes": [
                f"This is a {modifier_type} modifier — appears in the custom run modifier selection screen.",
                f"Registration patch adds it to ModelDb.{modifier_list} at runtime.",
                "The LocManager patch injects translations that persist across language changes.",
                "Add 'Code/Modifiers' to your project subdirectories if it doesn't exist.",
                f"Test in-game: Start a custom run and look for '{class_name}' in the {modifier_type.lower()} modifiers list.",
            ],
        }
