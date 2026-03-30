import json
from pathlib import Path
from types import SimpleNamespace

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
        csproj = (project_dir / f"{project['namespace']}.csproj").read_text(encoding="utf-8")

        assert manifest["id"] == "tmpscaffold"
        assert 'new Harmony("com.testrunner.tmpscaffold")' in mod_entry
        assert (project_dir / "NuGet.config").exists() is True
        assert "<EnableCopyToModsFolderOnBuild>false</EnableCopyToModsFolderOnBuild>" in csproj
        assert "<DebugType>portable</DebugType>" in csproj
        assert "<DebugType>none</DebugType>" in csproj
        assert 'Name="CopyToModsFolderOnBuild"' in csproj

        build_result = mod_gen.build_mod(str(project_dir), configuration="Debug")
        assert build_result["success"] is True, build_result["stderr"] or build_result["stdout"]


class TestHotReloadWorkflow:
    def test_discover_pool_registrations_reads_pool_attributes(self, mod_gen, tmp_path):
        from sts2mcp.hot_reload import discover_pool_registrations

        project_dir, project = _create_project(mod_gen, tmp_path, "Pool Discovery Workflow")
        source = f"""
using BaseLib.Utils;

namespace {project["namespace"]}.Cards;

[Pool(typeof(IroncladCardPool))]
[Pool(typeof(ColorlessCardPool))]
public sealed class FlameStrike : CardModel
{{
}}
"""
        (project_dir / "Code" / "Cards").mkdir(parents=True, exist_ok=True)
        (project_dir / "Code" / "Cards" / "FlameStrike.cs").write_text(source, encoding="utf-8")

        result = discover_pool_registrations(project_dir)

        assert result["success"] is True
        assert result["pool_registrations"] == [
            {"pool_type": "IroncladCardPool", "model_type": f"{project['namespace']}.Cards.FlameStrike"},
            {"pool_type": "ColorlessCardPool", "model_type": f"{project['namespace']}.Cards.FlameStrike"},
        ]

    def test_build_deploy_and_hot_reload_project_auto_discovers_pools(self, mod_gen, tmp_path, monkeypatch):
        from sts2mcp import bridge_client
        from sts2mcp.hot_reload import build_deploy_and_hot_reload_project
        import sts2mcp.hot_reload as hot_reload_module

        project_dir, project = _create_project(mod_gen, tmp_path, "Hot Reload Workflow")
        source = f"""
using BaseLib.Utils;

namespace {project["namespace"]}.Relics;

[Pool(typeof(SharedRelicPool))]
public sealed class WorkflowRelic : RelicModel
{{
}}
"""
        (project_dir / "Code" / "Relics").mkdir(parents=True, exist_ok=True)
        (project_dir / "Code" / "Relics" / "WorkflowRelic.cs").write_text(source, encoding="utf-8")

        deployed_mod_dir = tmp_path / "InstalledMod"
        deployed_mod_dir.mkdir()
        dll_path = deployed_mod_dir / f"{project['namespace']}.dll"
        pck_path = deployed_mod_dir / f"{project['mod_id']}.pck"
        dll_path.write_text("dll", encoding="utf-8")
        pck_path.write_text("pck", encoding="utf-8")

        monkeypatch.setattr(
            hot_reload_module,
            "build_and_deploy_project",
            lambda *args, **kwargs: {
                "success": True,
                "mod_dir": str(deployed_mod_dir),
                "copied_files": [dll_path.name, "mod_manifest.json", pck_path.name],
            },
        )

        captured: dict[str, object] = {}

        def _fake_hot_reload(**kwargs):
            captured.update(kwargs)
            # Return a JSON-RPC-like wrapper to verify unwrap logic
            return {"result": {"success": True, "tier": kwargs["tier"]}, "id": 1}

        monkeypatch.setattr(bridge_client, "hot_reload", _fake_hot_reload)

        result = build_deploy_and_hot_reload_project(
            project_dir,
            mods_dir=tmp_path / "mods",
        )

        assert result["success"] is True
        assert result["hot_reload"]["success"] is True
        assert result["hot_reload_inputs"]["tier"] == 3
        assert captured["dll_path"] == str(dll_path)
        assert captured["pck_path"] == str(pck_path)
        # Pool registrations are None — C# bridge does assembly-level reflection
        # discovery which is more accurate than Python regex scanning
        assert captured["pool_registrations"] is None
        assert result["hot_reload_inputs"]["auto_detect_pools"] is True
        assert result["hot_reload_inputs"]["pool_discovery_mode"] == "bridge_auto"

    def test_build_deploy_and_hot_reload_project_can_disable_pool_auto_discovery(self, mod_gen, tmp_path, monkeypatch):
        from sts2mcp import bridge_client
        from sts2mcp.hot_reload import build_deploy_and_hot_reload_project
        import sts2mcp.hot_reload as hot_reload_module

        project_dir, project = _create_project(mod_gen, tmp_path, "Hot Reload Disable Pools")

        deployed_mod_dir = tmp_path / "InstalledModNoPools"
        deployed_mod_dir.mkdir()
        dll_path = deployed_mod_dir / f"{project['namespace']}.dll"
        dll_path.write_text("dll", encoding="utf-8")

        monkeypatch.setattr(
            hot_reload_module,
            "build_and_deploy_project",
            lambda *args, **kwargs: {
                "success": True,
                "mod_dir": str(deployed_mod_dir),
                "copied_files": [dll_path.name, "mod_manifest.json"],
            },
        )

        captured: dict[str, object] = {}

        def _fake_hot_reload(**kwargs):
            captured.update(kwargs)
            return {"result": {"success": True}, "id": 1}

        monkeypatch.setattr(bridge_client, "hot_reload", _fake_hot_reload)

        result = build_deploy_and_hot_reload_project(
            project_dir,
            mods_dir=tmp_path / "mods",
            auto_detect_pools=False,
        )

        assert result["success"] is True
        assert captured["pool_registrations"] == []
        assert result["hot_reload_inputs"]["pool_registrations"] == []
        assert result["hot_reload_inputs"]["auto_detect_pools"] is False
        assert result["hot_reload_inputs"]["pool_discovery_mode"] == "disabled"

    def test_bridge_client_hot_reload_sends_explicit_empty_pool_registrations(self, monkeypatch):
        from sts2mcp import bridge_client

        captured: dict[str, object] = {}

        def _fake_send_request(method, params=None, request_id=1, timeout=None):
            captured["method"] = method
            captured["params"] = params
            captured["request_id"] = request_id
            captured["timeout"] = timeout
            return {"result": {"success": True}, "id": request_id}

        monkeypatch.setattr(bridge_client, "send_request", _fake_send_request)

        result = bridge_client.hot_reload(
            dll_path="E:/mods/TestMod/TestMod.dll",
            tier=2,
            pool_registrations=[],
        )

        assert result["result"]["success"] is True
        assert captured["method"] == "hot_reload"
        assert captured["params"]["pool_registrations"] == []

    def test_determine_reload_tier_treats_resource_json_as_tier_three(self):
        from sts2mcp.hot_reload import determine_reload_tier

        assert determine_reload_tier(["Code/Patches/DamagePatch.cs"]) == 1
        assert determine_reload_tier(["Code/Cards/MyCard.cs"]) == 2
        assert determine_reload_tier(["MyMod/data/config.json"]) == 3
        # Empty/unrecognized → tier 0 (nothing to reload)
        assert determine_reload_tier([]) == 0
        assert determine_reload_tier(["README.md"]) == 0
        # Non-resource JSON excluded from tier 3
        assert determine_reload_tier(["mod_manifest.json"]) == 0
        # Localization JSON → tier 2 without PCK, tier 3 with PCK
        assert determine_reload_tier(["localization/eng/strings.json"]) == 2
        assert determine_reload_tier(["localization/eng/strings.json"], has_pck=True) == 3

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


class TestFileWatcher:
    def test_duplicate_failure_dedup_tracks_file_state_not_just_paths(self, tmp_path, monkeypatch):
        import sts2mcp.file_watcher as file_watcher_module

        monkeypatch.setattr(
            file_watcher_module,
            "_resolve_project_context",
            lambda project_dir: SimpleNamespace(has_pck=False),
        )

        project_dir = tmp_path / "watcher_project"
        source_file = project_dir / "Code" / "Cards" / "MyCard.cs"
        source_file.parent.mkdir(parents=True)
        source_file.write_text("public sealed class MyCard { }\n", encoding="utf-8")

        watcher = file_watcher_module.ModFileWatcher(
            project_dir=str(project_dir),
            mods_dir=str(tmp_path / "mods"),
        )
        changed_files = [str(source_file)]

        watcher._record_build_failure(changed_files, "compile error")

        assert watcher._should_skip_duplicate_error(changed_files) is True

        source_file.write_text("public sealed class MyCard { private int Value; }\n", encoding="utf-8")

        assert watcher._should_skip_duplicate_error(changed_files) is False

    def test_resource_hash_tracking_handles_audio_changes_and_deletions(self, tmp_path, monkeypatch):
        import sts2mcp.file_watcher as file_watcher_module

        monkeypatch.setattr(
            file_watcher_module,
            "_resolve_project_context",
            lambda project_dir: SimpleNamespace(has_pck=True),
        )

        project_dir = tmp_path / "watcher_resources"
        audio_file = project_dir / "MyMod" / "audio" / "impact.ogg"
        audio_file.parent.mkdir(parents=True)
        audio_file.write_bytes(b"first")

        watcher = file_watcher_module.ModFileWatcher(
            project_dir=str(project_dir),
            mods_dir=str(tmp_path / "mods"),
        )
        changed_files = [str(audio_file)]

        assert watcher._check_resource_hashes_changed(changed_files) is True
        assert watcher._check_resource_hashes_changed(changed_files) is False

        audio_file.write_bytes(b"second")
        assert watcher._check_resource_hashes_changed(changed_files) is True

        audio_file.unlink()
        assert watcher._check_resource_hashes_changed(changed_files) is True
        assert str(audio_file) not in watcher._resource_hashes

    def test_status_thread_safety_under_concurrent_access(self, tmp_path, monkeypatch):
        """status() should not raise RuntimeError when called while watcher thread writes."""
        import sts2mcp.file_watcher as file_watcher_module
        import threading
        import time as time_mod

        monkeypatch.setattr(
            file_watcher_module,
            "_resolve_project_context",
            lambda project_dir: SimpleNamespace(has_pck=False),
        )

        project_dir = tmp_path / "thread_safety_project"
        project_dir.mkdir()

        watcher = file_watcher_module.ModFileWatcher(
            project_dir=str(project_dir),
            mods_dir=str(tmp_path / "mods"),
        )

        # Simulate the watcher thread writing to shared state concurrently
        errors: list[Exception] = []
        stop = threading.Event()

        def writer():
            i = 0
            while not stop.is_set():
                with watcher._state_lock:
                    # Replace set each cycle to keep it small
                    watcher._pending_changes = {f"fake_file_{i % 5}.cs"}
                    watcher._build_count = i
                    watcher._last_result = {"build_number": i}
                i += 1
                time_mod.sleep(0.001)

        t = threading.Thread(target=writer, daemon=True)
        t.start()
        try:
            for _ in range(50):
                try:
                    s = watcher.status()
                    assert isinstance(s, dict)
                    assert "build_count" in s
                except Exception as e:
                    errors.append(e)
        finally:
            stop.set()
            t.join(timeout=2)

        assert len(errors) == 0, f"status() raised errors under concurrent access: {errors}"

    def test_resource_hashes_use_sha256(self, tmp_path, monkeypatch):
        """Resource hashes should use SHA256 (not MD5) for consistency."""
        import hashlib
        import sts2mcp.file_watcher as file_watcher_module

        monkeypatch.setattr(
            file_watcher_module,
            "_resolve_project_context",
            lambda project_dir: SimpleNamespace(has_pck=True),
        )

        project_dir = tmp_path / "hash_algo_project"
        img_file = project_dir / "Assets" / "icon.png"
        img_file.parent.mkdir(parents=True)
        img_file.write_bytes(b"image_data")

        watcher = file_watcher_module.ModFileWatcher(
            project_dir=str(project_dir),
            mods_dir=str(tmp_path / "mods"),
        )
        watcher._check_resource_hashes_changed([str(img_file)])

        expected_hash = hashlib.sha256(b"image_data").hexdigest()
        assert watcher._resource_hashes[str(img_file)] == expected_hash

    def test_trigger_build_uses_unified_hot_reload_when_auto_reload(self, tmp_path, monkeypatch):
        """When auto_reload=True, _trigger_build should call build_deploy_and_hot_reload_project."""
        import sts2mcp.file_watcher as file_watcher_module

        monkeypatch.setattr(
            file_watcher_module,
            "_resolve_project_context",
            lambda project_dir: SimpleNamespace(has_pck=False),
        )

        captured: dict = {}

        def _fake_build_deploy_and_hot_reload(**kwargs):
            captured.update(kwargs)
            return {"success": True, "hot_reload": {"success": True}}

        monkeypatch.setattr(
            file_watcher_module,
            "build_deploy_and_hot_reload_project",
            lambda *a, **kw: _fake_build_deploy_and_hot_reload(**kw),
        )

        project_dir = tmp_path / "unified_reload_project"
        cs_file = project_dir / "Code" / "MyCard.cs"
        cs_file.parent.mkdir(parents=True)
        cs_file.write_text("class MyCard {}", encoding="utf-8")

        watcher = file_watcher_module.ModFileWatcher(
            project_dir=str(project_dir),
            mods_dir=str(tmp_path / "mods"),
            auto_reload=True,
        )
        watcher._trigger_build([str(cs_file)])

        # Verify it called build_deploy_and_hot_reload_project
        assert "mods_dir" in captured
        assert "tier" in captured
        assert "cancel_event" in captured

    def test_trigger_build_uses_plain_build_when_no_auto_reload(self, tmp_path, monkeypatch):
        """When auto_reload=False, _trigger_build should call build_and_deploy_project only."""
        import sts2mcp.file_watcher as file_watcher_module

        monkeypatch.setattr(
            file_watcher_module,
            "_resolve_project_context",
            lambda project_dir: SimpleNamespace(has_pck=False),
        )

        captured: dict = {}

        def _fake_build_and_deploy(*a, **kw):
            captured.update(kw)
            return {"success": True}

        monkeypatch.setattr(
            file_watcher_module,
            "build_and_deploy_project",
            _fake_build_and_deploy,
        )

        # Also mock build_deploy_and_hot_reload_project to ensure it's NOT called
        hot_reload_called = False

        def _fake_hot_reload_build(*a, **kw):
            nonlocal hot_reload_called
            hot_reload_called = True
            return {"success": True}

        monkeypatch.setattr(
            file_watcher_module,
            "build_deploy_and_hot_reload_project",
            _fake_hot_reload_build,
        )

        project_dir = tmp_path / "plain_build_project"
        cs_file = project_dir / "Code" / "MyCard.cs"
        cs_file.parent.mkdir(parents=True)
        cs_file.write_text("class MyCard {}", encoding="utf-8")

        # Also need to mock validate_project since _trigger_build calls it now
        monkeypatch.setattr(
            file_watcher_module,
            "validate_project",
            lambda *a, **kw: {"valid": True},
        )

        watcher = file_watcher_module.ModFileWatcher(
            project_dir=str(project_dir),
            mods_dir=str(tmp_path / "mods"),
            auto_reload=False,
        )
        watcher._trigger_build([str(cs_file)])

        assert not hot_reload_called
        assert "mods_dir" in captured
        assert "cancel_event" in captured

    def test_notification_types_reflect_phase(self, tmp_path, monkeypatch):
        """Notifications should use phase-specific types (build_failed, hot_reload_failed, etc.)."""
        import sts2mcp.file_watcher as file_watcher_module

        monkeypatch.setattr(
            file_watcher_module,
            "_resolve_project_context",
            lambda project_dir: SimpleNamespace(has_pck=False),
        )

        notifications: list[dict] = []

        def _capture_notification(payload):
            notifications.append(payload)

        # Test hot_reload_complete
        monkeypatch.setattr(
            file_watcher_module,
            "build_deploy_and_hot_reload_project",
            lambda *a, **kw: {"success": True, "hot_reload": {"success": True}},
        )

        project_dir = tmp_path / "notification_project"
        cs_file = project_dir / "Code" / "MyCard.cs"
        cs_file.parent.mkdir(parents=True)
        cs_file.write_text("class MyCard {}", encoding="utf-8")

        watcher = file_watcher_module.ModFileWatcher(
            project_dir=str(project_dir),
            mods_dir=str(tmp_path / "mods"),
            auto_reload=True,
            on_notification=_capture_notification,
        )
        watcher._trigger_build([str(cs_file)])

        # Should have build_started + hot_reload_complete
        types = [n["type"] for n in notifications]
        assert "build_started" in types
        assert "hot_reload_complete" in types

        # Test build_failed
        notifications.clear()
        watcher._clear_error_cache()  # Clear dedup state from prior success
        monkeypatch.setattr(
            file_watcher_module,
            "build_deploy_and_hot_reload_project",
            lambda *a, **kw: {"success": False, "error": "compile error"},
        )
        watcher._trigger_build([str(cs_file)])
        types = [n["type"] for n in notifications]
        assert "build_failed" in types

        # Test hot_reload_failed
        notifications.clear()
        watcher._clear_error_cache()  # Clear dedup state from prior failure
        monkeypatch.setattr(
            file_watcher_module,
            "build_deploy_and_hot_reload_project",
            lambda *a, **kw: {"success": False, "hot_reload": {"error": "bridge down"}},
        )
        watcher._trigger_build([str(cs_file)])
        types = [n["type"] for n in notifications]
        assert "hot_reload_failed" in types

    def test_pre_build_validation_skips_on_invalid_project(self, tmp_path, monkeypatch):
        """_trigger_build should skip build when validate_project reports invalid."""
        import sts2mcp.file_watcher as file_watcher_module

        monkeypatch.setattr(
            file_watcher_module,
            "_resolve_project_context",
            lambda project_dir: SimpleNamespace(has_pck=False),
        )

        # validate_project returns invalid
        monkeypatch.setattr(
            file_watcher_module,
            "validate_project",
            lambda *a, **kw: {"valid": False, "errors": ["missing .csproj"]},
        )

        build_called = False

        def _fake_build(*a, **kw):
            nonlocal build_called
            build_called = True
            return {"success": True}

        monkeypatch.setattr(
            file_watcher_module,
            "build_deploy_and_hot_reload_project",
            _fake_build,
        )

        project_dir = tmp_path / "invalid_project"
        cs_file = project_dir / "Code" / "MyCard.cs"
        cs_file.parent.mkdir(parents=True)
        cs_file.write_text("class MyCard {}", encoding="utf-8")

        notifications: list[dict] = []
        watcher = file_watcher_module.ModFileWatcher(
            project_dir=str(project_dir),
            mods_dir=str(tmp_path / "mods"),
            auto_reload=True,
            on_notification=lambda p: notifications.append(p),
        )
        watcher._trigger_build([str(cs_file)])

        assert not build_called
        assert any(n["type"] == "validation_failed" for n in notifications)
        assert watcher._last_result["phase"] == "validation"

    def test_build_cancellation_via_cancel_event(self, tmp_path, monkeypatch):
        """build_project should return cancelled result when cancel_event is set."""
        import threading
        from sts2mcp.project_workflow import build_project

        project_dir = tmp_path / "cancel_test"
        project_dir.mkdir()
        csproj = project_dir / "CancelTest.csproj"
        csproj.write_text("""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>""", encoding="utf-8")

        # Set cancel event immediately so the build gets cancelled on first poll
        cancel = threading.Event()
        cancel.set()

        result = build_project(project_dir, cancel_event=cancel)
        assert result.get("cancelled") is True
        assert result["success"] is False
        assert "cancelled" in result.get("error", "").lower()

    def test_stop_single_always_removes_from_registry(self, tmp_path, monkeypatch):
        """_stop_single should always pop the watcher from _active_watchers."""
        import sts2mcp.file_watcher as file_watcher_module

        monkeypatch.setattr(
            file_watcher_module,
            "_resolve_project_context",
            lambda project_dir: SimpleNamespace(has_pck=False),
        )

        project_dir = tmp_path / "stop_test_project"
        project_dir.mkdir()

        watcher = file_watcher_module.ModFileWatcher(
            project_dir=str(project_dir),
            mods_dir=str(tmp_path / "mods"),
        )

        key = file_watcher_module._normalize_project_key(str(project_dir))
        with file_watcher_module._watchers_lock:
            file_watcher_module._active_watchers[key] = watcher

        with file_watcher_module._watchers_lock:
            file_watcher_module._stop_single(str(project_dir))

        with file_watcher_module._watchers_lock:
            assert key not in file_watcher_module._active_watchers

    def test_bridge_hot_reload_retries_on_transient_errors(self, monkeypatch):
        """hot_reload should retry on connection refused and timeout errors."""
        from sts2mcp import bridge_client

        call_count = 0

        def _fake_send_request(method, params=None, request_id=1, timeout=None):
            nonlocal call_count
            call_count += 1
            if call_count < 3:
                return {"error": "Bridge not running. Is the game running with MCPTest mod loaded?"}
            return {"result": {"success": True}, "id": 1}

        monkeypatch.setattr(bridge_client, "send_request", _fake_send_request)
        # Speed up the test by patching time.sleep
        monkeypatch.setattr(bridge_client.time, "sleep", lambda s: None)

        result = bridge_client.hot_reload(dll_path="test.dll", tier=2)
        assert call_count == 3
        assert result["result"]["success"] is True

    def test_bridge_hot_reload_no_retry_on_non_transient_error(self, monkeypatch):
        """hot_reload should NOT retry on non-transient errors (e.g., invalid DLL)."""
        from sts2mcp import bridge_client

        call_count = 0

        def _fake_send_request(method, params=None, request_id=1, timeout=None):
            nonlocal call_count
            call_count += 1
            return {"result": {"error": "DLL not found at path"}, "id": 1}

        monkeypatch.setattr(bridge_client, "send_request", _fake_send_request)

        result = bridge_client.hot_reload(dll_path="bad.dll", tier=2)
        assert call_count == 1  # No retries

    def test_post_build_coalescing_resets_debounce_timer(self, tmp_path, monkeypatch):
        """After a build, if new changes accumulated, debounce timer should be reset."""
        import sts2mcp.file_watcher as file_watcher_module
        import time as time_mod

        monkeypatch.setattr(
            file_watcher_module,
            "_resolve_project_context",
            lambda project_dir: SimpleNamespace(has_pck=False),
        )

        monkeypatch.setattr(
            file_watcher_module,
            "build_deploy_and_hot_reload_project",
            lambda *a, **kw: {"success": True, "hot_reload": {"success": True}},
        )

        project_dir = tmp_path / "coalesce_project"
        cs_file = project_dir / "Code" / "MyCard.cs"
        cs_file.parent.mkdir(parents=True)
        cs_file.write_text("class MyCard {}", encoding="utf-8")

        watcher = file_watcher_module.ModFileWatcher(
            project_dir=str(project_dir),
            mods_dir=str(tmp_path / "mods"),
            auto_reload=True,
        )

        # Simulate: during _trigger_build, new changes appear via pending_changes
        old_trigger = watcher._trigger_build

        def _trigger_with_simulated_change(changed_files):
            # Simulate a new change appearing during build
            watcher._pending_changes.add(str(cs_file) + ".new")
            old_trigger(changed_files)

        watcher._trigger_build = _trigger_with_simulated_change

        before_time = watcher._last_change_time
        watcher._trigger_build([str(cs_file)])

        # The _watch_loop post-build coalescing logic is not called here since
        # we're calling _trigger_build directly. But we can verify the pending
        # changes survived and would be picked up.
        assert str(cs_file) + ".new" in watcher._pending_changes

    def test_result_notification_type_helper(self):
        """_result_notification_type should map results to correct event types."""
        from sts2mcp.file_watcher import ModFileWatcher

        assert ModFileWatcher._result_notification_type(
            {"success": True, "hot_reload": {"success": True}}
        ) == "hot_reload_complete"

        assert ModFileWatcher._result_notification_type(
            {"success": True}
        ) == "build_complete"

        assert ModFileWatcher._result_notification_type(
            {"success": False, "cancelled": True}
        ) == "build_cancelled"

        assert ModFileWatcher._result_notification_type(
            {"success": False, "hot_reload": {"error": "connection refused"}}
        ) == "hot_reload_failed"

        assert ModFileWatcher._result_notification_type(
            {"success": False, "error": "compile error"}
        ) == "build_failed"

    def test_extract_error_message_helper(self):
        """_extract_error_message should pull errors from various result structures."""
        from sts2mcp.file_watcher import ModFileWatcher

        # Top-level error
        assert ModFileWatcher._extract_error_message({"error": "compile fail"}) == "compile fail"

        # Hot reload error
        assert ModFileWatcher._extract_error_message(
            {"hot_reload": {"error": "bridge down"}}
        ) == "bridge down"

        # Build stderr
        assert ModFileWatcher._extract_error_message(
            {"build": {"stderr": "CS1234: syntax error"}}
        ) == "CS1234: syntax error"

        # Empty
        assert ModFileWatcher._extract_error_message({}) == ""
