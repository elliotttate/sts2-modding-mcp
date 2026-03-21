"""Game data indexing and querying for Slay the Spire 2 decompiled source."""

import json
import os
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Optional

# Map namespace directories to entity types
NAMESPACE_TO_TYPE = {
    "MegaCrit.Sts2.Core.Models.Cards": "card",
    "MegaCrit.Sts2.Core.Models.Cards.Mocks": "card_mock",
    "MegaCrit.Sts2.Core.Models.Relics": "relic",
    "MegaCrit.Sts2.Core.Models.Potions": "potion",
    "MegaCrit.Sts2.Core.Models.Powers": "power",
    "MegaCrit.Sts2.Core.Models.Powers.Mocks": "power_mock",
    "MegaCrit.Sts2.Core.Models.Monsters": "monster",
    "MegaCrit.Sts2.Core.Models.Monsters.Mocks": "monster_mock",
    "MegaCrit.Sts2.Core.Models.Encounters": "encounter",
    "MegaCrit.Sts2.Core.Models.Encounters.Mocks": "encounter_mock",
    "MegaCrit.Sts2.Core.Models.Events": "event",
    "MegaCrit.Sts2.Core.Models.Events.Mocks": "event_mock",
    "MegaCrit.Sts2.Core.Models.Enchantments": "enchantment",
    "MegaCrit.Sts2.Core.Models.Enchantments.Mocks": "enchantment_mock",
    "MegaCrit.Sts2.Core.Models.Afflictions": "affliction",
    "MegaCrit.Sts2.Core.Models.Afflictions.Mocks": "affliction_mock",
    "MegaCrit.Sts2.Core.Models.Characters": "character",
    "MegaCrit.Sts2.Core.Models.CardPools": "card_pool",
    "MegaCrit.Sts2.Core.Models.RelicPools": "relic_pool",
    "MegaCrit.Sts2.Core.Models.PotionPools": "potion_pool",
    "MegaCrit.Sts2.Core.Models.Acts": "act",
    "MegaCrit.Sts2.Core.Models.Modifiers": "modifier",
    "MegaCrit.Sts2.Core.Models.Orbs": "orb",
    "MegaCrit.Sts2.Core.Models.Singleton": "singleton",
    "MegaCrit.Sts2.Core.Models": "model_base",
    "MegaCrit.Sts2.Core.GameActions": "game_action",
    "MegaCrit.Sts2.Core.GameActions.Multiplayer": "game_action_mp",
    "MegaCrit.Sts2.Core.Hooks": "hooks",
    "MegaCrit.Sts2.Core.Modding": "modding",
    "MegaCrit.Sts2.Core.Combat": "combat",
    "MegaCrit.Sts2.Core.Combat.History": "combat_history",
    "MegaCrit.Sts2.Core.Combat.History.Entries": "combat_history_entry",
    "MegaCrit.Sts2.Core.DevConsole.ConsoleCommands": "console_command",
    "MegaCrit.Sts2.Core.DevConsole": "dev_console",
    "MegaCrit.Sts2.Core.Entities.Cards": "card_entity",
    "MegaCrit.Sts2.Core.Entities.Creatures": "creature_entity",
    "MegaCrit.Sts2.Core.Entities.Players": "player_entity",
    "MegaCrit.Sts2.Core.Entities.Powers": "power_entity",
    "MegaCrit.Sts2.Core.Entities.Relics": "relic_entity",
    "MegaCrit.Sts2.Core.Entities.Potions": "potion_entity",
    "MegaCrit.Sts2.Core.Entities.Orbs": "orb_entity",
    "MegaCrit.Sts2.Core.Entities.Enchantments": "enchantment_entity",
    "MegaCrit.Sts2.Core.Entities.Actions": "action_entity",
    "MegaCrit.Sts2.Core.Entities.Intents": "intent",
    "MegaCrit.Sts2.Core.Entities.Models": "entity_model",
    "MegaCrit.Sts2.Core.Entities.Ancients": "ancient",
    "MegaCrit.Sts2.Core.Entities.Gold": "gold",
    "MegaCrit.Sts2.Core.Entities.Merchant": "merchant",
    "MegaCrit.Sts2.Core.Entities.Multiplayer": "entity_mp",
    "MegaCrit.Sts2.Core.Entities.Text": "entity_text",
    "MegaCrit.Sts2.Core.Entities.UI": "entity_ui",
    "MegaCrit.Sts2.Core.Entities.Rewards": "entity_reward",
    "MegaCrit.Sts2.Core.Entities.RestSite": "rest_site",
    "MegaCrit.Sts2.Core.Entities.Ascension": "ascension",
    "MegaCrit.Sts2.Core.Entities.CardRewardAlternatives": "card_reward_alt",
    "MegaCrit.Sts2.Core.Entities.TreasureRelicPicking": "treasure_relic",
    "MegaCrit.Sts2.Core.Factories": "factory",
    "MegaCrit.Sts2.Core.Commands": "command",
    "MegaCrit.Sts2.Core.Commands.Builders": "command_builder",
    "MegaCrit.Sts2.Core.Runs": "run",
    "MegaCrit.Sts2.Core.Runs.History": "run_history",
    "MegaCrit.Sts2.Core.Runs.Metrics": "run_metrics",
    "MegaCrit.Sts2.Core.Context": "context",
    "MegaCrit.Sts2.Core.Logging": "logging",
    "MegaCrit.Sts2.Core.Extensions": "extensions",
    "MegaCrit.Sts2.Core.Localization": "localization",
    "MegaCrit.Sts2.Core.Localization.DynamicVars": "dynamic_var",
    "MegaCrit.Sts2.Core.Localization.Formatters": "loc_formatter",
    "MegaCrit.Sts2.Core.Settings": "settings",
    "MegaCrit.Sts2.Core.Saves": "saves",
    "MegaCrit.Sts2.Core.Saves.Managers": "save_manager",
    "MegaCrit.Sts2.Core.Saves.Runs": "save_run",
    "MegaCrit.Sts2.Core.Map": "map",
    "MegaCrit.Sts2.Core.Rooms": "room",
    "MegaCrit.Sts2.Core.Rewards": "reward",
    "MegaCrit.Sts2.Core.MonsterMoves": "monster_move",
    "MegaCrit.Sts2.Core.MonsterMoves.Intents": "monster_intent",
    "MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine": "move_state_machine",
    "MegaCrit.Sts2.Core.Random": "random",
    "MegaCrit.Sts2.Core.Odds": "odds",
    "MegaCrit.Sts2.Core.Events": "event_system",
    "MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent": "crystal_sphere_event",
    "MegaCrit.Sts2.Core.Helpers": "helper",
    "MegaCrit.Sts2.Core.Helpers.Models": "helper_model",
    "MegaCrit.Sts2.Core.HoverTips": "hover_tip",
    "MegaCrit.Sts2.Core.ValueProps": "value_prop",
    "MegaCrit.Sts2.Core.RichTextTags": "rich_text",
    "MegaCrit.Sts2.Core.TextEffects": "text_effect",
    "MegaCrit.Sts2.Core.CardSelection": "card_selection",
    "MegaCrit.Sts2.Core.Unlocks": "unlock",
    "MegaCrit.Sts2.Core.Timeline": "timeline",
    "MegaCrit.Sts2.Core.Timeline.Epochs": "epoch",
    "MegaCrit.Sts2.Core.Timeline.Stories": "story",
    "MegaCrit.Sts2.Core.Achievements": "achievement",
    "MegaCrit.Sts2.Core.Daily": "daily",
    "MegaCrit.Sts2.Core.Platform": "platform",
    "MegaCrit.Sts2.Core.Platform.Steam": "platform_steam",
    "MegaCrit.Sts2.Core.Animation": "animation",
    "MegaCrit.Sts2.Core.Assets": "assets",
    "MegaCrit.Sts2.Core.Audio": "audio",
    "MegaCrit.Sts2.Core.AutoSlay": "autoslay",
    "MegaCrit.Sts2.Core.Debug": "debug",
    "MegaCrit.Sts2.Core.Exceptions": "exception",
    "MegaCrit.Sts2.Core.ControllerInput": "controller_input",
    "MegaCrit.Sts2.Core.Leaderboard": "leaderboard",
    "MegaCrit.Sts2.Core.Multiplayer": "multiplayer",
    "MegaCrit.Sts2.Core.Nodes": "node",
}

# Primary entity types that modders care about
PRIMARY_ENTITY_TYPES = {
    "card", "relic", "potion", "power", "monster", "encounter",
    "event", "enchantment", "affliction", "character", "orb",
    "card_pool", "relic_pool", "potion_pool", "act",
}


@dataclass
class EntityInfo:
    name: str
    entity_type: str
    namespace: str
    file_path: str
    base_class: str = ""
    properties: dict = field(default_factory=dict)


class GameDataIndex:
    def __init__(self, decompiled_path: str):
        self.decompiled_path = Path(decompiled_path)
        self.entities: dict[str, EntityInfo] = {}
        self.by_type: dict[str, list[EntityInfo]] = {}
        self.all_files: dict[str, Path] = {}  # class_name -> file_path for ALL files
        self.hooks: list[dict] = []
        self.overridable_hooks: set[str] = set()  # AbstractModel virtual methods modders override
        self.console_commands: list[dict] = []
        # Roslyn-derived structured data (available when roslyn_index.json is loaded)
        self.roslyn_classes: dict = {}
        self.roslyn_enums: dict = {}
        self._type_ref_index: dict[str, list[str]] = {}   # type_name -> [classes that reference it]
        self._method_override_index: dict[str, list[str]] = {}  # method_name -> [classes that override it]
        self._roslyn_loaded = False
        self._indexed = False

    def ensure_indexed(self):
        if not self._indexed:
            self._build_index()
            self._indexed = True

    def _build_index(self):
        if not self.decompiled_path.exists():
            raise FileNotFoundError(f"Decompiled source not found at {self.decompiled_path}")

        index_path = self.decompiled_path / "roslyn_index.json"

        # Auto-build Roslyn index if missing or stale
        if not index_path.exists() or self._is_index_stale(index_path):
            self._auto_build_roslyn_index(index_path)

        # Try loading the Roslyn index
        if index_path.exists():
            try:
                self._build_from_roslyn(index_path)
                return
            except Exception as e:
                print(f"[sts2mcp] Failed to load Roslyn index, falling back to regex: {e}", file=sys.stderr)

        self._build_from_regex()

    def _is_index_stale(self, index_path: Path) -> bool:
        """Check if roslyn_index.json is older than the decompiled source."""
        # Use sts2.csproj as a marker — it's always regenerated during decompilation
        marker = self.decompiled_path / "sts2.csproj"
        if marker.exists():
            return marker.stat().st_mtime > index_path.stat().st_mtime
        return False

    def _auto_build_roslyn_index(self, index_path: Path) -> bool:
        """Build and run the Roslyn analyzer to generate the index. Returns True on success."""
        if not shutil.which("dotnet"):
            print("[sts2mcp] dotnet not found, skipping Roslyn index build (using regex fallback)", file=sys.stderr)
            return False

        analyzer_dir = Path(__file__).parent.parent / "tools" / "roslyn_analyzer"
        if not (analyzer_dir / "RoslynAnalyzer.csproj").exists():
            return False

        dll_path = analyzer_dir / "bin" / "Release" / "net9.0" / "RoslynAnalyzer.dll"

        # Build if DLL doesn't exist
        if not dll_path.exists():
            print("[sts2mcp] Building Roslyn analyzer (one-time)...", file=sys.stderr)
            nuget_config = analyzer_dir / "nuget.config"
            restore_cmd = ["dotnet", "restore"]
            if nuget_config.exists():
                restore_cmd += ["--configfile", str(nuget_config)]
            try:
                result = subprocess.run(
                    restore_cmd, cwd=str(analyzer_dir),
                    capture_output=True, text=True, timeout=120,
                )
                if result.returncode != 0:
                    print(f"[sts2mcp] Roslyn restore failed: {result.stderr[:200]}", file=sys.stderr)
                    return False
                result = subprocess.run(
                    ["dotnet", "build", "-c", "Release", "--no-restore"],
                    cwd=str(analyzer_dir),
                    capture_output=True, text=True, timeout=120,
                )
                if result.returncode != 0:
                    print(f"[sts2mcp] Roslyn build failed: {result.stderr[:200]}", file=sys.stderr)
                    return False
            except (subprocess.TimeoutExpired, OSError) as e:
                print(f"[sts2mcp] Roslyn build error: {e}", file=sys.stderr)
                return False

        # Run analyzer
        print("[sts2mcp] Generating Roslyn index (this takes ~5s)...", file=sys.stderr)
        try:
            result = subprocess.run(
                ["dotnet", str(dll_path), str(self.decompiled_path), str(index_path)],
                capture_output=True, text=True, timeout=300,
            )
            if result.returncode == 0:
                print(f"[sts2mcp] Roslyn index ready: {result.stdout.strip()}", file=sys.stderr)
                return True
            else:
                print(f"[sts2mcp] Roslyn analyzer failed: {result.stderr[:200]}", file=sys.stderr)
                return False
        except (subprocess.TimeoutExpired, OSError) as e:
            print(f"[sts2mcp] Roslyn analyzer error: {e}", file=sys.stderr)
            return False

    # ── Roslyn-based indexing ─────────────────────────────────────────────────

    def _build_from_roslyn(self, index_path: Path):
        with open(index_path, encoding="utf-8") as f:
            data = json.load(f)

        self.roslyn_classes = data.get("classes", {})
        self.roslyn_enums = data.get("enums", {})
        self._roslyn_loaded = True

        for class_name, cls_data in self.roslyn_classes.items():
            namespace = cls_data.get("namespace", "")
            entity_type = self._resolve_entity_type(namespace)
            file_rel = cls_data.get("file", "")
            file_path = str(self.decompiled_path / file_rel)

            # Build all_files index (stem -> path)
            file_stem = Path(file_rel).stem
            self.all_files[file_stem] = Path(file_path)
            # Also index by class name if different from stem
            if class_name != file_stem:
                self.all_files[class_name] = Path(file_path)

            props = self._extract_roslyn_properties(cls_data, file_path)

            info = EntityInfo(
                name=class_name,
                entity_type=entity_type,
                namespace=namespace,
                file_path=file_path,
                base_class=cls_data.get("base_class") or "",
                properties=props,
            )
            self.entities[class_name] = info
            self.by_type.setdefault(entity_type, []).append(info)

        self._extract_roslyn_hooks()
        self._extract_roslyn_overridable_hooks()
        self._extract_roslyn_console_commands()
        self._build_inverted_indexes()

    def _extract_roslyn_properties(self, cls_data: dict, file_path: str) -> dict:
        """Extract game-domain properties from Roslyn class data."""
        props: dict = {}

        # From constructor base args (CardModel pattern: base(cost, type, rarity, target))
        base_args = cls_data.get("constructor_base_args", [])
        for arg in base_args:
            if m := re.match(r"CardType\.(\w+)", arg):
                props["card_type"] = m.group(1)
            elif m := re.match(r"(?:Card|Relic|Potion)Rarity\.(\w+)", arg):
                props["rarity"] = m.group(1)
            elif m := re.match(r"TargetType\.(\w+)", arg):
                props["target_type"] = m.group(1)

        # Energy cost: first plain integer arg for CardModel subclasses
        if cls_data.get("base_class") == "CardModel" and base_args:
            if re.match(r"^\d+$", base_args[0]):
                props["energy_cost"] = base_args[0]

        # From expression-bodied properties
        for prop in cls_data.get("properties", []):
            name = prop.get("name", "")
            expr = prop.get("expression_value")
            if not expr:
                continue

            if name == "Type":
                if m := re.match(r"CardType\.(\w+)", expr):
                    props["card_type"] = m.group(1)
                elif m := re.match(r"PowerType\.(\w+)", expr):
                    props["power_type"] = m.group(1)
            elif name == "Rarity":
                if m := re.match(r"(?:Card|Relic|Potion)Rarity\.(\w+)", expr):
                    props["rarity"] = m.group(1)
            elif name == "TargetType":
                if m := re.match(r"TargetType\.(\w+)", expr):
                    props["target_type"] = m.group(1)
            elif name == "EnergyCost" and re.match(r"^\d+$", expr):
                props["energy_cost"] = expr
            elif name == "StackType":
                if m := re.match(r"PowerStackType\.(\w+)", expr):
                    props["stack_type"] = m.group(1)
            elif name == "MinInitialHp":
                if m := re.search(r"(\d+)", expr):
                    props["min_hp"] = m.group(1)
            elif name == "MaxInitialHp":
                if m := re.search(r"(\d+)", expr):
                    props["max_hp"] = m.group(1)
            elif name == "RoomType":
                if m := re.match(r"RoomType\.(\w+)", expr):
                    props["room_type"] = m.group(1)
            elif name == "Usage":
                if m := re.match(r"PotionUsage\.(\w+)", expr):
                    props["usage"] = m.group(1)

        # Pool from [Pool(typeof(X))] attribute
        for attr in cls_data.get("attributes", []):
            if attr.get("name") == "Pool" and attr.get("args"):
                if m := re.match(r"typeof\((\w+)\)", attr["args"][0]):
                    props["pool"] = m.group(1)

        # Keywords: scan source for CardKeyword.X references
        if "CardKeyword" in cls_data.get("type_references", []):
            source = self._read_file(file_path)
            if source:
                keywords = re.findall(r"CardKeyword\.(\w+)", source)
                if keywords:
                    props["keywords"] = list(set(keywords))

        return props

    def _extract_roslyn_hooks(self):
        """Extract hooks from the Hook class in Roslyn data."""
        hook_cls = self.roslyn_classes.get("Hook")
        if not hook_cls or hook_cls.get("namespace") != "MegaCrit.Sts2.Core.Hooks":
            return

        skip_methods = {"IterateHookListeners", "ModifyDamageInternal"}

        for method in hook_cls.get("methods", []):
            name = method["name"]
            if name in skip_methods:
                continue
            if "static" not in method.get("modifiers", []):
                continue

            params_str = ", ".join(
                f"{p['type']} {p['name']}" for p in method.get("parameters", [])
            )
            ret = method["return_type"]

            category = "event"
            if name.startswith("Before"):
                category = "before"
            elif name.startswith("After"):
                category = "after"
            elif name.startswith("Modify"):
                category = "modify"
            elif name.startswith("Should"):
                category = "should"
            elif name.startswith("Try"):
                category = "try"

            self.hooks.append({
                "name": name,
                "return_type": ret,
                "params": params_str,
                "category": category,
                "subcategory": self._classify_hook(name),
            })

    def _extract_roslyn_overridable_hooks(self):
        """Extract AbstractModel virtual methods that modders override (the actual hook interface)."""
        am = self.roslyn_classes.get("AbstractModel")
        if not am:
            return
        for method in am.get("methods", []):
            if "virtual" in method.get("modifiers", []):
                self.overridable_hooks.add(method["name"])

    def _extract_roslyn_console_commands(self):
        """Extract console commands from Roslyn data."""
        for class_name, cls_data in self.roslyn_classes.items():
            if cls_data.get("namespace") != "MegaCrit.Sts2.Core.DevConsole.ConsoleCommands":
                continue

            cmd_name = ""
            description = ""
            args = ""
            is_networked = False

            for prop in cls_data.get("properties", []):
                pname = prop.get("name", "")
                expr = prop.get("expression_value", "") or ""

                if pname == "CmdName":
                    if m := re.match(r'"([^"]*)"', expr):
                        cmd_name = m.group(1)
                elif pname == "Description":
                    if m := re.match(r'"([^"]*)"', expr):
                        description = m.group(1)
                elif pname == "Args":
                    if m := re.match(r'"([^"]*)"', expr):
                        args = m.group(1)
                elif pname == "IsNetworked" and expr == "true":
                    is_networked = True

            if not cmd_name:
                continue

            self.console_commands.append({
                "name": cmd_name,
                "description": description,
                "args": args,
                "is_networked": is_networked,
                "file": Path(cls_data.get("file", "")).name,
            })

    def _build_inverted_indexes(self):
        """Build inverted indexes for fast type-ref and override lookups."""
        for class_name, cls_data in self.roslyn_classes.items():
            for ref in cls_data.get("type_references", []):
                self._type_ref_index.setdefault(ref, []).append(class_name)
            for method in cls_data.get("methods", []):
                if "override" in method.get("modifiers", []):
                    self._method_override_index.setdefault(method["name"], []).append(class_name)

    # ── Smart search (Roslyn-accelerated with regex fallback) ─────────────────

    def search_code_smart(self, pattern: str, max_results: int = 50) -> list[dict]:
        """Search game code using Roslyn indexes when possible, regex fallback otherwise."""
        self.ensure_indexed()

        if not self._roslyn_loaded:
            return self.search_code(pattern, max_results)

        # Check if pattern is a simple identifier (no regex metacharacters)
        is_simple_id = bool(re.match(r"^[A-Z]\w+$", pattern))

        if is_simple_id:
            # Type reference lookup (who references this type?)
            if pattern in self._type_ref_index:
                return self._search_by_type_ref(pattern, max_results)

            # Override lookup (who overrides this method?)
            if pattern in self._method_override_index:
                return self._search_by_override(pattern, max_results)

            # Class name lookup (find this class + who uses it via invocations)
            if pattern in self.roslyn_classes:
                return self._search_by_class_usage(pattern, max_results)

        # Override pattern: "override.*MethodName"
        m = re.match(r"override\s*\.?\*?\s*(\w+)", pattern)
        if m and m.group(1) in self._method_override_index:
            return self._search_by_override(m.group(1), max_results)

        # "Class.Method" pattern → find callers via invocation index
        m = re.match(r"^(\w+)\.(\w+)$", pattern)
        if m:
            results = self._search_by_invocation(f"{m.group(1)}.{m.group(2)}", max_results)
            if results:
                return results

        # Fallback: full-text regex
        return self.search_code(pattern, max_results)

    def _search_by_type_ref(self, type_name: str, max_results: int) -> list[dict]:
        """Find all classes that reference a given type (O(1) index lookup)."""
        classes = self._type_ref_index.get(type_name, [])
        results = []
        for cls_name in classes[:max_results]:
            cls_data = self.roslyn_classes.get(cls_name, {})
            file_rel = cls_data.get("file", "")
            base = cls_data.get("base_class", "")
            entity = self.entities.get(cls_name)
            etype = entity.entity_type if entity else ""
            results.append({
                "file": file_rel,
                "line": 1,
                "content": f"class {cls_name} : {base}  // references {type_name} [{etype}]",
            })
        return results

    def _search_by_override(self, method_name: str, max_results: int) -> list[dict]:
        """Find all classes that override a given method (O(1) index lookup)."""
        classes = self._method_override_index.get(method_name, [])
        results = []
        for cls_name in classes[:max_results]:
            cls_data = self.roslyn_classes.get(cls_name, {})
            file_rel = cls_data.get("file", "")
            # Find the actual method for line number and signature
            for method in cls_data.get("methods", []):
                if method["name"] == method_name and "override" in method.get("modifiers", []):
                    params = ", ".join(f"{p['type']} {p['name']}" for p in method.get("parameters", []))
                    results.append({
                        "file": file_rel,
                        "line": method.get("line_start", 1),
                        "content": f"override {method['return_type']} {method_name}({params})",
                    })
                    break
            if len(results) >= max_results:
                break
        return results

    def _search_by_class_usage(self, class_name: str, max_results: int) -> list[dict]:
        """Find a class definition + who calls its methods (via invocation scan)."""
        cls_data = self.roslyn_classes.get(class_name, {})
        results = []

        # First result: the class itself
        file_rel = cls_data.get("file", "")
        base = cls_data.get("base_class", "")
        method_names = [m["name"] for m in cls_data.get("methods", [])[:5]]
        results.append({
            "file": file_rel,
            "line": 1,
            "content": f"class {class_name} : {base}  // methods: {', '.join(method_names)}",
        })

        # Find classes that invoke ClassName.* in their methods
        prefix = f"{class_name}."
        for other_name, other_data in self.roslyn_classes.items():
            if other_name == class_name:
                continue
            for method in other_data.get("methods", []):
                for inv in method.get("invocations", []):
                    if inv.startswith(prefix):
                        results.append({
                            "file": other_data.get("file", ""),
                            "line": method.get("line_start", 1),
                            "content": f"{other_name}.{method['name']} calls {inv}",
                        })
                        break  # one match per method is enough
                if len(results) >= max_results:
                    break
            if len(results) >= max_results:
                break

        return results

    def _search_by_invocation(self, call_target: str, max_results: int) -> list[dict]:
        """Find all methods that invoke a specific Class.Method target."""
        results = []
        for class_name, cls_data in self.roslyn_classes.items():
            for method in cls_data.get("methods", []):
                if call_target in method.get("invocations", []):
                    results.append({
                        "file": cls_data.get("file", ""),
                        "line": method.get("line_start", 1),
                        "content": f"{class_name}.{method['name']} calls {call_target}",
                    })
                    if len(results) >= max_results:
                        return results
        return results

    # ── Regex-based indexing (fallback) ───────────────────────────────────────

    def _build_from_regex(self):
        for entry in sorted(self.decompiled_path.iterdir()):
            if not entry.is_dir():
                continue

            namespace = entry.name
            entity_type = self._resolve_entity_type(namespace)

            for cs_file in sorted(entry.glob("*.cs")):
                # Index ALL files for source lookup
                stem = cs_file.stem
                self.all_files[stem] = cs_file

                info = self._parse_cs_file(cs_file, namespace, entity_type)
                if info:
                    self.entities[info.name] = info
                    self.by_type.setdefault(entity_type, []).append(info)

        self._parse_hooks()
        self._parse_overridable_hooks()
        self._parse_console_commands()

    def _resolve_entity_type(self, namespace: str) -> str:
        if namespace in NAMESPACE_TO_TYPE:
            return NAMESPACE_TO_TYPE[namespace]
        # Try prefix match (longest first)
        best_match = ""
        best_type = "other"
        for ns_prefix, etype in NAMESPACE_TO_TYPE.items():
            if namespace.startswith(ns_prefix) and len(ns_prefix) > len(best_match):
                best_match = ns_prefix
                best_type = etype
        return best_type

    def _parse_cs_file(self, file_path: Path, namespace: str, entity_type: str) -> Optional[EntityInfo]:
        try:
            content = file_path.read_text(encoding="utf-8-sig")
        except Exception:
            return None

        class_match = re.search(
            r"(?:public\s+)?(?:sealed\s+)?(?:abstract\s+)?(?:static\s+)?class\s+(\w+)(?:\s*:\s*([^\s{,]+))?",
            content,
        )
        if not class_match:
            return None

        class_name = class_match.group(1)
        base_class = class_match.group(2) or ""
        props: dict = {}

        # Card properties
        m = re.search(r"override\s+CardType\s+Type\s*=>\s*CardType\.(\w+)", content)
        if m:
            props["card_type"] = m.group(1)
        m = re.search(r"override\s+(?:Card|Relic|Potion)Rarity\s+Rarity\s*=>\s*\w+Rarity\.(\w+)", content)
        if m:
            props["rarity"] = m.group(1)
        m = re.search(r"override\s+TargetType\s+TargetType\s*=>\s*TargetType\.(\w+)", content)
        if m:
            props["target_type"] = m.group(1)
        m = re.search(r"override\s+CardEnergyCost\s+EnergyCost\s*=>\s*(\d+)", content)
        if m:
            props["energy_cost"] = m.group(1)

        # Power properties
        m = re.search(r"override\s+PowerType\s+Type\s*=>\s*PowerType\.(\w+)", content)
        if m:
            props["power_type"] = m.group(1)
        m = re.search(r"override\s+PowerStackType\s+StackType\s*=>\s*PowerStackType\.(\w+)", content)
        if m:
            props["stack_type"] = m.group(1)

        # Monster HP
        m = re.search(r"override\s+int\s+MinInitialHp\s*=>\s*(?:AscensionHelper\.\w+\([^,]+,\s*)?(\d+)", content)
        if m:
            props["min_hp"] = m.group(1)
        m = re.search(r"override\s+int\s+MaxInitialHp\s*=>\s*(?:AscensionHelper\.\w+\([^,]+,\s*)?(\d+)", content)
        if m:
            props["max_hp"] = m.group(1)

        # Encounter room type
        m = re.search(r"override\s+RoomType\s+RoomType\s*=>\s*RoomType\.(\w+)", content)
        if m:
            props["room_type"] = m.group(1)

        # Pool attribute
        m = re.search(r"\[Pool\(typeof\((\w+)\)\)\]", content)
        if m:
            props["pool"] = m.group(1)

        # Potion usage
        m = re.search(r"override\s+PotionUsage\s+Usage\s*=>\s*PotionUsage\.(\w+)", content)
        if m:
            props["usage"] = m.group(1)

        # Keywords
        keywords = re.findall(r"CardKeyword\.(\w+)", content)
        if keywords:
            props["keywords"] = list(set(keywords))

        return EntityInfo(
            name=class_name,
            entity_type=entity_type,
            namespace=namespace,
            file_path=str(file_path),
            base_class=base_class,
            properties=props,
        )

    def _parse_hooks(self):
        hook_file = self.decompiled_path / "MegaCrit.Sts2.Core.Hooks" / "Hook.cs"
        if not hook_file.exists():
            return

        content = hook_file.read_text(encoding="utf-8-sig")
        method_pattern = re.compile(
            r"public\s+static\s+(?:async\s+)?(?:Task(?:<([^>]+)>)?\s+|(\w+)\s+)(\w+)\s*\(([^)]*)\)",
            re.MULTILINE,
        )

        for match in method_pattern.finditer(content):
            return_generic = match.group(1)
            return_type_raw = match.group(2)
            method_name = match.group(3)
            params = match.group(4).strip()

            if method_name in ("IterateHookListeners", "ModifyDamageInternal"):
                continue

            ret = f"Task<{return_generic}>" if return_generic else (return_type_raw or "Task")
            category = "event"
            if method_name.startswith("Before"):
                category = "before"
            elif method_name.startswith("After"):
                category = "after"
            elif method_name.startswith("Modify"):
                category = "modify"
            elif method_name.startswith("Should"):
                category = "should"
            elif method_name.startswith("Try"):
                category = "try"

            subcategory = self._classify_hook(method_name)

            self.hooks.append({
                "name": method_name,
                "return_type": ret,
                "params": params,
                "category": category,
                "subcategory": subcategory,
            })

    def _parse_overridable_hooks(self):
        """Parse AbstractModel virtual methods (regex fallback)."""
        am_file = self.decompiled_path / "MegaCrit.Sts2.Core.Models" / "AbstractModel.cs"
        if not am_file.exists():
            return
        try:
            content = am_file.read_text(encoding="utf-8-sig")
        except Exception:
            return
        for m in re.finditer(
            r"public\s+virtual\s+(?:async\s+)?[\w<>\[\]?,\s]+\s+(\w+)\s*\(",
            content,
        ):
            self.overridable_hooks.add(m.group(1))

    def _classify_hook(self, name: str) -> str:
        nl = name.lower()
        if "card" in nl and "reward" not in nl:
            return "card"
        if "damage" in nl or "block" in nl or "hp" in nl:
            return "damage_block"
        if "power" in nl:
            return "power"
        if "turn" in nl or "energy" in nl or "flush" in nl or "playphase" in nl:
            return "turn"
        if "room" in nl or "map" in nl or "act" in nl:
            return "map"
        if "reward" in nl or "merchant" in nl or "purchase" in nl:
            return "reward"
        if "potion" in nl:
            return "potion"
        if "orb" in nl:
            return "orb"
        if "combat" in nl:
            return "combat"
        if "death" in nl or "die" in nl or "doom" in nl:
            return "death"
        if "draw" in nl or "hand" in nl or "shuffle" in nl or "discard" in nl:
            return "hand"
        if "star" in nl or "summon" in nl or "forge" in nl:
            return "special"
        if "rest" in nl or "heal" in nl:
            return "rest_site"
        if "gold" in nl:
            return "gold"
        if "relic" in nl or "treasure" in nl:
            return "relic"
        return "general"

    def _parse_console_commands(self):
        cmd_dir = self.decompiled_path / "MegaCrit.Sts2.Core.DevConsole.ConsoleCommands"
        if not cmd_dir.exists():
            return

        for cs_file in sorted(cmd_dir.glob("*.cs")):
            try:
                content = cs_file.read_text(encoding="utf-8-sig")
            except Exception:
                continue

            # Game uses CmdName property
            name_match = re.search(r'override\s+string\s+CmdName\s*=>\s*"([^"]*)"', content)
            if not name_match:
                continue
            cmd_name = name_match.group(1)

            desc_match = re.search(r'override\s+string\s+Description\s*=>\s*"([^"]*)"', content)
            description = desc_match.group(1) if desc_match else ""

            args_match = re.search(r'override\s+string\s+Args\s*=>\s*"([^"]*)"', content)
            usage = args_match.group(1) if args_match else ""

            is_networked = "IsNetworked => true" in content

            self.console_commands.append({
                "name": cmd_name,
                "description": description,
                "args": usage,
                "is_networked": is_networked,
                "file": cs_file.name,
            })

    # --- Query Methods ---

    def list_entities(self, entity_type: str = "", query: str = "",
                      rarity: str = "", limit: int = 200) -> list[dict]:
        self.ensure_indexed()
        results = []

        source = self.by_type.get(entity_type, []) if entity_type else list(self.entities.values())

        for info in source:
            if rarity and info.properties.get("rarity", "").lower() != rarity.lower():
                continue
            if query and query.lower() not in info.name.lower():
                continue
            results.append({
                "name": info.name,
                "type": info.entity_type,
                "base_class": info.base_class,
                "properties": info.properties,
            })
            if len(results) >= limit:
                break

        return sorted(results, key=lambda x: x["name"])

    def get_source(self, class_name: str) -> Optional[str]:
        self.ensure_indexed()

        # Direct lookup
        info = self.entities.get(class_name)
        if info:
            return self._read_file(info.file_path)

        # Case-insensitive lookup in entities
        for name, info in self.entities.items():
            if name.lower() == class_name.lower():
                return self._read_file(info.file_path)

        # Check all_files index
        if class_name in self.all_files:
            return self._read_file(str(self.all_files[class_name]))
        for name, path in self.all_files.items():
            if name.lower() == class_name.lower():
                return self._read_file(str(path))

        # Glob fallback
        for cs_file in self.decompiled_path.rglob("*.cs"):
            if cs_file.stem.lower() == class_name.lower():
                return self._read_file(str(cs_file))

        return None

    def get_source_by_path(self, relative_path: str) -> Optional[str]:
        """Get source by relative path within decompiled dir."""
        full_path = self.decompiled_path / relative_path
        if full_path.exists():
            return self._read_file(str(full_path))
        return None

    def search_code(self, pattern: str, max_results: int = 50) -> list[dict]:
        self.ensure_indexed()
        results = []
        try:
            regex = re.compile(pattern, re.IGNORECASE)
        except re.error:
            regex = re.compile(re.escape(pattern), re.IGNORECASE)

        for cs_file in sorted(self.decompiled_path.rglob("*.cs")):
            try:
                content = cs_file.read_text(encoding="utf-8-sig")
                for i, line in enumerate(content.split("\n"), 1):
                    if regex.search(line):
                        results.append({
                            "file": str(cs_file.relative_to(self.decompiled_path)),
                            "line": i,
                            "content": line.rstrip(),
                        })
                        if len(results) >= max_results:
                            return results
            except Exception:
                continue
        return results

    def get_hooks(self, category: str = "", subcategory: str = "") -> list[dict]:
        self.ensure_indexed()
        results = self.hooks
        if category:
            results = [h for h in results if h["category"] == category]
        if subcategory:
            results = [h for h in results if h["subcategory"] == subcategory]
        return results

    def get_console_commands(self) -> list[dict]:
        self.ensure_indexed()
        return self.console_commands

    def get_entity_info(self, class_name: str) -> Optional[dict]:
        self.ensure_indexed()
        info = self.entities.get(class_name)
        if not info:
            for name, i in self.entities.items():
                if name.lower() == class_name.lower():
                    info = i
                    break
        if not info:
            return None

        result = {
            "name": info.name,
            "type": info.entity_type,
            "namespace": info.namespace,
            "base_class": info.base_class,
            "properties": info.properties,
            "file_path": info.file_path,
        }
        source = self._read_file(info.file_path)
        if source:
            result["source"] = source
        return result

    def get_entity_types_summary(self) -> dict:
        self.ensure_indexed()
        summary = {}
        for etype, entities in sorted(self.by_type.items()):
            if etype in PRIMARY_ENTITY_TYPES:
                summary[etype] = {
                    "count": len(entities),
                    "examples": [e.name for e in entities[:5]],
                }
        return summary

    def list_namespaces(self) -> list[str]:
        self.ensure_indexed()
        return sorted(set(info.namespace for info in self.entities.values()))

    def list_files_in_namespace(self, namespace: str) -> list[str]:
        ns_dir = self.decompiled_path / namespace
        if not ns_dir.exists():
            return []
        return sorted(f.name for f in ns_dir.glob("*.cs"))

    # --- Roslyn-enhanced queries (only available when Roslyn index is loaded) ---

    def get_class_methods(self, class_name: str) -> list[dict]:
        """Get all methods of a class with full signatures (Roslyn only)."""
        self.ensure_indexed()
        cls = self.roslyn_classes.get(class_name)
        if not cls:
            return []
        return cls.get("methods", [])

    def get_class_hierarchy(self, class_name: str) -> list[str]:
        """Walk the inheritance chain upward (Roslyn only)."""
        self.ensure_indexed()
        chain = []
        current = class_name
        seen = set()
        while current and current not in seen:
            seen.add(current)
            cls = self.roslyn_classes.get(current)
            if not cls:
                break
            base = cls.get("base_class")
            if base:
                chain.append(base)
                current = base
            else:
                break
        return chain

    def get_class_interfaces(self, class_name: str) -> list[str]:
        """Get interfaces implemented by a class (Roslyn only)."""
        self.ensure_indexed()
        cls = self.roslyn_classes.get(class_name)
        if not cls:
            return []
        return cls.get("interfaces", [])

    def get_enum_members(self, enum_name: str) -> list[str]:
        """Get members of an enum (Roslyn only)."""
        self.ensure_indexed()
        enum = self.roslyn_enums.get(enum_name)
        if not enum:
            return []
        return enum.get("members", [])

    def find_classes_by_base(self, base_class: str) -> list[str]:
        """Find all classes that extend a given base class (Roslyn only)."""
        self.ensure_indexed()
        return [
            name for name, cls in self.roslyn_classes.items()
            if cls.get("base_class") == base_class
        ]

    def find_overrides_of(self, method_name: str) -> list[dict]:
        """Find all classes that override a specific method (Roslyn only)."""
        self.ensure_indexed()
        results = []
        for class_name, cls in self.roslyn_classes.items():
            for method in cls.get("methods", []):
                if method["name"] == method_name and "override" in method.get("modifiers", []):
                    results.append({
                        "class": class_name,
                        "method": method["name"],
                        "return_type": method["return_type"],
                        "parameters": method.get("parameters", []),
                        "file": cls.get("file", ""),
                    })
        return results

    @staticmethod
    def _read_file(file_path: str) -> Optional[str]:
        try:
            return Path(file_path).read_text(encoding="utf-8-sig")
        except Exception:
            return None
