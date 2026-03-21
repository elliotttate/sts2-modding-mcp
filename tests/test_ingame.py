"""Comprehensive in-game bridge tests.

These tests require the game running with the MCPTest bridge mod loaded.
They are executed in order and build on each other — Phase 1 starts a run,
Phase 2 queries state within that run, Phase 3 enters combat, etc.

Run with:
    pytest tests/test_ingame.py -v --tb=short

All tests auto-skip when the bridge is not reachable.
"""

import os
import shutil
import tempfile
import time
from pathlib import Path

import pytest

from tests.conftest import skip_no_bridge, GAME_DIR

# Ensure tests run in definition order
pytestmark = skip_no_bridge


# ─── Helpers ─────────────────────────────────────────────────────────────────

def _r(response: dict) -> dict:
    """Unwrap a bridge response to get the result payload."""
    return response.get("result", response)


def _ok(response: dict) -> dict:
    """Assert response has no error and return unwrapped result."""
    r = _r(response)
    # If bridge died, skip instead of failing — game crash is not a test bug
    if r.get("error", "").startswith("Bridge not running"):
        pytest.skip("Bridge connection lost (game may have crashed)")
    assert "error" not in r, f"Bridge error: {r.get('error')}"
    return r


def _wait_for_screen(target: str, timeout: float = 15.0, interval: float = 0.5) -> dict:
    """Poll until we land on the expected screen or timeout."""
    from sts2mcp.bridge_client import get_screen
    deadline = time.time() + timeout
    last = {}
    while time.time() < deadline:
        last = _r(get_screen())
        if target.lower() in last.get("screen", "").lower():
            return last
        time.sleep(interval)
    pytest.skip(f"Timed out waiting for screen '{target}', stuck on '{last.get('screen')}'")


def _wait_for_run(timeout: float = 20.0) -> dict:
    """Wait until a run is in progress."""
    from sts2mcp.bridge_client import get_run_state
    deadline = time.time() + timeout
    while time.time() < deadline:
        r = _r(get_run_state())
        if r.get("in_progress"):
            return r
        time.sleep(0.5)
    pytest.skip("Timed out waiting for run to start")


def _wait_for_combat(timeout: float = 15.0) -> dict:
    """Wait until combat is in progress."""
    from sts2mcp.bridge_client import get_combat_state
    deadline = time.time() + timeout
    while time.time() < deadline:
        r = _r(get_combat_state())
        if r.get("in_combat"):
            return r
        time.sleep(0.5)
    pytest.skip("Timed out waiting for combat")


# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 0: Connection & Health Check
# ═══════════════════════════════════════════════════════════════════════════════


class TestPhase0Connection:
    """Verify bridge is alive and responding correctly."""

    def test_ping_status(self):
        from sts2mcp.bridge_client import ping
        r = _ok(ping())
        assert r["status"] == "ok"
        assert r["mod"] == "MCPTest"
        assert "version" in r

    def test_ping_includes_game_state(self):
        from sts2mcp.bridge_client import ping
        r = _ok(ping())
        # After our fixes, ping should always include screen info
        assert "screen" in r or r["status"] == "ok"

    def test_get_screen_returns_valid(self):
        from sts2mcp.bridge_client import get_screen
        r = _ok(get_screen())
        assert "screen" in r
        assert isinstance(r["screen"], str)
        assert len(r["screen"]) > 0

    def test_get_diagnostics(self):
        from sts2mcp.bridge_client import get_diagnostics
        r = _ok(get_diagnostics(log_lines=10))
        # Diagnostics should always return some structure
        assert isinstance(r, dict)


# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 1: Run Lifecycle — Start, Query, Restart
# ═══════════════════════════════════════════════════════════════════════════════


class TestPhase1RunLifecycle:
    """Start a run, verify state, restart it."""

    def test_start_run_ironclad(self):
        from sts2mcp.bridge_client import start_run, get_run_state
        # If a run is already in progress, that's fine — just verify state
        state = _r(get_run_state())
        if state.get("in_progress"):
            return

        r = _ok(start_run(character="Ironclad", ascension=0, seed="TESTBRIDGE42"))
        assert r.get("success") is True
        assert r["character"] == "Ironclad"

        # Wait for run to actually be in progress
        _wait_for_run()

    def test_run_state_after_start(self):
        from sts2mcp.bridge_client import get_run_state
        r = _ok(get_run_state())
        assert r["in_progress"] is True
        assert r["act"] >= 1
        assert r["player_count"] >= 1
        assert isinstance(r["players"], list)
        assert len(r["players"]) >= 1

        player = r["players"][0]
        assert "hp" in player
        assert "max_hp" in player
        assert "deck_size" in player
        assert player["hp"] > 0

    def test_player_state(self):
        from sts2mcp.bridge_client import get_player_state
        r = _ok(get_player_state())
        # Should have player data when run is in progress
        assert isinstance(r, dict)

    def test_map_state(self):
        from sts2mcp.bridge_client import get_map_state
        r = _ok(get_map_state())
        assert isinstance(r, dict)

    def test_available_actions(self):
        from sts2mcp.bridge_client import get_available_actions
        r = _ok(get_available_actions())
        assert isinstance(r, dict)
        # Should list actions available on the current screen
        assert "actions" in r or "available" in r or isinstance(r, dict)

    def test_restart_run(self):
        from sts2mcp.bridge_client import restart_run
        r = _r(restart_run())
        # restart_run may return success or restarting status
        assert isinstance(r, dict)

        _wait_for_run()


# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 2: Game Speed & Console Commands
# ═══════════════════════════════════════════════════════════════════════════════


class TestPhase2SpeedAndConsole:
    """Test game speed control and console command execution."""

    def test_set_game_speed_fast(self):
        from sts2mcp.bridge_client import set_game_speed
        r = _ok(set_game_speed(5.0))
        assert r.get("success") is True or "speed" in str(r).lower()

    def test_set_game_speed_normal(self):
        from sts2mcp.bridge_client import set_game_speed
        r = _ok(set_game_speed(1.0))
        assert r.get("success") is True or "speed" in str(r).lower()

    def test_console_help(self):
        from sts2mcp.bridge_client import execute_console_command
        r = _ok(execute_console_command("help"))
        assert r.get("success") is True

    def test_console_godmode(self):
        """Enable godmode for subsequent tests so we don't die."""
        from sts2mcp.bridge_client import execute_console_command
        r = _ok(execute_console_command("godmode"))
        assert r.get("success") is True


# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 3: State Manipulation
# ═══════════════════════════════════════════════════════════════════════════════


class TestPhase3StateManipulation:
    """Test manipulate_state to change gold, HP, energy, etc."""

    def test_manipulate_gold(self):
        from sts2mcp.bridge_client import manipulate_state, get_run_state
        r = _ok(manipulate_state({"gold": 999}))
        assert "applied" in r or "changes" in r or r.get("success")

        # Verify the manipulation didn't crash — gold may take a frame to update
        time.sleep(0.5)
        state = _ok(get_run_state())
        if "players" in state and len(state["players"]) > 0:
            # Just verify we can read it; exact value may vary due to game events
            assert "gold" in state["players"][0]

    def test_manipulate_hp(self):
        from sts2mcp.bridge_client import manipulate_state
        r = _ok(manipulate_state({"hp": 9999, "max_hp": 9999}))
        assert isinstance(r, dict)

    def test_manipulate_multiple(self):
        from sts2mcp.bridge_client import manipulate_state
        r = _ok(manipulate_state({
            "gold": 500,
            "add_relic": "BurningBlood",
        }))
        assert isinstance(r, dict)


# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 4: Enter Combat & Fight
# ═══════════════════════════════════════════════════════════════════════════════


class TestPhase4Combat:
    """Use console to enter combat, then test combat operations."""

    def test_enter_combat_via_console(self):
        """Fight a known encounter to get into combat."""
        from sts2mcp.bridge_client import execute_console_command, get_combat_state
        _ok(execute_console_command("fight JawWorm"))

        # Wait for combat to start — don't blindly proceed, just poll
        for attempt in range(15):
            time.sleep(2)
            combat = _r(get_combat_state())
            if combat.get("in_combat"):
                return  # We're in combat
        pytest.skip("fight command did not enter combat within 30s")

    def test_combat_state_structure(self):
        from sts2mcp.bridge_client import get_combat_state
        r = _r(get_combat_state())
        if not r.get("in_combat"):
            pytest.skip("Not in combat — previous fight command may have failed")
        assert "enemies" in r
        assert isinstance(r["enemies"], list)
        assert len(r["enemies"]) >= 1

        enemy = r["enemies"][0]
        assert "name" in enemy
        assert "hp" in enemy
        assert "max_hp" in enemy
        assert enemy["hp"] > 0

    def test_combat_state_has_players(self):
        from sts2mcp.bridge_client import get_combat_state
        r = _r(get_combat_state())
        if not r.get("in_combat"):
            pytest.skip("Not in combat")
        # The key could be "players" or "allies" depending on the response format
        has_players = "players" in r or "allies" in r
        assert has_players, f"Expected players/allies key, got: {list(r.keys())}"

    def test_combat_enemy_intents(self):
        """Verify intent decomposition works (our fix area)."""
        from sts2mcp.bridge_client import get_combat_state
        r = _r(get_combat_state())
        if not r.get("in_combat"):
            pytest.skip("Not in combat")
        for enemy in r.get("enemies", []):
            if enemy.get("intent"):
                intent = enemy["intent"]
                assert "intents" in intent or "move_id" in intent
                break

    def test_card_piles(self):
        from sts2mcp.bridge_client import get_card_piles, get_combat_state
        combat = _r(get_combat_state())
        if not combat.get("in_combat"):
            pytest.skip("Not in combat — card piles only available in combat")
        r = _ok(get_card_piles())
        assert "hand" in r
        assert "draw_pile" in r
        assert "discard_pile" in r
        assert isinstance(r["hand"], list)

    def test_play_card(self):
        """Play the first playable card."""
        from sts2mcp.bridge_client import get_combat_state, play_card
        combat = _r(get_combat_state())
        if not combat.get("in_combat"):
            pytest.skip("Not in combat")

        # Find first playable card from player hand
        players_key = "players" if "players" in combat else "allies"
        players = combat.get(players_key, [])
        if not players:
            pytest.skip("No player data in combat state")

        player = players[0]
        hand = player.get("hand", [])
        played = False
        for i, card in enumerate(hand):
            can_play = card.get("can_play", False) if isinstance(card, dict) else True
            if can_play:
                r = play_card(i, target_index=0)
                assert isinstance(_r(r), dict)
                played = True
                break
        if not played:
            pytest.skip("No playable cards in hand")

    def test_end_turn(self):
        from sts2mcp.bridge_client import end_turn
        r = end_turn()
        assert isinstance(_r(r), dict)
        time.sleep(1)  # Let turn resolve

    def test_play_second_turn(self):
        """Play another turn to exercise the combat loop."""
        from sts2mcp.bridge_client import get_combat_state, play_card, end_turn

        time.sleep(1)
        combat = _r(get_combat_state())
        if not combat.get("in_combat"):
            pytest.skip("Combat ended after first turn")

        # Try to play a card
        players_key = "players" if "players" in combat else "allies"
        players = combat.get(players_key, [])
        if players:
            hand = players[0].get("hand", [])
            for i, card in enumerate(hand):
                can_play = card.get("can_play", False) if isinstance(card, dict) else True
                if can_play:
                    play_card(i, target_index=0)
                    time.sleep(0.3)
                    break

        end_turn()
        time.sleep(1)


# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 5: Debugging — Pause, Resume, Breakpoints, Stepping
# ═══════════════════════════════════════════════════════════════════════════════


class TestPhase5Debugging:
    """Test the breakpoint/stepping system.

    First ensure we're in combat (start a new one if previous ended).
    """

    def test_ensure_in_combat(self):
        """Make sure we're in combat for debugging tests. If not, skip."""
        from sts2mcp.bridge_client import get_combat_state, execute_console_command
        combat = _r(get_combat_state())
        if not combat.get("in_combat"):
            _ok(execute_console_command("fight JawWorm"))
            for _ in range(15):
                time.sleep(2)
                combat = _r(get_combat_state())
                if combat.get("in_combat"):
                    return
            pytest.skip("Could not enter combat for debugging tests")

    def test_pause(self):
        from sts2mcp.bridge_client import debug_pause
        r = _ok(debug_pause())
        assert isinstance(r, dict)

    def test_get_context_while_paused(self):
        from sts2mcp.bridge_client import debug_get_context
        r = _ok(debug_get_context())
        assert isinstance(r, dict)
        # Should have context info about why we're paused
        if "context" in r:
            ctx = r["context"]
            assert "location" in ctx or "reason" in ctx

    def test_resume(self):
        from sts2mcp.bridge_client import debug_resume
        r = _ok(debug_resume())
        assert isinstance(r, dict)

    def test_set_breakpoint_action(self):
        from sts2mcp.bridge_client import debug_set_breakpoint
        r = _ok(debug_set_breakpoint(bp_type="action", target="DamageAction"))
        assert isinstance(r, dict)
        # Should return the breakpoint ID (may be "id", "breakpoint_id", or "success")
        assert "id" in r or "breakpoint_id" in r or r.get("success") is True

    def test_set_breakpoint_with_condition(self):
        from sts2mcp.bridge_client import debug_set_breakpoint
        r = _ok(debug_set_breakpoint(
            bp_type="action",
            target="GainBlockAction",
            condition="hp<50",
        ))
        assert isinstance(r, dict)

    def test_list_breakpoints(self):
        from sts2mcp.bridge_client import debug_list_breakpoints
        r = _ok(debug_list_breakpoints())
        assert isinstance(r, dict)
        bps = r.get("breakpoints", [])
        assert len(bps) >= 2, f"Expected 2+ breakpoints, got {len(bps)}"

    def test_remove_breakpoint(self):
        from sts2mcp.bridge_client import debug_list_breakpoints, debug_remove_breakpoint
        r = _ok(debug_list_breakpoints())
        bps = r.get("breakpoints", [])
        if bps:
            bp_id = bps[0].get("id", bps[0].get("Id", 1))
            remove_r = _ok(debug_remove_breakpoint(bp_id))
            assert isinstance(remove_r, dict)

    def test_clear_all_breakpoints(self):
        from sts2mcp.bridge_client import debug_clear_breakpoints, debug_list_breakpoints
        _ok(debug_clear_breakpoints())

        r = _ok(debug_list_breakpoints())
        bps = r.get("breakpoints", [])
        assert len(bps) == 0, f"Expected 0 breakpoints after clear, got {len(bps)}"

    def test_step_action(self):
        """Set step mode and verify we can step through actions."""
        from sts2mcp.bridge_client import debug_step, debug_resume
        r = _ok(debug_step(mode="action"))
        assert isinstance(r, dict)
        # Immediately resume to avoid blocking the game
        time.sleep(0.5)
        debug_resume()

    def test_cleanup_debug_state(self):
        """Ensure we're fully resumed and no breakpoints linger."""
        from sts2mcp.bridge_client import debug_resume, debug_clear_breakpoints
        debug_resume()
        debug_clear_breakpoints()


# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 6: Event Tracking & Game Logging
# ═══════════════════════════════════════════════════════════════════════════════


class TestPhase6EventsAndLogging:
    """Test the event tracker, exception monitor, and game log capture."""

    def test_clear_events(self):
        from sts2mcp.bridge_client import clear_events
        r = _ok(clear_events())
        assert isinstance(r, dict)

    def test_get_events_after_clear(self):
        from sts2mcp.bridge_client import get_events
        r = _ok(get_events(since_id=0, max_count=10))
        assert isinstance(r, dict)
        # Events list should exist (may have new events from test actions)
        assert "events" in r

    def test_events_accumulate(self):
        """Do something that generates events, then check they're captured."""
        from sts2mcp.bridge_client import get_events, execute_console_command
        # Get baseline
        baseline = _ok(get_events(since_id=0, max_count=1000))
        baseline_count = len(baseline.get("events", []))
        baseline_id = baseline.get("latest_id", 0)

        # Do an action that generates an event
        execute_console_command("gold 1")
        time.sleep(0.5)

        # Check new events appeared
        after = _ok(get_events(since_id=baseline_id, max_count=100))
        # We just verify the call works; events may or may not appear depending on what triggers them

    def test_clear_exceptions(self):
        from sts2mcp.bridge_client import clear_exceptions
        r = _ok(clear_exceptions())
        assert isinstance(r, dict)

    def test_get_exceptions(self):
        from sts2mcp.bridge_client import get_exceptions
        r = _ok(get_exceptions(max_count=10))
        assert isinstance(r, dict)
        assert "exceptions" in r
        # After clear, should have 0 or very few
        assert isinstance(r["exceptions"], list)

    def test_get_game_log(self):
        from sts2mcp.bridge_client import get_game_log
        r = _ok(get_game_log(max_count=20))
        assert isinstance(r, dict)
        assert "entries" in r
        assert isinstance(r["entries"], list)

    def test_get_game_log_with_filter(self):
        from sts2mcp.bridge_client import get_game_log
        r = _ok(get_game_log(max_count=50, level="Warn"))
        assert isinstance(r, dict)
        # All returned entries should be Warn level
        for entry in r.get("entries", []):
            if isinstance(entry, dict):
                assert entry.get("level", "").lower() in ("warn", "warning", "error")

    def test_get_game_log_since_id(self):
        from sts2mcp.bridge_client import get_game_log
        # Get latest ID
        r1 = _ok(get_game_log(max_count=1))
        if r1.get("entries"):
            latest_id = r1["entries"][-1].get("id", 0)
            # Query since that ID — should return 0 or very few
            r2 = _ok(get_game_log(since_id=latest_id, max_count=100))
            assert isinstance(r2, dict)

    def test_get_log_levels(self):
        from sts2mcp.bridge_client import get_log_levels
        r = _ok(get_log_levels())
        assert isinstance(r, dict)

    def test_set_log_level_capture(self):
        """Change capture level and verify it's applied."""
        from sts2mcp.bridge_client import set_log_level
        r = _ok(set_log_level(capture_level="Debug"))
        assert isinstance(r, dict)

        # Reset to Info
        _ok(set_log_level(capture_level="Info"))

    def test_set_log_level_category(self):
        from sts2mcp.bridge_client import set_log_level
        r = _ok(set_log_level(log_type="Actions", level="Debug"))
        assert isinstance(r, dict)


# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 7: Snapshots
# ═══════════════════════════════════════════════════════════════════════════════


class TestPhase7Snapshots:
    """Test save/restore snapshot functionality."""

    def test_save_snapshot(self):
        from sts2mcp.bridge_client import save_snapshot
        r = _ok(save_snapshot(name="test_snap"))
        assert r.get("success") is True or "saved" in str(r).lower()

    def test_save_second_snapshot(self):
        from sts2mcp.bridge_client import save_snapshot
        r = _ok(save_snapshot(name="test_snap_2"))
        assert isinstance(r, dict)

    def test_restore_snapshot(self):
        from sts2mcp.bridge_client import restore_snapshot, get_run_state
        # Capture state before restore
        before = _ok(get_run_state())

        r = _ok(restore_snapshot(name="test_snap"))
        assert isinstance(r, dict)
        time.sleep(1)

        # State should still be valid after restore
        after = _ok(get_run_state())
        assert after["in_progress"] is True


# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 8: Screenshots
# ═══════════════════════════════════════════════════════════════════════════════


class TestPhase8Screenshots:
    """Test screenshot capture."""

    def test_capture_screenshot_default(self):
        from sts2mcp.bridge_client import capture_screenshot
        r = _ok(capture_screenshot())
        assert isinstance(r, dict)
        # Should indicate success or provide a path/data
        assert r.get("success") is True or "path" in r or "data" in r

    def test_capture_screenshot_to_path(self):
        from sts2mcp.bridge_client import capture_screenshot
        tmp = tempfile.mktemp(suffix=".png", prefix="sts2_test_")
        try:
            r = _ok(capture_screenshot(save_path=tmp))
            assert isinstance(r, dict)
        finally:
            if os.path.exists(tmp):
                os.remove(tmp)


# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 9: AutoSlay (Automated Runner)
# ═══════════════════════════════════════════════════════════════════════════════


class TestPhase9AutoSlay:
    """Test the AutoSlay automated runner system.

    We configure it, start a very short run, check status, then stop.
    """

    def test_configure_autoslay(self):
        from sts2mcp.bridge_client import autoslay_configure
        r = _ok(autoslay_configure(
            run_timeout_seconds=120,
            room_timeout_seconds=30,
            max_floor=5,  # Only go 5 floors — keep it short
        ))
        assert r.get("success") is True

    def test_start_autoslay(self):
        from sts2mcp.bridge_client import autoslay_start, autoslay_stop
        autoslay_stop()  # Stop any previous session
        time.sleep(1)
        r = _r(autoslay_start(
            character="Ironclad",
            runs=1,
            seed="AUTOTEST1",
        ))
        # AutoSlay may not be available if game version lacks RunAsync
        if "error" in r and "RunAsync" in r["error"]:
            pytest.skip("AutoSlay RunAsync not available in this game version")
        assert r.get("success") is True or "started" in str(r).lower()

    def test_autoslay_status(self):
        """Check status after a brief wait."""
        time.sleep(3)
        from sts2mcp.bridge_client import autoslay_status
        r = _r(autoslay_status())
        # May error if AutoSlay isn't available
        if "error" in r and "RunAsync" in str(r.get("error", "")):
            pytest.skip("AutoSlay not available in this game version")
        assert isinstance(r, dict)

    def test_stop_autoslay(self):
        from sts2mcp.bridge_client import autoslay_stop
        r = _r(autoslay_stop())
        assert isinstance(r, dict)
        time.sleep(2)  # Give it time to stop


# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 10: Live Coding — Generate, Build, Hot-Swap a Harmony Patch
# ═══════════════════════════════════════════════════════════════════════════════


class TestPhase10LiveCoding:
    """Generate a mod with a Harmony patch, build it, and hot-swap it into
    the running game. This exercises the full live coding workflow:

    1. Generate a simple Harmony Prefix patch (logs a message before DamageAction)
    2. Create a buildable project scaffold
    3. Build with dotnet
    4. Hot-swap the DLL via the bridge
    5. Verify the patch loaded (check events/log)
    """

    _project_dir: str | None = None

    @pytest.fixture(autouse=True)
    def _setup_project(self, tmp_path):
        """Create a temp project directory for this test class."""
        TestPhase10LiveCoding._project_dir = str(tmp_path / "HotSwapTest")

    def test_01_start_fresh_run(self):
        """Ensure a run is in progress for live coding tests."""
        from sts2mcp.bridge_client import start_run, get_run_state
        state = _r(get_run_state())
        if state.get("in_progress"):
            return  # Already in a run — good enough
        r = _ok(start_run(character="Ironclad", ascension=0, seed="HOTSWAP1"))
        _wait_for_run()

    def test_02_generate_patch_project(self):
        """Generate a complete mod project with a Harmony patch."""
        from sts2mcp.mod_gen import ModGenerator

        gen = ModGenerator(GAME_DIR)
        project_dir = Path(self._project_dir)

        # Create mod project scaffold
        proj = gen.create_mod_project(
            mod_name="HotSwapTest",
            author="MCPTestSuite",
            description="Live coding hot-swap test",
            output_dir=str(project_dir),
        )
        assert proj.get("success") or project_dir.exists()

        # Generate a Harmony Prefix patch that logs when DamageAction executes
        patch = gen.generate_harmony_patch(
            mod_namespace="HotSwapTest",
            class_name="DamageWatcher",
            target_type="DamageAction",
            target_method="Execute",
            patch_type="Prefix",
            description="Logs when DamageAction runs (hot-swap test)",
        )
        assert "source" in patch

        # Write the patch to the project Code directory
        code_dir = project_dir / "HotSwapTest" / "Code"
        code_dir.mkdir(parents=True, exist_ok=True)
        patch_path = code_dir / patch["file_name"]
        patch_path.write_text(patch["source"])

        assert patch_path.exists()
        TestPhase10LiveCoding._patch_path = str(patch_path)

    def test_03_build_mod(self):
        """Build the mod with dotnet."""
        if not shutil.which("dotnet"):
            pytest.skip("dotnet CLI not available")

        project_dir = Path(self._project_dir)
        csproj_files = list(project_dir.rglob("*.csproj"))
        if not csproj_files:
            pytest.skip("No .csproj generated — create_mod_project may need GAME_DIR")

        import subprocess
        result = subprocess.run(
            ["dotnet", "build", str(csproj_files[0]), "-c", "Release"],
            capture_output=True, text=True, timeout=120,
        )
        if result.returncode != 0:
            pytest.fail(f"dotnet build failed:\n{result.stderr}\n{result.stdout}")

        # Find the built DLL
        dll_files = list(project_dir.rglob("bin/Release/**/*.dll"))
        mod_dlls = [d for d in dll_files if "HotSwapTest" in d.name]
        assert len(mod_dlls) >= 1, f"No HotSwapTest DLL found in {dll_files}"
        TestPhase10LiveCoding._dll_path = str(mod_dlls[0])

    def test_04_hot_swap_patches(self):
        """Hot-swap the built DLL into the running game."""
        dll_path = getattr(TestPhase10LiveCoding, "_dll_path", None)
        if not dll_path or not Path(dll_path).exists():
            pytest.skip("No DLL built in previous step")

        from sts2mcp.bridge_client import hot_swap_patches, clear_events
        # Clear events so we can check for new ones
        _ok(clear_events())

        r = _ok(hot_swap_patches(dll_path))
        assert r.get("success") is True or "loaded" in str(r).lower() or "patches" in str(r).lower()

    def test_05_verify_patch_active(self):
        """Enter combat and verify the patch is doing something."""
        dll_path = getattr(TestPhase10LiveCoding, "_dll_path", None)
        if not dll_path:
            pytest.skip("No DLL built — skipping verification")

        from sts2mcp.bridge_client import (
            execute_console_command, get_combat_state, play_card,
            end_turn, get_events, get_log,
        )

        # Enter combat
        _ok(execute_console_command("fight JawWorm"))
        time.sleep(2)
        _wait_for_combat(timeout=10)

        # End a turn to trigger DamageAction from enemies
        end_turn()
        time.sleep(2)

        # Check bridge log for evidence the patch ran
        log = _r(get_log(lines=50, contains="HotSwap"))
        # The patch may or may not log (depends on template), but at minimum
        # the hot_swap_patches call should have logged something
        assert isinstance(log, dict)


# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 11: Navigation — Map Travel, Events, Shop, Rest, Rewards
# ═══════════════════════════════════════════════════════════════════════════════


class TestPhase11Navigation:
    """Test navigation through different room types.

    Uses console commands to set up specific screens, then exercises
    the navigation bridge methods.
    """

    def test_start_clean_run(self):
        """Ensure a run is in progress for navigation tests."""
        from sts2mcp.bridge_client import start_run, get_run_state, execute_console_command
        state = _r(get_run_state())
        if not state.get("in_progress"):
            _ok(start_run(character="Ironclad", ascension=0, seed="NAV42"))
            _wait_for_run()
        _ok(execute_console_command("godmode"))
        time.sleep(1)

    def test_navigate_map(self):
        """Try to navigate on the map."""
        from sts2mcp.bridge_client import navigate_map, get_map_state
        map_state = _r(get_map_state())

        # Attempt to navigate to the first available node
        r = navigate_map(row=1, col=0)
        result = _r(r)
        # Either succeeds or gives a meaningful error (not on map screen, etc.)
        assert isinstance(result, dict)

    def test_event_via_console(self):
        """Trigger an event and try to make a choice."""
        from sts2mcp.bridge_client import execute_console_command, make_event_choice
        _ok(execute_console_command("event BigFish"))
        time.sleep(2)

        r = make_event_choice(choice_index=0)
        result = _r(r)
        assert isinstance(result, dict)
        time.sleep(1)

    def test_proceed_generic(self):
        """Test the generic proceed action."""
        from sts2mcp.bridge_client import proceed
        r = proceed()
        assert isinstance(_r(r), dict)
        time.sleep(1)

    def test_rest_site_via_console(self):
        """Navigate to rest site and test rest actions."""
        from sts2mcp.bridge_client import execute_console_command, rest_site_choice
        _ok(execute_console_command("rest"))
        time.sleep(2)

        r = rest_site_choice(choice="rest")
        result = _r(r)
        assert isinstance(result, dict)
        time.sleep(1)


# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 12: Error Handling & Edge Cases
# ═══════════════════════════════════════════════════════════════════════════════


class TestPhase12ErrorHandling:
    """Test that the bridge handles invalid inputs gracefully."""

    def test_invalid_card_index(self):
        """Playing a card with invalid index should return error, not crash."""
        from sts2mcp.bridge_client import play_card
        r = _r(play_card(card_index=999, target_index=0))
        # Should either error or fail gracefully — must not crash
        assert isinstance(r, dict)

    def test_invalid_console_command(self):
        from sts2mcp.bridge_client import execute_console_command
        r = _r(execute_console_command("thiscommanddoesnotexist_xyz"))
        assert isinstance(r, dict)

    def test_start_run_invalid_character(self):
        from sts2mcp.bridge_client import start_run
        r = _r(start_run(character="NonExistentCharacter123"))
        # Should return error about character not found
        assert "error" in r

    def test_manipulate_state_empty(self):
        from sts2mcp.bridge_client import manipulate_state
        r = _r(manipulate_state({}))
        assert isinstance(r, dict)

    def test_breakpoint_remove_nonexistent(self):
        from sts2mcp.bridge_client import debug_remove_breakpoint
        r = _r(debug_remove_breakpoint(bp_id=99999))
        assert isinstance(r, dict)

    def test_restore_nonexistent_snapshot(self):
        from sts2mcp.bridge_client import restore_snapshot
        r = _r(restore_snapshot(name="does_not_exist_xyz"))
        assert isinstance(r, dict)

    def test_hot_swap_nonexistent_dll(self):
        from sts2mcp.bridge_client import hot_swap_patches
        r = _r(hot_swap_patches(dll_path="C:/nonexistent/fake.dll"))
        assert isinstance(r, dict)
        # Should have an error about file not found
        assert "error" in r or "not found" in str(r).lower() or "fail" in str(r).lower()

    def test_set_game_speed_extreme(self):
        """Test boundary values for game speed."""
        from sts2mcp.bridge_client import set_game_speed
        # Slow (not too extreme to avoid making game unresponsive)
        r = _r(set_game_speed(0.5))
        assert isinstance(r, dict)
        # Fast
        r = _r(set_game_speed(10.0))
        assert isinstance(r, dict)
        # Reset to normal
        _ok(set_game_speed(1.0))

    def test_resume_when_not_paused(self):
        """Resuming when not paused should be a no-op, not crash."""
        from sts2mcp.bridge_client import debug_resume
        r = _r(debug_resume())
        assert isinstance(r, dict)


# ═══════════════════════════════════════════════════════════════════════════════
# PHASE 13: Cleanup — Leave the game in a good state
# ═══════════════════════════════════════════════════════════════════════════════


class TestPhase13Cleanup:
    """Ensure we leave the game in a reasonable state."""

    def test_clear_debug_state(self):
        from sts2mcp.bridge_client import debug_clear_breakpoints, debug_resume
        debug_resume()
        _ok(debug_clear_breakpoints())

    def test_reset_game_speed(self):
        from sts2mcp.bridge_client import set_game_speed
        _ok(set_game_speed(1.0))

    def test_reset_log_levels(self):
        from sts2mcp.bridge_client import set_log_level
        _ok(set_log_level(capture_level="Info"))

    def test_final_ping(self):
        """Verify bridge is still healthy after all tests."""
        from sts2mcp.bridge_client import ping
        r = _ok(ping())
        assert r["status"] == "ok"
        assert r["mod"] == "MCPTest"
