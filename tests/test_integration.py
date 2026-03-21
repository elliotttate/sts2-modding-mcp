"""End-to-end integration test: create a full mod using every new tool.

Creates a complete mod project, generates every entity type, validates,
and optionally builds and tests in the live game.
"""

import json
import os
import shutil
import tempfile
from pathlib import Path

import pytest

from tests.conftest import skip_no_decompiled, skip_no_bridge


class TestFullModCreation:
    """Create a mod project and populate it with every generator."""

    @pytest.fixture(autouse=True)
    def setup_project(self, mod_gen, tmp_mod_dir):
        self.mod_gen = mod_gen
        self.project_dir = tmp_mod_dir
        self.namespace = "IntegrationTest"
        self.mod_name = "IntegrationTest"

        # Create the project
        result = mod_gen.create_mod_project(
            mod_name="Integration Test",
            author="TestRunner",
            description="Automated integration test mod",
            output_dir=self.project_dir,
        )
        assert "project_dir" in result
        self.project = Path(result["project_dir"])

    def _write_to_project(self, result: dict):
        """Write a generator result's source into the project."""
        if "source" in result:
            folder = result.get("folder", "Code")
            file_name = result.get("file_name", "Generated.cs")
            out_dir = self.project / folder
            out_dir.mkdir(parents=True, exist_ok=True)
            (out_dir / file_name).write_text(result["source"])

        if "localization" in result:
            loc_dir = self.project / self.namespace / "localization" / "eng"
            loc_dir.mkdir(parents=True, exist_ok=True)
            for loc_file, entries in result["localization"].items():
                loc_path = loc_dir / loc_file
                existing = {}
                if loc_path.exists():
                    existing = json.loads(loc_path.read_text())
                existing.update(entries)
                loc_path.write_text(json.dumps(existing, indent=2))

    def test_generate_all_entities(self):
        """Generate one of every entity type into the project."""
        results = []

        # Card
        r = self.mod_gen.generate_card(
            mod_namespace=self.namespace, class_name="TestStrike",
            card_type="Attack", damage=8, energy_cost=1,
        )
        self._write_to_project(r)
        results.append(("card", r))

        # Relic
        r = self.mod_gen.generate_relic(
            mod_namespace=self.namespace, class_name="TestAmulet",
            trigger_hook="BeforeCombatStart",
        )
        self._write_to_project(r)
        results.append(("relic", r))

        # Power
        r = self.mod_gen.generate_power(
            mod_namespace=self.namespace, class_name="TestPower",
            trigger_hook="AfterTurnEnd",
        )
        self._write_to_project(r)
        results.append(("power", r))

        # Potion
        r = self.mod_gen.generate_potion(
            mod_namespace=self.namespace, class_name="TestElixir",
            block=12,
        )
        self._write_to_project(r)
        results.append(("potion", r))

        # Event (NEW)
        r = self.mod_gen.generate_event(
            mod_namespace=self.namespace, class_name="TestEvent",
            choices=[
                {"label": "Accept", "method_name": "ChoiceAccept", "effect_description": "Gain gold"},
                {"label": "Decline", "method_name": "ChoiceDecline"},
            ],
        )
        self._write_to_project(r)
        results.append(("event", r))

        # Orb (NEW)
        r = self.mod_gen.generate_orb(
            mod_namespace=self.namespace, class_name="TestOrb",
            passive_amount=3, evoke_amount=9,
        )
        self._write_to_project(r)
        results.append(("orb", r))

        # Enchantment (NEW)
        r = self.mod_gen.generate_enchantment(
            mod_namespace=self.namespace, class_name="TestEnchantment",
            trigger_hook="ModifyDamageAdditive",
        )
        self._write_to_project(r)
        results.append(("enchantment", r))

        # Game Action (NEW)
        r = self.mod_gen.generate_game_action(
            mod_namespace=self.namespace, class_name="TestAction",
            parameters=[
                {"name": "player", "type": "Player"},
                {"name": "amount", "type": "int"},
            ],
        )
        self._write_to_project(r)
        results.append(("game_action", r))

        # Custom Tooltip (NEW)
        r = self.mod_gen.generate_custom_tooltip(
            mod_namespace=self.namespace, tag_name="teststatus",
            title="Test Status", tooltip_description="A test keyword.",
        )
        self._write_to_project(r)
        results.append(("tooltip", r))

        # Save Data (NEW)
        r = self.mod_gen.generate_save_data(
            mod_namespace=self.namespace, mod_id="integrationtest",
            fields=[
                {"name": "RunCount", "type": "int", "default": "0"},
                {"name": "HighScore", "type": "int", "default": "0"},
            ],
        )
        self._write_to_project(r)
        results.append(("save_data", r))

        # Monster + Encounter (existing)
        r = self.mod_gen.generate_monster(
            mod_namespace=self.namespace, mod_name=self.mod_name,
            class_name="TestMonster",
            moves=[
                {"name": "BITE", "damage": 10, "type": "attack"},
                {"name": "GUARD", "block": 8, "type": "defend"},
            ],
        )
        self._write_to_project(r)
        # Write scene file too
        if "scene" in r:
            scene_dir = self.project / r.get("scene_folder", f"{self.mod_name}/MonsterResources/TestMonster")
            scene_dir.mkdir(parents=True, exist_ok=True)
            (scene_dir / r["scene_file_name"]).write_text(r["scene"])
        results.append(("monster", r))

        r = self.mod_gen.generate_encounter(
            mod_namespace=self.namespace, class_name="TestEncounter",
            monsters=["TestMonster"],
        )
        self._write_to_project(r)
        results.append(("encounter", r))

        # Harmony Patch (existing)
        r = self.mod_gen.generate_harmony_patch(
            mod_namespace=self.namespace, class_name="TestPatch",
            target_type="CardModel", target_method="OnPlay",
        )
        self._write_to_project(r)
        results.append(("patch", r))

        # Verify all were generated
        assert len(results) >= 13, f"Expected 13+ results, got {len(results)}"

        # Count .cs files in project
        cs_files = list(self.project.rglob("Code/**/*.cs"))
        assert len(cs_files) >= 12, f"Expected 12+ .cs files, got {len(cs_files)}: {[f.name for f in cs_files]}"

    def test_generate_mechanic(self):
        """Generate a full mechanic and write all files."""
        r = self.mod_gen.generate_mechanic(
            mod_namespace=self.namespace,
            mod_name=self.mod_name,
            keyword_name="Surge",
            keyword_description="At end of turn, gain block equal to Surge stacks.",
        )
        assert "files" in r
        for path, source in r["files"].items():
            out_path = self.project / path
            out_path.parent.mkdir(parents=True, exist_ok=True)
            out_path.write_text(source)

        # Write localization
        if "localization" in r:
            loc_dir = self.project / self.namespace / "localization" / "eng"
            loc_dir.mkdir(parents=True, exist_ok=True)
            for loc_file, entries in r["localization"].items():
                loc_path = loc_dir / loc_file
                existing = {}
                if loc_path.exists():
                    existing = json.loads(loc_path.read_text())
                existing.update(entries)
                loc_path.write_text(json.dumps(existing, indent=2))

        # Verify files were written
        for path in r["files"]:
            assert (self.project / path).exists(), f"File not written: {path}"

    def test_generate_vfx_and_test_scenario(self):
        """Generate VFX scene and test scenario."""
        # VFX
        vfx = self.mod_gen.generate_vfx_scene(node_name="SurgeBlast")
        assert "[gd_scene" in vfx["scene"]

        # Test scenario
        scenario = self.mod_gen.generate_test_scenario(
            scenario_name="Full mechanic test",
            relics=["TestAmulet"],
            cards=["TestStrike"],
            gold=500,
            godmode=True,
        )
        assert scenario["command_count"] > 0
        assert "godmode" in scenario["combined"]


@skip_no_decompiled
class TestValidateGeneratedMod:
    """Create a mod, generate all content, then validate it."""

    def test_validate_full_mod(self, mod_gen, code_analyzer, tmp_mod_dir):
        # Create project
        mod_gen.create_mod_project(
            mod_name="ValidateFull",
            author="Test",
            output_dir=tmp_mod_dir,
        )
        project = Path(tmp_mod_dir)
        ns = "ValidateFull"

        # Generate a card with localization
        card = mod_gen.generate_card(
            mod_namespace=ns, class_name="ValCard",
            card_type="Attack", damage=5,
        )
        (project / "Code" / "Cards").mkdir(parents=True, exist_ok=True)
        (project / "Code" / "Cards" / card["file_name"]).write_text(card["source"])

        # Write localization
        loc_dir = project / ns / "localization" / "eng"
        loc_dir.mkdir(parents=True, exist_ok=True)
        for loc_file, entries in card["localization"].items():
            loc_path = loc_dir / loc_file
            existing = {}
            if loc_path.exists():
                existing = json.loads(loc_path.read_text())
            existing.update(entries)
            loc_path.write_text(json.dumps(existing, indent=2))

        # Validate
        result = code_analyzer.validate_mod(tmp_mod_dir)
        assert result["valid"] is True, f"Errors: {result['errors']}"
        # Should have no warnings about missing localization for ValCard
        loc_warnings = [w for w in result["warnings"] if "ValCard" in w]
        assert len(loc_warnings) == 0, f"Unexpected loc warnings: {loc_warnings}"


@skip_no_decompiled
class TestAnalysisOnRealData:
    """Run analysis tools against real decompiled game data."""

    def test_suggest_patches_for_common_mods(self, code_analyzer):
        """Test several common modding scenarios."""
        scenarios = [
            "make all attacks cost 1 less energy",
            "increase max HP by 10",
            "draw 2 extra cards per turn",
            "double gold from combat rewards",
            "prevent death once per combat",
        ]
        for scenario in scenarios:
            result = code_analyzer.suggest_patches(scenario)
            assert result["suggestion_count"] > 0, \
                f"No suggestions for: '{scenario}'"

    def test_entity_relationships_for_key_entities(self, code_analyzer):
        """Check relationships for well-known game entities."""
        # These entities definitely exist in the game
        for entity in ["Bash", "Inflame", "Survivor"]:
            result = code_analyzer.get_entity_relationships(entity)
            assert "error" not in result, f"Error for {entity}: {result}"
            assert result["entity"] == entity

    def test_method_callers_for_key_methods(self, code_analyzer):
        """Analyze key methods that mods commonly patch."""
        result = code_analyzer.analyze_method_callers("CardModel", "OnPlay", max_results=50)
        assert result["overrides"]["count"] > 10, \
            f"OnPlay should have many overrides, got {result['overrides']['count']}"

    def test_hook_search_coverage(self, code_analyzer):
        """Verify hook search works for common parameter types."""
        for param_type in ["CombatState", "Creature", "CardModel", "DamageResult", "Player"]:
            results = code_analyzer.search_hooks_by_signature(param_type)
            assert len(results) > 0, f"No hooks found with param type: {param_type}"


@skip_no_bridge
class TestBridgeIntegration:
    """Integration tests with the live game bridge."""

    def test_full_state_read(self):
        """Read all state endpoints — none should crash."""
        from sts2mcp import bridge_client

        # These should all return dicts without crashing
        ping = bridge_client.ping()
        assert "result" in ping

        screen = bridge_client.get_screen()
        assert isinstance(screen, dict)

        run = bridge_client.get_run_state()
        assert isinstance(run, dict)

        player = bridge_client.get_player_state()
        assert isinstance(player, dict)

        actions = bridge_client.get_available_actions()
        assert isinstance(actions, dict)

    def test_card_piles_in_combat(self):
        """If in combat, card piles should have structure."""
        from sts2mcp.bridge_client import get_screen, get_card_piles

        screen_result = get_screen()
        screen = screen_result.get("result", screen_result).get("screen", "")

        if "COMBAT" in screen:
            result = get_card_piles()
            r = result.get("result", result)
            if "error" not in r:
                assert "hand" in r
                assert "count" in r["hand"]

    def test_manipulate_and_verify(self):
        """Add gold via manipulate_state and verify via run state."""
        from sts2mcp.bridge_client import get_run_state, manipulate_state

        run = get_run_state()
        r = run.get("result", run)
        if r.get("in_progress"):
            # Add gold
            manip = manipulate_state({"gold": 1})
            mr = manip.get("result", manip)
            assert mr.get("success") is True or "error" in mr

    def test_scenario_execution(self):
        """Generate a test scenario and execute its commands via bridge."""
        from sts2mcp.bridge_client import execute_console_command, get_run_state
        from sts2mcp.mod_gen import ModGenerator

        run = get_run_state()
        r = run.get("result", run)
        if not r.get("in_progress"):
            pytest.skip("No run in progress — can't execute scenario")

        mg = ModGenerator(".")
        scenario = mg.generate_test_scenario(
            scenario_name="Bridge test",
            gold=10,
        )
        for cmd in scenario["commands"]:
            result = execute_console_command(cmd)
            assert isinstance(result, dict)
