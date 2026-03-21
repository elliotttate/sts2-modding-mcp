"""TCP client for communicating with the MCPTest bridge mod running inside STS2."""

import json
import socket
from typing import Any, Optional, Sequence

BRIDGE_HOST = "127.0.0.1"
BRIDGE_PORT = 21337
TIMEOUT = 12.0  # Must exceed MainThreadDispatcher.Invoke's 10s timeout
_ACTION_REQUIREMENTS = {
    "event_option": ("choice_index",),
    "map_travel": ("row", "col"),
    "rest_option": ("choice",),
    "reward_select": ("reward_index",),
    "shop_buy": ("item_type", "index"),
    "treasure_pick": ("treasure_index",),
}


def _payload(response: dict) -> dict:
    if isinstance(response, dict) and isinstance(response.get("result"), dict):
        return response["result"]
    return response


def send_request(method: str, params: dict | None = None, request_id: int = 1) -> dict:
    """Send a JSON-RPC request to the bridge mod and return the parsed response."""
    request = {"method": method, "id": request_id}
    if params:
        request["params"] = params

    try:
        with socket.create_connection((BRIDGE_HOST, BRIDGE_PORT), timeout=TIMEOUT) as sock:
            sock.settimeout(TIMEOUT)
            payload = json.dumps(request) + "\n"
            sock.sendall(payload.encode("utf-8"))

            # Read response (newline-delimited, max 10MB)
            data = b""
            max_size = 10 * 1024 * 1024
            while True:
                chunk = sock.recv(4096)
                if not chunk:
                    break
                data += chunk
                if len(data) > max_size:
                    return {"error": f"Bridge response exceeded {max_size} bytes"}
                if b"\n" in data or b"\r" in data:
                    break

        # Strip BOM and whitespace
        try:
            text = data.decode("utf-8-sig").strip()
        except UnicodeDecodeError as e:
            return {"error": f"Bridge sent non-UTF8 data: {e}"}
        if not text:
            return {"error": "Empty response from bridge"}

        try:
            return json.loads(text)
        except json.JSONDecodeError as e:
            return {"error": f"Invalid JSON from bridge: {e}"}

    except ConnectionRefusedError:
        return {"error": "Bridge not running. Is the game running with MCPTest mod loaded?"}
    except socket.timeout:
        return {"error": "Bridge timed out. Game may be loading or unresponsive."}
    except Exception as e:
        return {"error": f"Bridge communication failed: {type(e).__name__}: {e}"}


def ping() -> dict:
    return send_request("ping")


def get_run_state() -> dict:
    return send_request("get_run_state")


def get_combat_state() -> dict:
    return send_request("get_combat_state")


def get_player_state() -> dict:
    return send_request("get_player_state")


def get_screen() -> dict:
    return send_request("get_screen")


def get_map_state() -> dict:
    return send_request("get_map_state")


def get_available_actions() -> dict:
    return send_request("get_available_actions")


def execute_console_command(command: str) -> dict:
    return send_request("console", {"command": command})


def play_card(card_index: int, target_index: int = -1) -> dict:
    return send_request("play_card", {"card_index": card_index, "target_index": target_index})


def end_turn() -> dict:
    return send_request("end_turn")


def start_run(
    character: str = "Ironclad",
    ascension: int = 0,
    seed: str | int | None = None,
    fixture: dict[str, Any] | None = None,
    modifiers: Sequence[str] | None = None,
    acts: Sequence[str] | None = None,
    relics: Sequence[str] | None = None,
    cards: Sequence[str] | None = None,
    potions: Sequence[str] | None = None,
    powers: Sequence[dict[str, Any]] | None = None,
    gold: int | None = None,
    hp: int | None = None,
    energy: int | None = None,
    draw_cards: int | None = None,
    fight: str | None = None,
    event: str | None = None,
    godmode: bool = False,
    fixture_commands: Sequence[str] | None = None,
) -> dict:
    params: dict[str, Any] = {"character": character, "ascension": ascension}
    if seed is not None:
        params["seed"] = str(seed)
    if fixture:
        params["fixture"] = fixture
    if modifiers:
        params["modifiers"] = list(modifiers)
    if acts:
        params["acts"] = list(acts)
    if relics:
        params["relics"] = list(relics)
    if cards:
        params["cards"] = list(cards)
    if potions:
        params["potions"] = list(potions)
    if powers:
        params["powers"] = list(powers)
    if gold is not None:
        params["gold"] = gold
    if hp is not None:
        params["hp"] = hp
    if energy is not None:
        params["energy"] = energy
    if draw_cards is not None:
        params["draw_cards"] = draw_cards
    if fight:
        params["fight"] = fight
    if event:
        params["event"] = event
    if godmode:
        params["godmode"] = True
    if fixture_commands:
        params["fixture_commands"] = list(fixture_commands)
    return send_request("start_run", params)


def start_run_with_options(
    character: str = "Ironclad",
    ascension: int = 0,
    seed: str | int | None = None,
    fixture: dict[str, Any] | None = None,
) -> dict:
    return start_run(character=character, ascension=ascension, seed=seed, fixture=fixture)


def is_connected() -> bool:
    """Check if bridge is reachable."""
    try:
        result = ping()
        return "error" not in result and result.get("result", {}).get("status") == "ok"
    except Exception:
        return False


def use_potion(potion_index: int, target_index: int = -1) -> dict:
    return send_request("use_potion", {"potion_index": potion_index, "target_index": target_index})


def execute_action(action: str, **params: Any) -> dict:
    normalized = action.strip().lower()
    required = _ACTION_REQUIREMENTS.get(normalized, ())
    missing = [param for param in required if param not in params]
    if missing:
        return {
            "error": (
                f"Action '{normalized}' is missing required parameter(s): "
                + ", ".join(sorted(missing))
            )
        }

    payload = {"action": normalized}
    payload.update(params)
    return send_request("execute_action", payload)


def make_event_choice(choice_index: int) -> dict:
    return execute_action("event_option", choice_index=choice_index)


def navigate_map(row: int, col: int) -> dict:
    return execute_action("map_travel", row=row, col=col)


def rest_site_choice(choice: str) -> dict:
    """choice: 'rest', 'smith', or 'recall'"""
    return execute_action("rest_option", choice=choice)


def rest_site_proceed() -> dict:
    return execute_action("rest_proceed")


def shop_action(action: str, index: int = 0, item_type: Optional[str] = None) -> dict:
    """action: 'buy_card', 'buy_relic', 'buy_potion', 'remove_card', 'proceed'"""
    normalized = action.strip().lower()
    if normalized in {"proceed", "leave", "shop_proceed"}:
        return execute_action("shop_proceed")

    if item_type is None:
        item_type = {
            "buy_card": "card",
            "buy_relic": "relic",
            "buy_potion": "potion",
            "remove_card": "remove",
        }.get(normalized)

    if item_type is not None:
        return execute_action("shop_buy", index=index, item_type=item_type, shop_action=normalized)

    return send_request("shop_action", {"action": action, "index": index})


def reward_select(index: int) -> dict:
    return execute_action("reward_select", reward_index=index)


def reward_proceed() -> dict:
    return execute_action("reward_proceed")


def shop_buy(item_type: str, index: int = 0) -> dict:
    return execute_action("shop_buy", item_type=item_type, index=index)


def shop_proceed() -> dict:
    return execute_action("shop_proceed")


def treasure_pick(index: int = 0) -> dict:
    return execute_action("treasure_pick", treasure_index=index)


def treasure_proceed() -> dict:
    return execute_action("treasure_proceed")


def card_select(indices: int | Sequence[int], confirm: bool = False) -> dict:
    if isinstance(indices, int):
        return execute_action("card_select", card_index=indices, confirm=confirm)
    return execute_action("card_select", card_indices=list(indices), confirm=confirm)


def card_confirm() -> dict:
    return execute_action("card_confirm")


def card_skip() -> dict:
    return execute_action("card_skip")


def proceed() -> dict:
    return execute_action("proceed")


def get_log(lines: int = 200, contains: str | None = None) -> dict:
    params: dict[str, Any] = {"lines": lines}
    if contains:
        params["contains"] = contains
    return send_request("get_log", params)


def get_card_piles() -> dict:
    return send_request("get_card_piles")


def manipulate_state(changes: dict) -> dict:
    """Apply state changes for testing. changes can include: hp, max_hp, gold, energy, draw_cards, add_power, add_relic, add_card, etc."""
    return send_request("manipulate_state", changes)


# ─── Live Coding & Iteration ─────────────────────────────────────────────────


def hot_swap_patches(dll_path: str) -> dict:
    """Hot-swap Harmony patches from a new DLL without restarting the game."""
    return send_request("hot_swap_patches", {"dll_path": dll_path})


def get_exceptions(max_count: int = 20, since_id: int = 0) -> dict:
    """Get recent unhandled exceptions captured by the bridge."""
    return send_request("get_exceptions", {"max_count": max_count, "since_id": since_id})


def get_state_diff() -> dict:
    """Get changes since the last state query. First call captures baseline."""
    return send_request("get_state_diff")


def capture_screenshot(save_path: str = "") -> dict:
    """Capture a screenshot of the game window."""
    params: dict[str, Any] = {}
    if save_path:
        params["save_path"] = save_path
    return send_request("capture_screenshot", params if params else None)


def get_events(since_id: int = 0, max_count: int = 100) -> dict:
    """Get game events since a given event ID."""
    return send_request("get_events", {"since_id": since_id, "max_count": max_count})


def save_snapshot(name: str = "default") -> dict:
    """Save a state snapshot for later restoration."""
    return send_request("save_snapshot", {"name": name})


def restore_snapshot(name: str = "default") -> dict:
    """Restore a previously saved state snapshot."""
    return send_request("restore_snapshot", {"name": name})


def set_game_speed(speed: float = 1.0) -> dict:
    """Set the game speed multiplier (0.1 to 20.0). Use >1 for faster testing."""
    return send_request("set_game_speed", {"speed": speed})


def restart_run() -> dict:
    """Restart a run with the same parameters as the last start_run call."""
    return send_request("restart_run")


# ─── Breakpoints & Stepping ───────────────────────────────────────────────────


def debug_pause() -> dict:
    """Pause action processing. Game keeps rendering but no actions execute."""
    return send_request("debug_pause")


def debug_resume() -> dict:
    """Resume from a breakpoint or pause."""
    return send_request("debug_resume")


def debug_step(mode: str = "action") -> dict:
    """Resume and pause again at the next opportunity.

    Args:
        mode: "action" = pause after next action, "turn" = pause at next player turn start.
    """
    return send_request("debug_step", {"mode": mode})


def debug_set_breakpoint(
    bp_type: str = "action",
    target: str = "",
    condition: str | None = None,
) -> dict:
    """Set a breakpoint.

    Args:
        bp_type: "action" (break on action type) or "hook" (break on hook name).
        target: Action type name (e.g., "PlayCardAction", "DamageAction") or
                hook name (e.g., "BeforeCardPlayed", "BeforeDamageReceived").
        condition: Optional condition like "hp<10", "energy==0", "round>=3".
    """
    params: dict[str, Any] = {"type": bp_type, "target": target}
    if condition:
        params["condition"] = condition
    return send_request("debug_set_breakpoint", params)


def debug_remove_breakpoint(bp_id: int) -> dict:
    """Remove a breakpoint by ID."""
    return send_request("debug_remove_breakpoint", {"id": bp_id})


def debug_list_breakpoints() -> dict:
    """List all breakpoints and current step/pause state."""
    return send_request("debug_list_breakpoints")


def debug_clear_breakpoints() -> dict:
    """Remove all breakpoints and disable step mode."""
    return send_request("debug_clear_breakpoints")


def debug_get_context() -> dict:
    """Get the current breakpoint context (why we're paused, game state snapshot)."""
    return send_request("debug_get_context")


# ─── Game Log & Debugging ─────────────────────────────────────────────────────


def get_game_log(
    max_count: int = 100,
    since_id: int = 0,
    level: str | None = None,
    contains: str | None = None,
) -> dict:
    """Get captured game log messages (from the game's own Log system, not the bridge log).

    The game uses Log.LogCallback which we hook to capture messages. This covers
    all game subsystems: Actions, Network, GameSync, VisualSync, Generic.

    Args:
        max_count: Max entries to return (up to 500).
        since_id: Only return entries after this ID (for polling).
        level: Filter by level (VeryDebug, Load, Debug, Info, Warn, Error).
        contains: Filter by substring in message.
    """
    params: dict[str, Any] = {"max_count": max_count, "since_id": since_id}
    if level:
        params["level"] = level
    if contains:
        params["contains"] = contains
    return send_request("get_game_log", params)


def set_log_level(
    log_type: str | None = None,
    level: str | None = None,
    global_level: str | None = None,
    capture_level: str | None = None,
) -> dict:
    """Set game logging verbosity.

    The game has per-category log levels and a global fallback. You can also
    control how verbose the capture buffer is (what we store for get_game_log).

    Log levels (least to most verbose): Error, Warn, Info, Debug, Load, VeryDebug.
    Log types: Generic, Network, Actions, GameSync, VisualSync.

    Args:
        log_type: Category to set (e.g., "Actions"). Used with level.
        level: Level for the specified type (e.g., "Debug").
        global_level: Set the global fallback level for all types.
        capture_level: Set minimum level captured into the ring buffer.
    """
    params: dict[str, Any] = {}
    if log_type and level:
        params["type"] = log_type
        params["level"] = level
    if global_level:
        params["global_level"] = global_level
    if capture_level:
        params["capture_level"] = capture_level
    return send_request("set_log_level", params)


def get_log_levels() -> dict:
    """Get current log level settings for all categories and the global level."""
    return send_request("get_log_levels")


def get_diagnostics(log_lines: int = 40) -> dict:
    """Get comprehensive diagnostics: screen, run state, combat state, active screen shape, and recent log."""
    return send_request("get_diagnostics", {"log_lines": log_lines})


def clear_exceptions() -> dict:
    """Clear the exception ring buffer. Useful before a test to get a clean baseline."""
    return send_request("clear_exceptions")


def clear_events() -> dict:
    """Clear the event ring buffer. Useful before a test to get a clean baseline."""
    return send_request("clear_events")


# ─── AutoSlay (Built-in Automated Runner) ────────────────────────────────────


def autoslay_start(
    character: str = "Ironclad",
    seed: str | None = None,
    runs: int = 1,
    loop: bool = False,
) -> dict:
    """Start the game's built-in AutoSlay automated runner.

    AutoSlay plays through entire runs automatically — useful for smoke testing,
    crash detection, and regression testing across many seeds.

    Args:
        character: Character to play (Ironclad, Silent, Defect, etc.)
        seed: Specific seed to use (empty/None = random). In multi-run mode, suffixed with _N.
        runs: Number of runs to play (default 1).
        loop: If True, run indefinitely until stopped.
    """
    params: dict[str, Any] = {"character": character, "runs": runs, "loop": loop}
    if seed:
        params["seed"] = str(seed)
    return send_request("autoslay_start", params)


def autoslay_stop() -> dict:
    """Stop the currently running AutoSlay session."""
    return send_request("autoslay_stop")


def autoslay_status() -> dict:
    """Get the current AutoSlay status including run progress, game state, and recent log."""
    return send_request("autoslay_status")


def autoslay_configure(
    run_timeout_seconds: int | None = None,
    room_timeout_seconds: int | None = None,
    screen_timeout_seconds: int | None = None,
    polling_interval_ms: int | None = None,
    watchdog_timeout_seconds: int | None = None,
    max_floor: int | None = None,
) -> dict:
    """Configure AutoSlay timeouts and behavior for subsequent runs.

    Args:
        run_timeout_seconds: Max time for an entire run (default ~1500s / 25min).
        room_timeout_seconds: Max time per room (default ~120s / 2min).
        screen_timeout_seconds: Max time per screen (default ~30s).
        polling_interval_ms: How often AutoSlay polls game state (default ~100ms).
        watchdog_timeout_seconds: Stall detection timeout (default ~30s).
        max_floor: Maximum floor to play to (default ~49).
    """
    params: dict[str, Any] = {}
    if run_timeout_seconds is not None:
        params["run_timeout_seconds"] = run_timeout_seconds
    if room_timeout_seconds is not None:
        params["room_timeout_seconds"] = room_timeout_seconds
    if screen_timeout_seconds is not None:
        params["screen_timeout_seconds"] = screen_timeout_seconds
    if polling_interval_ms is not None:
        params["polling_interval_ms"] = polling_interval_ms
    if watchdog_timeout_seconds is not None:
        params["watchdog_timeout_seconds"] = watchdog_timeout_seconds
    if max_floor is not None:
        params["max_floor"] = max_floor
    return send_request("autoslay_configure", params)
