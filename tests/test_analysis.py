"""Tests for code analysis and intelligence tools.

These tests require the decompiled source to exist (auto-skipped if not present).
"""

import pytest

from tests.conftest import skip_no_decompiled


# ─── Suggest Patches ──────────────────────────────────────────────────────────


@skip_no_decompiled
class TestSuggestPatches:
    def test_damage_query(self, code_analyzer):
        result = code_analyzer.suggest_patches("make all attacks deal more damage")
        assert "suggestions" in result
        assert result["suggestion_count"] > 0, \
            f"Expected suggestions for damage query, got: {result}"
        # Should find damage-related methods
        targets = [s["target_class"] for s in result["suggestions"]]
        all_text = str(result["suggestions"]).lower()
        assert "damage" in all_text or "attack" in all_text or "card" in all_text, \
            f"Expected damage-related suggestions, got: {targets}"

    def test_energy_query(self, code_analyzer):
        result = code_analyzer.suggest_patches("reduce card energy cost")
        assert result["suggestion_count"] > 0, \
            f"Expected suggestions for energy query, got: {result}"

    def test_draw_query(self, code_analyzer):
        result = code_analyzer.suggest_patches("draw extra cards each turn")
        assert result["suggestion_count"] > 0, \
            f"Expected suggestions for draw query, got: {result}"

    def test_gibberish_query(self, code_analyzer):
        result = code_analyzer.suggest_patches("xyzzyflorp")
        # Should not crash, may return 0 suggestions
        assert "suggestions" in result

    def test_max_suggestions_respected(self, code_analyzer):
        result = code_analyzer.suggest_patches("damage", max_suggestions=3)
        assert result["suggestion_count"] <= 3

    def test_suggestion_structure(self, code_analyzer):
        result = code_analyzer.suggest_patches("block")
        if result["suggestion_count"] > 0:
            s = result["suggestions"][0]
            assert "target_class" in s
            assert "target_method" in s
            assert "patch_type" in s
            assert s["patch_type"] in ("Prefix", "Postfix")
            assert "rationale" in s


# ─── Analyze Method Callers ───────────────────────────────────────────────────


@skip_no_decompiled
class TestAnalyzeMethodCallers:
    def test_known_method(self, code_analyzer):
        result = code_analyzer.analyze_method_callers("CardModel", "OnPlay")
        assert "callers" in result
        assert "callees" in result
        assert "overrides" in result
        # OnPlay should have overrides (every card overrides it)
        assert result["overrides"]["count"] > 0

    def test_callers_found(self, code_analyzer):
        result = code_analyzer.analyze_method_callers("DamageCmd", "Attack")
        assert result["callers"]["count"] > 0, "DamageCmd.Attack should have callers"

    def test_unknown_method(self, code_analyzer):
        result = code_analyzer.analyze_method_callers("NonExistentClass", "FakeMethod")
        # Should not crash
        assert "callers" in result


# ─── Entity Relationships ─────────────────────────────────────────────────────


@skip_no_decompiled
class TestGetEntityRelationships:
    def test_card_relationships(self, code_analyzer):
        # Bash is a basic Ironclad card
        result = code_analyzer.get_entity_relationships("Bash")
        assert "entity" in result
        assert result["entity"] == "Bash"
        # Bash applies Vulnerable power
        if "applies_powers" in result:
            assert any("Vulnerable" in p for p in result["applies_powers"]), \
                f"Bash should apply Vulnerable, got: {result.get('applies_powers', [])}"

    def test_power_relationships(self, code_analyzer):
        result = code_analyzer.get_entity_relationships("StrengthPower")
        assert "entity" in result
        # Strength implements ModifyDamage hooks
        if "hooks_used" in result:
            assert any("Damage" in h for h in result["hooks_used"])

    def test_unknown_entity(self, code_analyzer):
        result = code_analyzer.get_entity_relationships("NonExistentEntity999")
        assert "error" in result

    def test_has_commands(self, code_analyzer):
        """Cards that do damage should use DamageCmd."""
        result = code_analyzer.get_entity_relationships("Bash")
        if "uses_commands" in result:
            assert any("Cmd" in c for c in result["uses_commands"])


# ─── Search Hooks By Signature ────────────────────────────────────────────────


@skip_no_decompiled
class TestSearchHooksBySignature:
    def test_combat_state(self, code_analyzer):
        results = code_analyzer.search_hooks_by_signature("CombatState")
        assert len(results) > 0
        for hook in results:
            assert "CombatState" in hook["params"]

    def test_damage_result(self, code_analyzer):
        results = code_analyzer.search_hooks_by_signature("DamageResult")
        assert len(results) > 0

    def test_card_model(self, code_analyzer):
        results = code_analyzer.search_hooks_by_signature("CardModel")
        assert len(results) > 0

    def test_nonexistent_type(self, code_analyzer):
        results = code_analyzer.search_hooks_by_signature("NonExistentType999")
        assert len(results) == 0

    def test_case_insensitive(self, code_analyzer):
        results_lower = code_analyzer.search_hooks_by_signature("combatstate")
        results_proper = code_analyzer.search_hooks_by_signature("CombatState")
        # Both should find results (search is case-insensitive)
        assert len(results_lower) == len(results_proper)


# ─── Validate Mod ─────────────────────────────────────────────────────────────


@skip_no_decompiled
class TestValidateMod:
    def test_nonexistent_dir(self, code_analyzer):
        result = code_analyzer.validate_mod("/nonexistent/path/to/mod")
        assert result["valid"] is False
        assert len(result["errors"]) > 0

    def test_empty_dir(self, code_analyzer, tmp_mod_dir):
        result = code_analyzer.validate_mod(tmp_mod_dir)
        assert result["valid"] is False
        assert any("manifest" in e.lower() for e in result["errors"])
        assert any("csproj" in e.lower() for e in result["errors"])
        assert any("ModInitializer" in e for e in result["errors"])

    def test_valid_project(self, code_analyzer, mod_gen, tmp_mod_dir):
        """Create a real project and validate it."""
        mod_gen.create_mod_project(
            mod_name="TestValidation",
            author="TestAuthor",
            output_dir=tmp_mod_dir,
        )
        result = code_analyzer.validate_mod(tmp_mod_dir)
        # Should have no errors (warnings are OK)
        assert result["valid"] is True, f"Errors: {result['errors']}"


# ─── Diff Game Versions ───────────────────────────────────────────────────────


@skip_no_decompiled
class TestDiffGameVersions:
    def test_same_dir(self, code_analyzer):
        """Diffing a dir against itself should show no changes."""
        from tests.conftest import DECOMPILED_DIR
        result = code_analyzer.diff_game_versions(DECOMPILED_DIR, DECOMPILED_DIR)
        assert result["summary"]["added_files"] == 0
        assert result["summary"]["removed_files"] == 0
        assert result["summary"]["modified_files"] == 0

    def test_nonexistent_dir(self, code_analyzer):
        result = code_analyzer.diff_game_versions("/fake/old", "/fake/new")
        assert "error" in result

    def test_one_real_one_fake(self, code_analyzer):
        from tests.conftest import DECOMPILED_DIR
        result = code_analyzer.diff_game_versions(DECOMPILED_DIR, "/fake/new")
        assert "error" in result


# ─── Check Mod Compatibility ─────────────────────────────────────────────────


@skip_no_decompiled
class TestCheckModCompatibility:
    def test_nonexistent_project(self, code_analyzer):
        result = code_analyzer.check_mod_compatibility("/nonexistent/project")
        assert "error" in result

    def test_valid_project(self, code_analyzer, mod_gen, tmp_mod_dir):
        """A freshly generated project should be compatible."""
        mod_gen.create_mod_project(
            mod_name="CompatTest",
            author="Test",
            output_dir=tmp_mod_dir,
        )
        result = code_analyzer.check_mod_compatibility(tmp_mod_dir)
        assert result["compatible"] is True
        assert len(result["errors"]) == 0


# ─── List Game VFX ────────────────────────────────────────────────────────────


@skip_no_decompiled
class TestListGameVfx:
    def test_unfiltered(self, code_analyzer):
        results = code_analyzer.list_game_vfx()
        assert isinstance(results, list)
        # Game should have some VFX-related code
        assert len(results) > 0

    def test_with_query(self, code_analyzer):
        results = code_analyzer.list_game_vfx(query="particle")
        assert isinstance(results, list)
        # All results should relate to the query
        for r in results:
            assert "particle" in str(r).lower()
