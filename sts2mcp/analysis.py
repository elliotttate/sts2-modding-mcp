"""Code analysis, patch intelligence, and mod validation for STS2 modding."""

import json
import re
from pathlib import Path
from typing import Optional


class CodeAnalyzer:
    """Provides code intelligence on top of the GameDataIndex."""

    def __init__(self, game_data):
        self.game_data = game_data

    def suggest_patches(self, desired_behavior: str, max_suggestions: int = 10) -> dict:
        """Given a description of desired behavior change, search decompiled source for relevant methods to patch.

        Analyzes the desired_behavior string for keywords and maps them to relevant game classes/methods.
        Returns suggested Harmony patches with target class, method, patch type, and rationale.
        """
        self.game_data.ensure_indexed()

        # Keyword mapping: common modding desires -> relevant classes and search patterns
        behavior_keywords = {
            "damage": [
                ("DamageCmd", r"class DamageCmd"),
                ("ModifyDamage", r"ModifyDamage(?:Additive|Multiplicative)"),
                ("DamageResult", r"class DamageResult"),
                ("AttackIntent", r"GetSingleDamage|GetTotalDamage"),
            ],
            "block": [
                ("CreatureCmd", r"GainBlock"),
                ("ModifyBlock", r"ModifyBlock"),
                ("BlockVar", r"class BlockVar"),
            ],
            "card": [
                ("CardModel", r"class CardModel"),
                ("CardPileCmd", r"class CardPileCmd"),
                ("OnPlay", r"override.*OnPlay"),
                ("CardEntity", r"class CardEntity"),
                ("EnergyCost", r"EnergyCost|ModifyEnergyCost"),
            ],
            "draw": [
                ("CardPileCmd", r"Draw\("),
                ("ModifyHandDraw", r"ModifyHandDraw"),
                ("BeforeHandDraw", r"BeforeHandDraw"),
            ],
            "energy": [
                ("ModifyMaxEnergy", r"ModifyMaxEnergy"),
                ("EnergyReset", r"EnergyReset|AfterEnergyReset"),
                ("PlayerCombatState", r"Energy.*set"),
            ],
            "relic": [
                ("RelicModel", r"class RelicModel"),
                ("RelicEntity", r"class RelicEntity"),
            ],
            "power": [
                ("PowerModel", r"class PowerModel"),
                ("PowerCmd", r"class PowerCmd"),
                ("ModifyPowerAmount", r"ModifyPowerAmount"),
            ],
            "potion": [
                ("PotionModel", r"class PotionModel"),
                ("OnUse", r"override.*OnUse"),
                ("PotionEntity", r"class PotionEntity"),
            ],
            "gold": [
                ("GoldCmd", r"GoldCmd|GainGold|ModifyGold"),
                ("AfterGoldGained", r"AfterGoldGained"),
            ],
            "heal": [
                ("HealCmd", r"HealCmd|Heal\("),
                ("ModifyHealAmount", r"ModifyHealAmount"),
            ],
            "hp": [
                ("Creature", r"MaxHp|CurrentHp|InitialHp"),
                ("HealCmd", r"HealCmd|Heal\("),
                ("ModifyHealAmount", r"ModifyHealAmount"),
            ],
            "health": [
                ("Creature", r"MaxHp|CurrentHp"),
                ("HealCmd", r"HealCmd|Heal\("),
            ],
            "max": [
                ("ModifyMaxEnergy", r"ModifyMaxEnergy"),
                ("MaxHp", r"MaxHp|MaxInitialHp"),
            ],
            "increase": [
                ("ModifyDamage", r"Modify\w+Additive"),
                ("MaxHp", r"MaxHp|MaxInitialHp"),
            ],
            "reward": [
                ("RewardManager", r"class.*Reward"),
                ("ModifyRewards", r"ModifyRewards"),
                ("ModifyCardRewardOptions", r"ModifyCardRewardOptions"),
            ],
            "shop": [
                ("MerchantRoom", r"class MerchantRoom"),
                ("ModifyMerchantPrice", r"ModifyMerchantPrice"),
            ],
            "map": [
                ("ModifyGeneratedMap", r"ModifyGeneratedMap"),
                ("MapGenerator", r"class.*MapGenerator"),
            ],
            "combat": [
                ("CombatManager", r"class CombatManager"),
                ("CombatState", r"class CombatState"),
            ],
            "turn": [
                ("BeforeTurnEnd", r"BeforeTurnEnd"),
                ("AfterTurnEnd", r"AfterTurnEnd"),
                ("EndPlayerTurnAction", r"EndPlayerTurn"),
            ],
            "death": [
                ("ShouldDie", r"ShouldDie"),
                ("BeforeDeath", r"BeforeDeath"),
                ("AfterDeath", r"AfterDeath"),
            ],
            "cost": [
                ("EnergyCost", r"EnergyCost"),
                ("ModifyEnergyCostInCombat", r"ModifyEnergyCostInCombat"),
                ("ModifyMerchantPrice", r"ModifyMerchantPrice"),
                ("ModifyStarCost", r"ModifyStarCost"),
            ],
            "orb": [
                ("OrbModel", r"class OrbModel"),
                ("OrbCmd", r"OrbCmd|ChannelOrb|EvokeOrb"),
                ("ModifyOrbValue", r"ModifyOrbValue"),
            ],
            "rest": [
                ("RestSiteRoom", r"class RestSiteRoom"),
                ("AfterRestSiteHeal", r"AfterRestSiteHeal"),
                ("AfterRestSiteSmith", r"AfterRestSiteSmith"),
            ],
            "encounter": [
                ("EncounterModel", r"class EncounterModel"),
                ("GenerateAllEncounters", r"GenerateAllEncounters"),
            ],
            "event": [
                ("EventModel", r"class EventModel"),
                ("EventOption", r"class EventOption"),
            ],
            "monster": [
                ("MonsterModel", r"class MonsterModel"),
                ("MonsterMoveStateMachine", r"GenerateMoveStateMachine"),
            ],
            "upgrade": [
                ("OnUpgrade", r"override.*OnUpgrade"),
                ("CardSelection", r"class.*CardSelection"),
            ],
            "exhaust": [
                ("AfterCardExhausted", r"AfterCardExhausted"),
                ("ExhaustPile", r"ExhaustPile"),
            ],
            "discard": [
                ("AfterCardDiscarded", r"AfterCardDiscarded"),
                ("DiscardPile", r"DiscardPile"),
            ],
            "target": [
                ("TargetType", r"TargetType"),
                ("IsValidTarget", r"IsValidTarget"),
                ("ShouldAllowTargeting", r"ShouldAllowTargeting"),
            ],
            "animation": [
                ("NCreatureVisuals", r"class NCreatureVisuals"),
                ("Animation", r"Animation|Animate"),
            ],
            "save": [
                ("SaveManager", r"class SaveManager"),
                ("SaveData", r"SaveData|OnSave|OnLoad"),
            ],
            "screen": [
                ("NGame", r"class NGame"),
                ("ScreenContext", r"ScreenContext|ActiveScreen"),
            ],
            "keyword": [
                ("CardKeyword", r"CardKeyword"),
                ("HoverTip", r"class.*HoverTip"),
            ],
        }

        # Find relevant keywords in the desired behavior
        behavior_lower = desired_behavior.lower()
        matched_patterns = []

        for keyword, patterns in behavior_keywords.items():
            if keyword in behavior_lower:
                matched_patterns.extend(patterns)

        # If no keyword match, do a broad search
        if not matched_patterns:
            # Extract notable words and search for them
            words = re.findall(r'\b[a-z]{4,}\b', behavior_lower)
            for word in words[:5]:
                results = self.game_data.search_code(word, max_results=5)
                for r in results:
                    class_match = re.search(r'class\s+(\w+)', r.get("content", ""))
                    if class_match:
                        matched_patterns.append((class_match.group(1), re.escape(word)))

        # Search for each pattern and build suggestions
        suggestions = []
        seen_targets = set()

        for target_hint, pattern in matched_patterns:
            if len(suggestions) >= max_suggestions:
                break

            results = self.game_data.search_code(pattern, max_results=10)
            for r in results:
                file_path = r.get("file", "")
                content = r.get("content", "").strip()
                line = r.get("line", 0)

                # Extract class and method from context (handle both / and \ separators)
                path_parts = re.split(r'[/\\]', file_path)
                class_name = path_parts[-1].replace(".cs", "") if path_parts else ""

                # Try declaration pattern first
                method_match = re.search(
                    r'(?:public|protected|private|internal)\s+(?:static\s+)?(?:async\s+)?'
                    r'(?:override\s+)?(?:virtual\s+)?(?:[\w<>\[\]?,\s]+?)\s+(\w+)\s*\(',
                    content,
                )
                if not method_match:
                    # Try method call pattern: .MethodName( or ClassName.MethodName(
                    method_match = re.search(r'\.(\w+)\s*[\(<]', content)
                method_name = method_match.group(1) if method_match else ""

                if not class_name or not method_name:
                    continue

                key = f"{class_name}.{method_name}"
                if key in seen_targets:
                    continue
                seen_targets.add(key)

                # Determine patch type
                patch_type = "Postfix"
                rationale = f"Modify behavior after {method_name} executes"
                if "Modify" in method_name or "Get" in method_name:
                    patch_type = "Postfix"
                    rationale = f"Modify the return value of {method_name}"
                elif "Before" in method_name or "Should" in method_name:
                    patch_type = "Prefix"
                    rationale = f"Intercept before {method_name} to change behavior"
                elif "Create" in method_name or "Generate" in method_name:
                    patch_type = "Postfix"
                    rationale = f"Modify the generated result from {method_name}"

                suggestions.append({
                    "target_class": class_name,
                    "target_method": method_name,
                    "patch_type": patch_type,
                    "rationale": rationale,
                    "file": file_path,
                    "line": line,
                    "context": content[:200],
                })

                if len(suggestions) >= max_suggestions:
                    break

        return {
            "query": desired_behavior,
            "suggestion_count": len(suggestions),
            "suggestions": suggestions,
            "tip": "Use get_entity_source to read the full source of suggested classes before patching.",
        }

    def analyze_method_callers(self, class_name: str, method_name: str, max_results: int = 30) -> dict:
        """Show who calls a method and what it calls (basic call graph)."""
        self.game_data.ensure_indexed()

        # Find callers (who calls this method)
        call_pattern = rf'\.{re.escape(method_name)}\s*[\(<]'
        callers = self.game_data.search_code(call_pattern, max_results=max_results)

        # Filter out the definition itself
        callers = [c for c in callers if not re.search(rf'(class|override|virtual|abstract)\s+.*{re.escape(method_name)}', c.get("content", ""))]

        # Find callees (what does this method call) - read the method source
        callees = []
        source = self.game_data.get_source(class_name)
        if source:
            # Find the method body
            method_pattern = rf'(?:public|protected|private|internal)\s+(?:static\s+)?(?:async\s+)?(?:override\s+)?[\w<>\[\]?,\s]+\s+{re.escape(method_name)}\s*\('
            match = re.search(method_pattern, source)
            if match:
                # Find the method body (track braces)
                start = match.start()
                brace_count = 0
                in_method = False
                method_body = ""
                for i in range(start, len(source)):
                    if source[i] == '{':
                        brace_count += 1
                        in_method = True
                    elif source[i] == '}':
                        brace_count -= 1
                    if in_method:
                        method_body += source[i]
                    if in_method and brace_count == 0:
                        break

                # Extract method calls from the body
                call_matches = re.findall(r'(?:await\s+)?(\w+(?:\.\w+)*)\s*(?:<[^>]+>)?\s*\(', method_body)
                seen = set()
                for call in call_matches:
                    if call not in seen and call != method_name and not call.startswith("new "):
                        seen.add(call)
                        callees.append(call)

        # Find overrides of this method
        override_pattern = rf'override\s+.*{re.escape(method_name)}\s*\('
        overrides = self.game_data.search_code(override_pattern, max_results=20)

        return {
            "class": class_name,
            "method": method_name,
            "callers": {
                "count": len(callers),
                "results": callers,
            },
            "callees": callees,
            "overrides": {
                "count": len(overrides),
                "results": overrides,
            },
        }

    def get_entity_relationships(self, entity_name: str) -> dict:
        """Show what other entities an entity interacts with (powers it applies, cards it references, etc.)."""
        self.game_data.ensure_indexed()

        source = self.game_data.get_source(entity_name)
        if not source:
            return {"error": f"Entity '{entity_name}' not found"}

        info = self.game_data.get_entity_info(entity_name)

        relationships = {
            "entity": entity_name,
            "type": info.get("type", "unknown") if info else "unknown",
            "base_class": info.get("base_class", "") if info else "",
            "applies_powers": [],
            "references_cards": [],
            "references_relics": [],
            "references_potions": [],
            "references_monsters": [],
            "uses_commands": [],
            "hooks_used": [],
            "keywords": [],
        }

        # Powers applied (PowerCmd.Apply<XPower> or new XPower)
        power_refs = re.findall(r'(?:Apply|PowerCmd\.Apply)\s*<\s*(\w+Power)\s*>', source)
        power_refs += re.findall(r'new\s+(\w+Power)\s*\(', source)
        relationships["applies_powers"] = list(set(power_refs))

        # Card references (ModelDb.Card<X> or new X())
        card_entities = set(e.name for e in self.game_data.by_type.get("card", []))
        card_refs = re.findall(r'ModelDb\.Card<(\w+)>', source)
        card_refs += [m for m in re.findall(r'new\s+(\w+)\s*\(', source) if m in card_entities]
        relationships["references_cards"] = list(set(card_refs))

        # Relic references
        relic_entities = set(e.name for e in self.game_data.by_type.get("relic", []))
        relic_refs = re.findall(r'ModelDb\.Relic<(\w+)>', source)
        relic_refs += [m for m in re.findall(r'new\s+(\w+)\s*\(', source) if m in relic_entities]
        relationships["references_relics"] = list(set(relic_refs))

        # Potion references
        potion_entities = set(e.name for e in self.game_data.by_type.get("potion", []))
        potion_refs = re.findall(r'ModelDb\.Potion<(\w+)>', source)
        potion_refs += [m for m in re.findall(r'new\s+(\w+)\s*\(', source) if m in potion_entities]
        relationships["references_potions"] = list(set(potion_refs))

        # Monster references
        monster_entities = set(e.name for e in self.game_data.by_type.get("monster", []))
        monster_refs = re.findall(r'ModelDb\.Monster<(\w+)>', source)
        relationships["references_monsters"] = list(set(monster_refs))

        # Commands used
        cmd_refs = re.findall(r'(\w+Cmd)\.\w+', source)
        relationships["uses_commands"] = list(set(cmd_refs))

        # Hooks (override methods that are hooks)
        hook_names = set(h["name"] for h in self.game_data.hooks)
        overrides = re.findall(r'override\s+(?:async\s+)?(?:Task|decimal|bool|int|void)\s*(?:<[^>]+>)?\s+(\w+)\s*\(', source)
        relationships["hooks_used"] = [o for o in overrides if o in hook_names]

        # Keywords
        kw_refs = re.findall(r'CardKeyword\.(\w+)', source)
        relationships["keywords"] = list(set(kw_refs))

        # Clean up empty lists
        relationships = {k: v for k, v in relationships.items() if v}

        return relationships

    def validate_mod(self, project_dir: str) -> dict:
        """Validate a mod project for common issues."""
        self.game_data.ensure_indexed()
        project = Path(project_dir)
        from .project_workflow import validate_project as validate_project_workflow

        if not project.exists():
            return {"valid": False, "errors": [f"Project directory not found: {project_dir}"]}

        errors = []
        warnings = []
        info = []
        workflow_validation = validate_project_workflow(project_dir)
        errors.extend(workflow_validation.get("errors", []))
        warnings.extend(workflow_validation.get("warnings", []))

        # Check manifest
        manifest_path = project / "mod_manifest.json"
        if not manifest_path.exists():
            errors.append("Missing mod_manifest.json")
        else:
            try:
                manifest = json.loads(manifest_path.read_text())
                required_keys = ["id", "name", "author", "version", "has_dll"]
                for key in required_keys:
                    if key not in manifest:
                        errors.append(f"mod_manifest.json missing required key: {key}")
                if manifest.get("has_pck") and not manifest.get("pck_name"):
                    warnings.append("has_pck=true but no pck_name specified")
            except json.JSONDecodeError as e:
                errors.append(f"Invalid JSON in mod_manifest.json: {e}")

        # Check .csproj
        csproj_files = list(project.glob("*.csproj"))
        if not csproj_files:
            errors.append("No .csproj file found")
        else:
            csproj_content = csproj_files[0].read_text()
            if "EnableDynamicLoading" not in csproj_content:
                errors.append(".csproj missing EnableDynamicLoading=true (required for mod loading)")
            if "sts2.dll" not in csproj_content:
                errors.append(".csproj missing reference to sts2.dll")
            if "net9.0" not in csproj_content and "net8.0" not in csproj_content:
                warnings.append(".csproj may need TargetFramework net9.0")

        # Check ModEntry
        mod_entry_found = False
        for cs_file in project.rglob("*.cs"):
            try:
                content = cs_file.read_text(encoding="utf-8-sig")
                if "[ModInitializer" in content:
                    mod_entry_found = True
                    break
            except Exception:
                continue
        if not mod_entry_found:
            errors.append("No [ModInitializer] entry point found in any .cs file")

        # Check localization
        loc_dirs = list(project.rglob("localization/eng"))
        if not loc_dirs:
            warnings.append("No localization/eng directory found")
        else:
            # Collect all entity class names from source
            entity_classes = set()
            for cs_file in project.rglob("Code/**/*.cs"):
                try:
                    content = cs_file.read_text(encoding="utf-8-sig")
                    class_matches = re.findall(r'class\s+(\w+)\s*:\s*(?:Custom)?(?:Card|Relic|Power|Potion)Model', content)
                    entity_classes.update(class_matches)
                except Exception:
                    continue

            # Check if entities have localization
            all_loc_keys = set()
            for loc_file in loc_dirs[0].glob("*.json"):
                try:
                    loc_data = json.loads(loc_file.read_text())
                    all_loc_keys.update(loc_data.keys())
                except Exception:
                    warnings.append(f"Invalid JSON in {loc_file.name}")

            for entity_class in entity_classes:
                snake = re.sub(r'(.)([A-Z][a-z]+)', r'\1_\2', entity_class)
                screaming = re.sub(r'([a-z0-9])([A-Z])', r'\1_\2', snake).upper()
                has_title = any(k.startswith(screaming + ".") for k in all_loc_keys)
                if not has_title:
                    warnings.append(f"No localization found for entity '{entity_class}' (expected key prefix: {screaming})")

        # Check Harmony usage
        harmony_init_found = False
        for cs_file in project.rglob("*.cs"):
            try:
                content = cs_file.read_text(encoding="utf-8-sig")
                if "new Harmony(" in content and ".PatchAll()" in content:
                    harmony_init_found = True
                if "[HarmonyPatch" in content and not harmony_init_found:
                    warnings.append(f"Found [HarmonyPatch] in {cs_file.name} but no Harmony.PatchAll() detected - patches may not apply")
                    break
            except Exception:
                continue

        # Check for common issues in C# code
        for cs_file in project.rglob("Code/**/*.cs"):
            try:
                content = cs_file.read_text(encoding="utf-8-sig")
                fname = cs_file.name

                # Check for async methods without await
                async_methods = re.findall(r'async\s+Task\s+(\w+)', content)
                for method in async_methods:
                    # Simple check: method body should contain await
                    method_start = content.find(f"async Task {method}")
                    if method_start >= 0:
                        # Find method body
                        brace_start = content.find("{", method_start)
                        if brace_start >= 0:
                            depth = 0
                            body = ""
                            for i in range(brace_start, min(brace_start + 2000, len(content))):
                                if content[i] == '{':
                                    depth += 1
                                elif content[i] == '}':
                                    depth -= 1
                                body += content[i]
                                if depth == 0:
                                    break
                            if "await " not in body and "Task.CompletedTask" not in body:
                                warnings.append(f"{fname}: async method '{method}' has no await - will run synchronously")

                # Pool attribute without proper base class
                if "[Pool(" in content:
                    pool_match = re.search(r'\[Pool\(typeof\((\w+)\)\)\]', content)
                    if pool_match:
                        pool_type = pool_match.group(1)
                        if "CardPool" in pool_type and "CardModel" not in content:
                            warnings.append(f"{fname}: Has CardPool attribute but doesn't extend CardModel")
                        if "RelicPool" in pool_type and "RelicModel" not in content:
                            warnings.append(f"{fname}: Has RelicPool attribute but doesn't extend RelicModel")
            except Exception:
                continue

        info.append(f"Found {len(list(project.rglob('Code/**/*.cs')))} C# source files")
        info.append(f"Found {len(list(project.rglob('*.json')))} JSON files")

        return {
            "valid": len(errors) == 0,
            "error_count": len(errors),
            "warning_count": len(warnings),
            "errors": errors,
            "warnings": warnings,
            "info": info,
            "workflow_validation": workflow_validation,
        }

    def diff_game_versions(self, old_decompiled_dir: str, new_decompiled_dir: str) -> dict:
        """Compare two decompiled source directories to find API changes."""
        old_dir = Path(old_decompiled_dir)
        new_dir = Path(new_decompiled_dir)

        if not old_dir.exists():
            return {"error": f"Old directory not found: {old_decompiled_dir}"}
        if not new_dir.exists():
            return {"error": f"New directory not found: {new_decompiled_dir}"}

        changes = {
            "added_files": [],
            "removed_files": [],
            "modified_files": [],
            "changed_hooks": [],
            "changed_public_methods": [],
        }

        # Collect all .cs files
        old_files = {}
        for f in old_dir.rglob("*.cs"):
            rel = str(f.relative_to(old_dir))
            old_files[rel] = f

        new_files = {}
        for f in new_dir.rglob("*.cs"):
            rel = str(f.relative_to(new_dir))
            new_files[rel] = f

        # Added/removed files
        for rel in sorted(set(new_files.keys()) - set(old_files.keys())):
            changes["added_files"].append(rel)
        for rel in sorted(set(old_files.keys()) - set(new_files.keys())):
            changes["removed_files"].append(rel)

        # Modified files - check hooks and public methods
        for rel in sorted(set(old_files.keys()) & set(new_files.keys())):
            try:
                old_content = old_files[rel].read_text(encoding="utf-8-sig")
                new_content = new_files[rel].read_text(encoding="utf-8-sig")

                if old_content == new_content:
                    continue

                changes["modified_files"].append(rel)

                # Check for hook signature changes (in Hook.cs)
                if "Hook.cs" in rel:
                    old_hooks = set(re.findall(r'public\s+static\s+(?:async\s+)?(?:Task(?:<[^>]+>)?\s+|\w+\s+)(\w+)\s*\([^)]*\)', old_content))
                    new_hooks = set(re.findall(r'public\s+static\s+(?:async\s+)?(?:Task(?:<[^>]+>)?\s+|\w+\s+)(\w+)\s*\([^)]*\)', new_content))

                    for hook in sorted(new_hooks - old_hooks):
                        changes["changed_hooks"].append({"hook": hook, "change": "added"})
                    for hook in sorted(old_hooks - new_hooks):
                        changes["changed_hooks"].append({"hook": hook, "change": "removed"})
                    # Check signature changes
                    for hook in sorted(old_hooks & new_hooks):
                        old_sig = re.search(rf'{re.escape(hook)}\s*\([^)]*\)', old_content)
                        new_sig = re.search(rf'{re.escape(hook)}\s*\([^)]*\)', new_content)
                        if old_sig and new_sig and old_sig.group() != new_sig.group():
                            changes["changed_hooks"].append({
                                "hook": hook,
                                "change": "signature_changed",
                                "old": old_sig.group(),
                                "new": new_sig.group(),
                            })

                # Check public method changes in key model classes
                if any(base in rel for base in ["CardModel", "RelicModel", "PowerModel", "PotionModel", "MonsterModel", "AbstractModel"]):
                    old_methods = set(re.findall(r'public\s+(?:virtual\s+|override\s+|abstract\s+)?(?:async\s+)?[\w<>\[\],?\s]+\s+(\w+)\s*\(', old_content))
                    new_methods = set(re.findall(r'public\s+(?:virtual\s+|override\s+|abstract\s+)?(?:async\s+)?[\w<>\[\],?\s]+\s+(\w+)\s*\(', new_content))

                    class_name = rel.split("/")[-1].replace(".cs", "")
                    for method in sorted(new_methods - old_methods):
                        changes["changed_public_methods"].append({"class": class_name, "method": method, "change": "added"})
                    for method in sorted(old_methods - new_methods):
                        changes["changed_public_methods"].append({"class": class_name, "method": method, "change": "removed"})
            except Exception:
                continue

        changes["summary"] = {
            "added_files": len(changes["added_files"]),
            "removed_files": len(changes["removed_files"]),
            "modified_files": len(changes["modified_files"]),
            "changed_hooks": len(changes["changed_hooks"]),
            "changed_public_methods": len(changes["changed_public_methods"]),
        }

        # Truncate large lists for readability
        for key in ["added_files", "removed_files", "modified_files"]:
            if len(changes[key]) > 50:
                changes[key] = changes[key][:50] + [f"... and {len(changes[key]) - 50} more"]

        return changes

    def check_mod_compatibility(self, project_dir: str) -> dict:
        """Check if a mod's code references any APIs that may have changed."""
        self.game_data.ensure_indexed()
        project = Path(project_dir)

        if not project.exists():
            return {"error": f"Project directory not found: {project_dir}"}

        issues = []
        checked_refs = 0

        # Collect all game class/method references from mod source
        for cs_file in project.rglob("*.cs"):
            try:
                content = cs_file.read_text(encoding="utf-8-sig")
                fname = cs_file.name

                # Check Harmony patch targets exist
                patch_targets = re.findall(r'\[HarmonyPatch\(typeof\((\w+)\),\s*(?:nameof\(\w+\.(\w+)\)|"(\w+)")', content)
                for target_class, method_nameof, method_str in patch_targets:
                    method_name = method_nameof or method_str
                    checked_refs += 1

                    source = self.game_data.get_source(target_class)
                    if source is None:
                        issues.append({
                            "file": fname,
                            "severity": "error",
                            "message": f"Harmony patch target class '{target_class}' not found in game source",
                        })
                    elif method_name and method_name not in source:
                        issues.append({
                            "file": fname,
                            "severity": "error",
                            "message": f"Harmony patch target method '{target_class}.{method_name}' not found in game source",
                        })

                # Check base class references exist
                base_refs = re.findall(r':\s*(?:Custom)?(\w+Model)\b', content)
                for base_class in base_refs:
                    checked_refs += 1
                    if base_class not in ("CardModel", "RelicModel", "PowerModel", "PotionModel",
                                          "MonsterModel", "EncounterModel", "EventModel", "CharacterModel",
                                          "AncientModel", "OrbModel", "EnchantmentModel"):
                        # Non-standard base class - check it exists
                        if self.game_data.get_source(base_class) is None:
                            # Might be from BaseLib
                            if not base_class.startswith("Custom"):
                                issues.append({
                                    "file": fname,
                                    "severity": "warning",
                                    "message": f"Base class '{base_class}' not found in game source (may be from a library)",
                                })

                # Check ModelDb references
                modeldb_refs = re.findall(r'ModelDb\.(\w+)<(\w+)>', content)
                for accessor, type_name in modeldb_refs:
                    checked_refs += 1
                    # Check if the referenced type exists
                    info = self.game_data.entities.get(type_name)
                    if not info:
                        # Case-insensitive check
                        found = False
                        for name in self.game_data.entities:
                            if name.lower() == type_name.lower():
                                found = True
                                break
                        if not found:
                            issues.append({
                                "file": fname,
                                "severity": "warning",
                                "message": f"ModelDb.{accessor}<{type_name}> references a type not found in game (may be a custom type)",
                            })

                # Check hook method signatures match
                hook_names = {h["name"]: h for h in self.game_data.hooks}
                override_hooks = re.findall(r'override\s+(?:async\s+)?(?:Task|decimal|bool|int|void)\s*(?:<[^>]+>)?\s+(\w+)\s*\(([^)]*)\)', content)
                for hook_name, params in override_hooks:
                    if hook_name in hook_names:
                        checked_refs += 1
                        # Basic parameter count check
                        hook_info = hook_names[hook_name]
                        expected_params = [p.strip() for p in hook_info["params"].split(",") if p.strip()]
                        actual_params = [p.strip() for p in params.split(",") if p.strip()]
                        if len(expected_params) != len(actual_params):
                            issues.append({
                                "file": fname,
                                "severity": "warning",
                                "message": f"Hook '{hook_name}' parameter count mismatch: expected {len(expected_params)}, got {len(actual_params)}",
                            })
            except Exception:
                continue

        return {
            "compatible": len([i for i in issues if i["severity"] == "error"]) == 0,
            "checked_references": checked_refs,
            "issue_count": len(issues),
            "errors": [i for i in issues if i["severity"] == "error"],
            "warnings": [i for i in issues if i["severity"] == "warning"],
        }

    def search_hooks_by_signature(self, param_type: str) -> list[dict]:
        """Search hooks by parameter type name."""
        self.game_data.ensure_indexed()
        results = []
        param_lower = param_type.lower()

        for hook in self.game_data.hooks:
            if param_lower in hook.get("params", "").lower():
                results.append(hook)

        return results

    def get_hook_signature(self, hook_name: str) -> dict:
        """Return a hook's signature and a ready-to-paste override skeleton."""
        self.game_data.ensure_indexed()

        hook = next((item for item in self.game_data.hooks if item["name"].lower() == hook_name.lower()), None)
        if not hook:
            return {
                "found": False,
                "error": f"Hook '{hook_name}' not found",
            }

        signature = f"{hook['return_type']} {hook['name']}({hook['params']})"
        return {
            "found": True,
            "hook": hook,
            "signature": signature,
            "override_stub": self._build_hook_override_stub(hook),
        }

    def analyze_build_output(self, stdout: str = "", stderr: str = "") -> dict:
        """Summarize structured errors and warnings from dotnet build output."""
        combined = "\n".join(part for part in (stdout, stderr) if part).strip()
        if not combined:
            return {
                "error_count": 0,
                "warning_count": 0,
                "errors": [],
                "warnings": [],
                "summary": "No build output provided.",
            }

        issue_pattern = re.compile(
            r"^(?P<file>.*?)(?:\((?P<line>\d+),(?P<column>\d+)\))?:\s+"
            r"(?P<severity>error|warning)\s+"
            r"(?P<code>[A-Z]{2,}\d+):\s+"
            r"(?P<message>.*)$",
            re.IGNORECASE,
        )

        errors = []
        warnings = []
        for raw_line in combined.splitlines():
            match = issue_pattern.match(raw_line.strip())
            if not match:
                continue
            issue = {
                "file": match.group("file") or "",
                "line": int(match.group("line")) if match.group("line") else 0,
                "column": int(match.group("column")) if match.group("column") else 0,
                "code": match.group("code") or "",
                "message": match.group("message") or "",
            }
            if match.group("severity").lower() == "error":
                errors.append(issue)
            else:
                warnings.append(issue)

        summary = (
            f"{len(errors)} error(s), {len(warnings)} warning(s) parsed from build output."
            if (errors or warnings)
            else "No structured compiler errors or warnings were parsed from the provided output."
        )
        return {
            "error_count": len(errors),
            "warning_count": len(warnings),
            "errors": errors,
            "warnings": warnings,
            "summary": summary,
        }

    def list_game_vfx(self, query: str = "") -> list[dict]:
        """List VFX-related classes and scenes in the game."""
        self.game_data.ensure_indexed()

        results = []

        # Search for VFX, particle, and animation classes
        vfx_patterns = [
            (r'GPUParticles', "particle_system"),
            (r'class\s+\w*(?:Vfx|VFX|Effect|Particle)\w*', "vfx_class"),
            (r'AnimationPlayer', "animation"),
            (r'class\s+N\w*Animation', "animation_class"),
        ]

        seen = set()
        for pattern, vfx_type in vfx_patterns:
            if query and query.lower() not in pattern.lower() and query.lower() not in vfx_type.lower():
                continue
            matches = self.game_data.search_code(pattern, max_results=20)
            for m in matches:
                file_key = m.get("file", "")
                if file_key not in seen:
                    seen.add(file_key)
                    results.append({
                        "file": file_key,
                        "line": m.get("line", 0),
                        "type": vfx_type,
                        "content": m.get("content", "").strip()[:150],
                    })

        # Also look for scene loading patterns that might be VFX
        scene_patterns = self.game_data.search_code(r'GetScene\(".*(?:vfx|effect|particle|animation)', max_results=20)
        for m in scene_patterns:
            path_match = re.search(r'GetScene\("([^"]+)"', m.get("content", ""))
            if path_match:
                results.append({
                    "file": m.get("file", ""),
                    "line": m.get("line", 0),
                    "type": "scene_reference",
                    "scene_path": path_match.group(1),
                    "content": m.get("content", "").strip()[:150],
                })

        if query:
            query_lower = query.lower()
            results = [r for r in results if query_lower in str(r).lower()]

        return results

    def reverse_hook_lookup(self, entity_name: str) -> dict:
        """Find what hooks fire when an entity is used/triggered."""
        self.game_data.ensure_indexed()

        source = self.game_data.get_source(entity_name)
        if not source:
            return {"error": f"Entity '{entity_name}' not found"}

        info = self.game_data.get_entity_info(entity_name)
        entity_type = info.get("type", "unknown") if info else "unknown"
        base_class = info.get("base_class", "") if info else ""

        # Map entity types to hooks that typically fire for them
        type_hooks: dict[str, list[str]] = {
            "card": [
                "BeforeCardPlayed", "AfterCardPlayed", "AfterCardExhausted",
                "AfterCardDiscarded", "ModifyEnergyCostInCombat",
                "ModifyDamageAdditive", "ModifyDamageMultiplicative",
                "ModifyBlockAdditive", "ModifyBlockMultiplicative",
                "ShouldAllowCardPlay",
            ],
            "relic": [
                "BeforeCombatStart", "AfterCombatStart", "AfterCombatEnd",
                "AfterTurnEnd", "BeforeTurnEnd", "AfterCardPlayed",
                "ModifyDamageAdditive", "ModifyBlockAdditive",
                "AfterGoldGained", "ModifyHealAmount", "AfterRestSiteHeal",
                "ModifyRewards",
            ],
            "power": [
                "ModifyDamageAdditive", "ModifyDamageMultiplicative",
                "ModifyBlockAdditive", "ModifyBlockMultiplicative",
                "BeforeTurnEnd", "AfterTurnEnd", "AfterCardPlayed",
            ],
            "potion": [
                "BeforePotionUsed", "AfterPotionUsed",
            ],
            "monster": [
                "BeforeDeath", "AfterDeath", "ShouldDie",
                "ModifyDamageAdditive", "ModifyDamageMultiplicative",
            ],
        }

        relevant_hooks = type_hooks.get(entity_type, [])

        # Find hooks the entity actually overrides
        hook_names = set(h["name"] for h in self.game_data.hooks)
        overrides = re.findall(
            r'override\s+(?:async\s+)?(?:Task|decimal|bool|int|void)\s*(?:<[^>]+>)?\s+(\w+)\s*\(',
            source,
        )
        overridden_hooks = sorted(set(o for o in overrides if o in hook_names))

        # Find hooks whose signature accepts this entity's base class
        hooks_by_param = []
        for hook in self.game_data.hooks:
            params = hook.get("params", "")
            if base_class and base_class in params:
                hooks_by_param.append({
                    "hook": hook["name"],
                    "reason": f"Accepts {base_class} parameter",
                })
            if entity_name in params:
                hooks_by_param.append({
                    "hook": hook["name"],
                    "reason": f"Directly accepts {entity_name}",
                })

        return {
            "entity": entity_name,
            "type": entity_type,
            "base_class": base_class,
            "overridden_hooks": overridden_hooks,
            "type_relevant_hooks": relevant_hooks,
            "hooks_accepting_entity": hooks_by_param,
        }

    def _build_hook_override_stub(self, hook: dict) -> str:
        return_type = hook["return_type"]
        params = hook["params"]
        method_name = hook["name"]

        if return_type == "Task":
            body = "        // TODO: implement hook behavior\n        await Task.CompletedTask;"
        elif return_type.startswith("Task<"):
            inner = return_type[5:-1]
            body = (
                "        // TODO: implement hook behavior\n"
                "        await Task.CompletedTask;\n"
                f"        return {self._default_return_expression(inner, params)};"
            )
        else:
            body = (
                "        // TODO: implement hook behavior\n"
                f"        return {self._default_return_expression(return_type, params)};"
            )

        async_prefix = "async " if return_type.startswith("Task") else ""
        return (
            f"public override {async_prefix}{return_type} {method_name}({params})\n"
            "{\n"
            f"{body}\n"
            "}"
        )

    def _default_return_expression(self, return_type: str, params: str) -> str:
        param_names = []
        for raw_param in [piece.strip() for piece in params.split(",") if piece.strip()]:
            parts = raw_param.replace("?", "").split()
            if parts:
                param_names.append(parts[-1])

        for preferred_name in (
            "currentDamage",
            "currentBlock",
            "currentValue",
            "currentAmount",
            "current",
            "amount",
            "value",
        ):
            if preferred_name in param_names:
                return preferred_name

        normalized = return_type.replace("?", "")
        if normalized == "bool":
            return "true"
        if normalized in {"int", "uint", "long", "short", "byte"}:
            return "0"
        if normalized in {"decimal", "double", "float"}:
            return "0"
        return "default!"
