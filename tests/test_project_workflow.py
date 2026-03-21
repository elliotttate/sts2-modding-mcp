import json
from pathlib import Path

from tests.conftest import skip_no_dotnet, skip_no_game


def _create_project(mod_gen, tmp_path: Path, mod_name: str, *, use_baselib: bool = False) -> tuple[Path, dict]:
    project_dir = tmp_path / mod_name
    result = mod_gen.create_mod_project(
        mod_name=mod_name,
        author="Test Runner",
        description="Workflow test project",
        output_dir=str(project_dir),
        use_baselib=use_baselib,
    )
    assert result["success"] is True
    return Path(result["project_dir"]), result


class TestApplyGeneratorOutputs:
    def test_apply_supports_entries_localization_payload(self, mod_gen, tmp_path):
        project_dir, project = _create_project(mod_gen, tmp_path, "Loc Workflow")

        localization = mod_gen.generate_localization(
            mod_id=project["mod_id"],
            entity_type="card",
            entity_name="EntryCard",
            title="Entry Card",
            description="Apply workflow localization.",
        )

        result = mod_gen.apply_generator_output(project_dir, localization)
        assert result["success"] is True

        cards_path = project_dir / project["namespace"] / "localization" / "eng" / "cards.json"
        payload = json.loads(cards_path.read_text(encoding="utf-8"))
        assert f"{project['mod_id'].upper()}-ENTRY_CARD.title" in payload
        assert payload[f"{project['mod_id'].upper()}-ENTRY_CARD.description"] == "Apply workflow localization."

    def test_apply_rejects_path_escape(self, mod_gen, tmp_path):
        project_dir, _ = _create_project(mod_gen, tmp_path, "Escape Workflow")
        escaped_target = project_dir.parent / "Escaped.cs"

        result = mod_gen.apply_generator_output(
            project_dir,
            {
                "source": "namespace Test; public sealed class Escaped { }",
                "file_name": "../../Escaped.cs",
                "folder": "Code",
            },
        )

        assert result["success"] is False
        assert any("escapes the project root" in conflict for conflict in result["conflicts"])
        assert escaped_target.exists() is False

    def test_dry_run_has_no_side_effects(self, mod_gen, tmp_path):
        project_dir, _ = _create_project(mod_gen, tmp_path, "Dry Run Workflow")
        output = mod_gen.generate_card(
            mod_namespace="DryRunWorkflow",
            class_name="DryRunStrike",
            card_type="Attack",
            damage=6,
            energy_cost=1,
            use_baselib=False,
        )

        result = mod_gen.apply_generator_output(project_dir, output, dry_run=True)
        assert result["success"] is True
        assert "Code/Cards/DryRunStrike.cs" in result["written_files"]
        assert (project_dir / "Code" / "Cards" / "DryRunStrike.cs").exists() is False

    def test_apply_is_transactional_on_conflict(self, mod_gen, tmp_path):
        project_dir, project = _create_project(mod_gen, tmp_path, "Transactional Workflow")
        output = mod_gen.generate_card(
            mod_namespace=project["namespace"],
            class_name="ConflictStrike",
            card_type="Attack",
            damage=7,
            energy_cost=1,
            use_baselib=False,
        )

        cards_path = project_dir / project["namespace"] / "localization" / "eng" / "cards.json"
        cards_path.write_text(
            json.dumps({"CONFLICT_STRIKE.title": "Existing Title"}, indent=2) + "\n",
            encoding="utf-8",
        )

        result = mod_gen.apply_generator_output(project_dir, output)
        assert result["success"] is False
        assert any("Refusing to overwrite localization key" in conflict for conflict in result["conflicts"])
        assert (project_dir / "Code" / "Cards" / "ConflictStrike.cs").exists() is False

        persisted = json.loads(cards_path.read_text(encoding="utf-8"))
        assert persisted["CONFLICT_STRIKE.title"] == "Existing Title"

    def test_project_edits_patch_mod_entry(self, mod_gen, tmp_path):
        project_dir, project = _create_project(mod_gen, tmp_path, "Project Edits Workflow")
        output = mod_gen.generate_settings_panel(
            mod_namespace=project["namespace"],
            class_name="ModSettings",
            mod_id=project["mod_id"],
        )

        result = mod_gen.apply_generator_output(project_dir, output)
        assert result["success"] is True

        mod_entry = (project_dir / "Code" / "ModEntry.cs").read_text(encoding="utf-8")
        assert f"using {project['namespace']}.Config;" in mod_entry
        assert "ModSettings.Initialize();" in mod_entry
        assert (project_dir / "Code" / "Config" / "ModSettings.cs").exists() is True


class TestProjectBuilds:
    @skip_no_game
    @skip_no_dotnet
    def test_create_mod_project_writes_buildable_scaffold(self, mod_gen, tmp_path):
        project_dir, project = _create_project(mod_gen, tmp_path, "Tmp Scaffold", use_baselib=False)

        manifest = json.loads((project_dir / "mod_manifest.json").read_text(encoding="utf-8"))
        mod_entry = (project_dir / "Code" / "ModEntry.cs").read_text(encoding="utf-8")

        assert manifest["id"] == "tmpscaffold"
        assert 'new Harmony("com.testrunner.tmpscaffold")' in mod_entry
        assert (project_dir / "NuGet.config").exists() is True

        build_result = mod_gen.build_mod(str(project_dir), configuration="Debug")
        assert build_result["success"] is True, build_result["stderr"] or build_result["stdout"]

    @skip_no_game
    @skip_no_dotnet
    def test_formerly_stale_generators_compile_in_fresh_project(self, mod_gen, tmp_path):
        project_dir, project = _create_project(mod_gen, tmp_path, "Compile Smoke", use_baselib=False)
        ns = project["namespace"]

        outputs = [
            mod_gen.generate_event(
                mod_namespace=ns,
                class_name="CompileEvent",
                choices=[
                    {"label": "Accept", "method_name": "ChoiceAccept", "effect_description": "Gain gold."},
                    {"label": "Leave", "method_name": "ChoiceLeave"},
                ],
            ),
            mod_gen.generate_orb(
                mod_namespace=ns,
                class_name="CompileOrb",
                passive_amount=2,
                evoke_amount=6,
            ),
            mod_gen.generate_enchantment(
                mod_namespace=ns,
                class_name="CompileEnchantment",
                trigger_hook="AfterCardPlayed",
            ),
            mod_gen.generate_game_action(
                mod_namespace=ns,
                class_name="CompileAction",
                parameters=[
                    {"name": "player", "type": "Player"},
                    {"name": "amount", "type": "int"},
                ],
            ),
            mod_gen.generate_custom_tooltip(
                mod_namespace=ns,
                tag_name="compile_tip",
                title="Compile Tip",
                tooltip_description="Tooltip helper smoke test.",
            ),
            mod_gen.generate_net_message(
                mod_namespace=ns,
                class_name="CompileMessage",
                fields=[
                    {"name": "Data", "type": "string"},
                    {"name": "Amount", "type": "int"},
                ],
            ),
            mod_gen.generate_settings_panel(
                mod_namespace=ns,
                class_name="CompileSettings",
                mod_id=project["mod_id"],
            ),
        ]

        apply_result = mod_gen.apply_generator_outputs(project_dir, outputs)
        assert apply_result["success"] is True, apply_result

        build_result = mod_gen.build_mod(str(project_dir), configuration="Debug")
        assert build_result["success"] is True, build_result["stderr"] or build_result["stdout"]
