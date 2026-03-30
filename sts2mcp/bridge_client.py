"""TCP client for communicating with the MCPTest bridge mod running inside STS2."""

import json
import socket
import sys
import time
from typing import Any, Optional, Sequence

BRIDGE_HOST = "127.0.0.1"
BRIDGE_PORT = 21337
TIMEOUT = 12.0  # Must exceed MainThreadDispatcher.Invoke's 10s timeout
MAX_RESPONSE_SIZE = 10 * 1024 * 1024  # 10 MB
RECV_BUFFER_SIZE = 4096
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


def send_request(method: str, params: dict | None = None, request_id: int = 1, timeout: float | None = None) -> dict:
    """Send a JSON-RPC request to the bridge mod and return the parsed response."""
    request = {"method": method, "id": request_id}
    if params:
        request["params"] = params

    effective_timeout = timeout if timeout is not None else TIMEOUT
    try:
        with socket.create_connection((BRIDGE_HOST, BRIDGE_PORT), timeout=effective_timeout) as sock:
            sock.settimeout(effective_timeout)
            payload = json.dumps(request) + "\n"
            sock.sendall(payload.encode("utf-8"))

            # Read response (newline-delimited)
            data = b""
            while True:
                chunk = sock.recv(RECV_BUFFER_SIZE)
                if not chunk:
                    break
                data += chunk
                if len(data) > MAX_RESPONSE_SIZE:
                    return {"error": f"Bridge response exceeded {MAX_RESPONSE_SIZE} bytes"}
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


def get_full_state() -> dict:
    """Get a compact combined game state in ONE call.

    Returns: screen, screen_context (raw type name for disambiguation),
    run info (act/floor/HP/gold), player info (deck/relics/potions),
    combat state (if fighting), and all available actions.
    """
    # Available actions includes screen info already
    actions_raw = get_available_actions()
    actions_result = _payload(actions_raw)

    screen = "UNKNOWN"
    screen_context = None
    if isinstance(actions_result, dict) and not actions_result.get("error"):
        screen = actions_result.get("screen", "UNKNOWN")
        screen_context = actions_result.get("screen_context_type")

    # If get_available_actions failed, fall back to get_screen
    if screen == "UNKNOWN":
        screen_raw = get_screen()
        screen_result = _payload(screen_raw)
        screen = screen_result.get("screen", "UNKNOWN") if isinstance(screen_result, dict) else "UNKNOWN"

    state: dict[str, Any] = {"screen": screen}
    if screen_context:
        state["screen_context"] = screen_context

    # Hint for ambiguous screens
    if screen == "CARD_SELECTION" and screen_context:
        ctx = screen_context.lower()
        if "reward" in ctx or "draft" in ctx:
            state["screen_hint"] = "card_reward_pick"
        elif "upgrade" in ctx or "smith" in ctx:
            state["screen_hint"] = "card_upgrade"
        elif "remove" in ctx or "purge" in ctx:
            state["screen_hint"] = "card_remove"
        elif "transform" in ctx:
            state["screen_hint"] = "card_transform"
        elif "scry" in ctx or "divination" in ctx:
            state["screen_hint"] = "scry"
        else:
            state["screen_hint"] = "card_selection"
    elif screen == "REWARD" and screen_context:
        ctx = screen_context.lower()
        if "boss" in ctx:
            state["screen_hint"] = "boss_relic_reward"
        else:
            state["screen_hint"] = "standard_reward"

    # Add run + player info if in a run
    in_run = screen not in ("MAIN_MENU", "CHARACTER_SELECT", "UNKNOWN")
    if in_run:
        player_raw = get_player_state()
        player_result = _payload(player_raw)
        if isinstance(player_result, dict) and not player_result.get("error"):
            state["player"] = player_result
        else:
            # Fallback to run state for basic info
            run_raw = get_run_state()
            run_result = _payload(run_raw)
            if isinstance(run_result, dict) and not run_result.get("error"):
                state["run"] = {
                    k: run_result[k]
                    for k in ("act", "floor", "hp", "max_hp", "gold", "character", "ascension", "seed")
                    if k in run_result
                }

    # Add combat info if in combat
    if "COMBAT" in screen and "LOADING" not in screen:
        combat_raw = get_combat_state()
        combat_result = _payload(combat_raw)
        if isinstance(combat_result, dict) and not combat_result.get("error"):
            state["combat"] = combat_result

    # Available actions — the key to knowing what to do
    if isinstance(actions_result, dict) and not actions_result.get("error"):
        state["available_actions"] = actions_result
    else:
        state["available_actions"] = {"actions": [], "error": "Could not fetch actions"}

    return state


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


def act_and_wait(action: str, settle_timeout: float = 5.0, **params: Any) -> dict:
    """Execute an action, wait for the game to settle, then return the new full state.

    This is the recommended way to interact with the game. It combines three steps:
    1. Execute the action via execute_action()
    2. Wait for the screen to stabilize (animations, transitions)
    3. Return the new full game state so you know exactly where you are

    Args:
        action: Action name (play_card, end_turn, reward_select, card_select, etc.)
        settle_timeout: Max seconds to wait for screen to stabilize after action.
        **params: Action-specific parameters (card_index, target_index, choice_index, etc.)

    Returns:
        Dict with: action_result (raw action response), then the full state
        (screen, player, combat, available_actions). If the action failed,
        includes error and still returns current state for recovery.
    """
    # Special-case: play_card and end_turn go via their dedicated handlers (more reliable)
    normalized = action.strip().lower()
    if normalized == "play_card":
        action_result = play_card(
            card_index=params.get("card_index", 0),
            target_index=params.get("target_index", -1),
        )
    elif normalized == "end_turn":
        action_result = end_turn()
    elif normalized == "use_potion":
        action_result = use_potion(
            potion_index=params.get("potion_index", 0),
            target_index=params.get("target_index", -1),
        )
    else:
        action_result = execute_action(action, **params)

    action_result = _payload(action_result)
    action_error = action_result.get("error") if isinstance(action_result, dict) else None

    # Wait for the screen to stabilize
    deadline = time.monotonic() + settle_timeout
    prev_screen = ""
    stable_count = 0
    while time.monotonic() < deadline:
        raw = get_screen()
        result = _payload(raw)
        current = result.get("screen", "UNKNOWN") if isinstance(result, dict) else "UNKNOWN"

        # Consider stable when same interactive screen appears twice
        if current == prev_screen and any(s in current.upper() for s in _STABLE_SCREENS):
            stable_count += 1
            if stable_count >= 1:
                break
        else:
            stable_count = 0
        prev_screen = current
        time.sleep(0.3)

    # Get the new full state
    new_state = get_full_state()

    # Combine action result with new state
    output: dict[str, Any] = {}
    if action_error:
        output["action_error"] = action_error
    output["action_result"] = action_result
    output.update(new_state)
    return output


def use_potion(potion_index: int, target_index: int = -1) -> dict:
    return send_request("use_potion", {"potion_index": potion_index, "target_index": target_index})


def discard_potion(potion_index: int) -> dict:
    return execute_action("discard_potion", potion_index=potion_index)


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


def hot_reload(
    dll_path: str,
    tier: int = 2,
    pck_path: str = "",
    pool_registrations: list[dict] | None = None,
) -> dict:
    """Full hot reload: patches + entities + localization + optional PCK.

    Tiers:
        1 = Harmony patches only (same as hot_swap_patches)
        2 = patches + entity models (ModelDb re-registration) + localization
        3 = tier 2 + PCK resource remount

    Args:
        dll_path: Path to the newly built mod DLL.
        tier: Reload tier (1, 2, or 3).
        pck_path: Path to PCK file (tier 3 only).
        pool_registrations: List of {"pool_type": "...", "model_type": "..."} dicts
            for re-registering entities into card/relic/potion pools. Pass an
            explicit empty list to disable bridge-side pool auto-discovery.

    Retries up to 3 times with exponential backoff for transient errors:
    - "already in progress" (previous reload still running)
    - Connection refused (game briefly unresponsive)
    - Socket timeout (long reload in progress)
    """
    params: dict[str, Any] = {"dll_path": dll_path, "tier": tier}
    if pck_path:
        params["pck_path"] = pck_path
    if pool_registrations is not None:
        params["pool_registrations"] = pool_registrations

    _RETRYABLE_ERRORS = ("already in progress", "bridge not running", "bridge timed out")
    result: dict = {}
    for attempt in range(3):
        result = send_request("hot_reload", params, timeout=30.0)
        payload = result
        if isinstance(result, dict) and isinstance(result.get("result"), dict):
            payload = result["result"]
        error = payload.get("error", "") if isinstance(payload, dict) else ""
        if any(msg in error.lower() for msg in _RETRYABLE_ERRORS) and attempt < 2:
            time.sleep(1 * (2 ** attempt))  # 1s, 2s
            continue
        return result
    return result


def reload_localization() -> dict:
    """Reload localization tables without rebuilding. Picks up changed JSON files."""
    return send_request("reload_localization")


def reload_history() -> dict:
    """Get the last N hot reload results with timestamps and diagnostics."""
    return send_request("reload_history")


def hot_reload_progress() -> dict:
    """Get the current hot reload step (if a reload is in progress)."""
    return send_request("hot_reload_progress")


def refresh_live_instances() -> dict:
    """Refresh live card/relic/power instances in the scene tree after hot reload.

    Walks the Godot scene tree and re-sets Model properties on NCard, NRelic, and
    NPower nodes to fresh instances from ModelDb. This makes changes visible
    immediately in the current combat/run without requiring a new encounter.

    Called automatically as part of tier 2+ hot_reload, but can also be invoked
    standalone to force a visual refresh.
    """
    return send_request("refresh_live_instances")


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


# ─── Menu Navigation (works without window focus) ────────────────────────────


def navigate_menu(target: str) -> dict:
    """Navigate main menu programmatically. Works even when game isn't focused.

    Args:
        target: "continue" | "compendium" | "card_library" | "settings" | "profile" | "timeline" | "multiplayer" | "new_run" | "abandon" | "back"
    """
    return send_request("navigate_menu", {"target": target})


def click_node(path: str) -> dict:
    """Click a Godot node by its scene tree path. Works without window focus.

    Args:
        path: Godot node path (e.g. "/root/NGame/MainMenu/ContinueButton")
    """
    return send_request("click_node", {"path": path})


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


# ── Navigation & Window Helpers ──────────────────────────────────────────────


_STABLE_SCREENS = frozenset({
    "COMBAT_PLAYER_TURN", "MAP", "EVENT", "SHOP", "REST_SITE", "TREASURE",
    "REWARD", "CARD_SELECTION", "MAIN_MENU", "CHARACTER_SELECT", "GAME_OVER",
    "SETTINGS", "TIMELINE",
})

# Screens where the agent needs to actively interact (not loading/transitioning)
_INTERACTIVE_SCREENS = frozenset({
    "COMBAT_PLAYER_TURN", "MAP", "EVENT", "SHOP", "REST_SITE", "TREASURE",
    "REWARD", "CARD_SELECTION",
})


def wait_for_screen(
    target_screen: str,
    timeout_seconds: float = 15,
    poll_interval: float = 0.5,
) -> dict:
    """Poll until the game reaches a screen matching *target_screen* (case-insensitive substring)."""
    deadline = time.monotonic() + timeout_seconds
    last_screen = "UNKNOWN"
    while time.monotonic() < deadline:
        raw = get_screen()
        result = _payload(raw)
        if isinstance(result, dict) and not result.get("error"):
            last_screen = result.get("screen", "UNKNOWN")
            if target_screen.upper() in last_screen.upper():
                return {"success": True, "screen": last_screen}
        time.sleep(poll_interval)
    return {
        "success": False,
        "error": f"Timed out ({timeout_seconds}s) waiting for '{target_screen}', last screen: {last_screen}",
        "last_screen": last_screen,
    }


def wait_until_idle(
    timeout_seconds: float = 10,
    poll_interval: float = 0.5,
) -> dict:
    """Poll until the game reaches a stable (interactive) screen."""
    deadline = time.monotonic() + timeout_seconds
    last_screen = "UNKNOWN"
    while time.monotonic() < deadline:
        raw = get_screen()
        result = _payload(raw)
        if isinstance(result, dict) and not result.get("error"):
            last_screen = result.get("screen", "UNKNOWN")
            if any(s in last_screen.upper() for s in _STABLE_SCREENS):
                return {"success": True, "screen": last_screen}
        time.sleep(poll_interval)
    return {
        "success": False,
        "error": f"Timed out ({timeout_seconds}s) waiting for idle, last screen: {last_screen}",
        "last_screen": last_screen,
    }


def focus_game_window() -> dict:
    """Bring the Slay the Spire 2 window to the foreground (Windows only)."""
    if sys.platform != "win32":
        return {"success": False, "error": "focus_game_window only supported on Windows"}
    try:
        import ctypes
        import ctypes.wintypes

        user32 = ctypes.windll.user32  # type: ignore[attr-defined]
        EnumWindows = user32.EnumWindows
        GetWindowTextW = user32.GetWindowTextW
        IsWindowVisible = user32.IsWindowVisible
        SetForegroundWindow = user32.SetForegroundWindow
        ShowWindow = user32.ShowWindow

        WNDENUMPROC = ctypes.WINFUNCTYPE(ctypes.c_bool, ctypes.wintypes.HWND, ctypes.wintypes.LPARAM)
        SW_RESTORE = 9
        found_hwnd: list[int] = []

        def _enum_callback(hwnd: int, _lp: int) -> bool:
            if IsWindowVisible(hwnd):
                buf = ctypes.create_unicode_buffer(256)
                GetWindowTextW(hwnd, buf, 256)
                title = buf.value
                if "Slay the Spire 2" in title or "SlayTheSpire2" in title:
                    found_hwnd.append(hwnd)
                    return False  # stop enumeration
            return True

        EnumWindows(WNDENUMPROC(_enum_callback), 0)
        if not found_hwnd:
            return {"success": False, "error": "Could not find game window"}
        hwnd = found_hwnd[0]
        ShowWindow(hwnd, SW_RESTORE)
        # Alt-key trick to allow SetForegroundWindow from background process
        user32.keybd_event(0x12, 0, 0, ctypes.wintypes.WPARAM(0))  # Alt down
        SetForegroundWindow(hwnd)
        user32.keybd_event(0x12, 0, 2, ctypes.wintypes.WPARAM(0))  # Alt up
        return {"success": True, "hwnd": hwnd}
    except Exception as exc:
        return {"success": False, "error": str(exc)}


def click_in_game(rel_x: float, rel_y: float) -> dict:
    """Click at a relative position (0.0-1.0) within the game window (Windows only).

    Useful for dismissing UI overlays that the bridge console commands can't reach.
    """
    if sys.platform != "win32":
        return {"success": False, "error": "click_in_game only supported on Windows"}
    try:
        import ctypes
        import ctypes.wintypes

        user32 = ctypes.windll.user32  # type: ignore[attr-defined]

        class RECT(ctypes.Structure):
            _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                        ("right", ctypes.c_long), ("bottom", ctypes.c_long)]

        # Find the game window
        focus_result = focus_game_window()
        if not focus_result.get("success"):
            return focus_result

        hwnd = focus_result["hwnd"]
        rect = RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(rect))

        x = rect.left + int((rect.right - rect.left) * rel_x)
        y = rect.top + int((rect.bottom - rect.top) * rel_y)

        user32.SetCursorPos(x, y)
        time.sleep(0.15)
        user32.mouse_event(0x0002, 0, 0, 0, ctypes.wintypes.WPARAM(0))  # LEFT_DOWN
        user32.mouse_event(0x0004, 0, 0, 0, ctypes.wintypes.WPARAM(0))  # LEFT_UP
        return {"success": True, "x": x, "y": y}
    except Exception as exc:
        return {"success": False, "error": str(exc)}


def navigate_to_combat(
    timeout: int = 60,
    neow_choice_index: int = 0,
    focus_first: bool = True,
) -> dict:
    """Navigate from any screen to the first combat encounter.

    Handles Neow events, card selections, reward screens, map navigation,
    and other intermediate screens automatically.
    """
    if focus_first:
        focus_game_window()
        time.sleep(0.3)

    deadline = time.monotonic() + timeout
    steps_taken = 0
    last_screen = "UNKNOWN"
    stuck_count = 0
    prev_screen = ""

    while time.monotonic() < deadline:
        # Ensure game window has focus for scene transitions
        focus_game_window()

        raw = get_screen()
        result = _payload(raw)
        if isinstance(result, dict) and result.get("error"):
            time.sleep(1)
            continue

        screen = result.get("screen", "UNKNOWN").upper() if isinstance(result, dict) else "UNKNOWN"
        last_screen = screen

        # Detect being stuck on the same screen
        if screen == prev_screen:
            stuck_count += 1
            if stuck_count > 8:
                return {
                    "success": False,
                    "error": f"Stuck on screen '{screen}' after {steps_taken} steps",
                    "steps_taken": steps_taken,
                    "last_screen": screen,
                }
        else:
            stuck_count = 0
        prev_screen = screen

        # ── Already in combat ──
        if "COMBAT" in screen and "LOADING" not in screen:
            return {"success": True, "screen": screen, "steps_taken": steps_taken}

        # ── Loading / transitioning ──
        if "LOADING" in screen or "TRANSITION" in screen:
            time.sleep(1)
            continue

        # ── No run in progress ──
        if "MAIN_MENU" in screen or "CHARACTER_SELECT" in screen:
            return {
                "success": False,
                "error": "No run in progress — start a run first with bridge_start_run",
                "steps_taken": steps_taken,
                "last_screen": screen,
            }

        # ── Event screen (Neow or other) ──
        if "EVENT" in screen:
            make_event_choice(neow_choice_index)
            time.sleep(2)
            # Check if we need to proceed or dismiss a sub-screen
            check = _payload(get_screen()).get("screen", "").upper()
            if "EVENT" in check:
                # Click Proceed button at bottom center
                click_in_game(0.30, 0.92)
                time.sleep(2)
            steps_taken += 1
            continue

        # ── Card selection screen ──
        if "CARD" in screen or "TRANSFORM" in screen or "DECK" in screen:
            card_skip()
            time.sleep(1.5)
            check = _payload(get_screen()).get("screen", "").upper()
            if "CARD" in check or "TRANSFORM" in check:
                click_in_game(0.50, 0.83)
                time.sleep(1.5)
            steps_taken += 1
            continue

        # ── Reward screen ──
        if "REWARD" in screen:
            reward_proceed()
            time.sleep(1.5)
            check = _payload(get_screen()).get("screen", "").upper()
            if "REWARD" in check:
                click_in_game(0.85, 0.77)
                time.sleep(1.5)
            steps_taken += 1
            continue

        # ── Treasure screen ──
        if "TREASURE" in screen:
            treasure_proceed()
            time.sleep(1)
            check = _payload(get_screen()).get("screen", "").upper()
            if "TREASURE" in check:
                click_in_game(0.50, 0.92)
                time.sleep(1)
            steps_taken += 1
            continue

        # ── Rest site ──
        if "REST" in screen:
            rest_site_choice("rest")
            time.sleep(1)
            rest_site_proceed()
            steps_taken += 1
            time.sleep(1)
            continue

        # ── Shop ──
        if "SHOP" in screen:
            shop_proceed()
            steps_taken += 1
            time.sleep(1)
            continue

        # ── Map screen — navigate to a combat node ──
        if "MAP" in screen:
            map_state = _payload(get_map_state())
            if isinstance(map_state, dict) and not map_state.get("error"):
                nodes = map_state.get("nodes", [])
                target = None
                fallback = None
                for node in nodes:
                    if not node.get("available"):
                        continue
                    if fallback is None:
                        fallback = node
                    ntype = (node.get("type") or "").lower()
                    if ntype in ("monster", "elite", "boss", "combat"):
                        target = node
                        break
                chosen = target or fallback
                if chosen:
                    navigate_map(chosen["row"], chosen["col"])
                    time.sleep(2)
                    # Check if we transitioned
                    check = _payload(get_screen()).get("screen", "").upper()
                    if "MAP" in check:
                        # Bridge navigate worked at data level but scene needs click
                        # Try clicking Proceed if it appeared
                        click_in_game(0.30, 0.92)
                        time.sleep(2)
                    steps_taken += 1
                    continue
            # Fallback: console fight
            execute_console_command("fight")
            time.sleep(2)
            check = _payload(get_screen()).get("screen", "").upper()
            if "MAP" in check:
                click_in_game(0.30, 0.92)
                time.sleep(2)
            steps_taken += 1
            continue

        # ── Game over ──
        if "GAME_OVER" in screen:
            return {
                "success": False,
                "error": "Run ended (game over)",
                "steps_taken": steps_taken,
                "last_screen": screen,
            }

        # ── Unknown screen — try generic proceed ──
        proceed()
        steps_taken += 1
        time.sleep(1)

    return {
        "success": False,
        "error": f"Timed out after {timeout}s",
        "steps_taken": steps_taken,
        "last_screen": last_screen,
    }


def auto_proceed(
    skip_cards: bool = True,
    skip_rewards: bool = False,
    timeout_seconds: float = 15,
) -> dict:
    """Automatically advance past non-combat screens. Returns full state when done.

    Handles the full chain: loading → event → card selection → rewards → treasure →
    rest → shop → MENU_* screens. Stops when it reaches a screen requiring a decision.

    Returns the new full game state (via get_full_state) plus what steps were taken
    and what kind of decision is needed (if any).
    """
    deadline = time.monotonic() + timeout_seconds
    steps: list[str] = []
    prev_screen = ""
    stuck_count = 0

    while time.monotonic() < deadline:
        raw = get_screen()
        result = _payload(raw)
        screen = (result.get("screen", "UNKNOWN") if isinstance(result, dict) else "UNKNOWN").upper()

        # Stuck detection — same screen 5+ iterations means we can't proceed
        if screen == prev_screen:
            stuck_count += 1
            if stuck_count >= 5:
                state = get_full_state()
                state["steps"] = steps
                state["stuck"] = True
                state["error"] = f"Stuck on {screen} after {len(steps)} steps — needs manual interaction"
                return state
        else:
            stuck_count = 0
        prev_screen = screen

        # ── Decision-required screens — stop and return full state ──
        if "COMBAT" in screen and "LOADING" not in screen:
            state = get_full_state()
            state["steps"] = steps
            state["needs_decision"] = "combat"
            return state

        if screen == "MAP":
            state = get_full_state()
            state["steps"] = steps
            state["needs_decision"] = "map_navigation"
            return state

        # ── Loading / transition — wait ──
        if "LOADING" in screen or "TRANSITION" in screen or screen == "UNKNOWN":
            time.sleep(0.5)
            continue

        # ── Enemy turn — wait for our turn ──
        if "ENEMY" in screen:
            time.sleep(0.5)
            continue

        # ── Terminal screens ──
        if "GAME_OVER" in screen:
            state = get_full_state()
            state["steps"] = steps
            state["error"] = "Run ended (game over)"
            return state
        if "MAIN_MENU" in screen or "CHARACTER_SELECT" in screen:
            state = get_full_state()
            state["steps"] = steps
            state["error"] = "No run in progress"
            return state

        # ── Card selection ──
        if "CARD_SELECTION" in screen or ("CARD" in screen and "SELECT" in screen):
            if skip_cards:
                card_skip()
                steps.append("card_skip")
                time.sleep(1)
                # Card skip might not work (e.g. mandatory selection) — try confirm too
                confirm_raw = get_screen()
                confirm_result = _payload(confirm_raw)
                still_on = (confirm_result.get("screen", "") if isinstance(confirm_result, dict) else "").upper()
                if "CARD" in still_on and "SELECT" in still_on:
                    card_confirm()
                    steps.append("card_confirm (skip failed, trying confirm)")
                    time.sleep(1)
            else:
                state = get_full_state()
                state["steps"] = steps
                state["needs_decision"] = "card_selection"
                return state
            continue

        # ── Reward screen ──
        if "REWARD" in screen:
            if skip_rewards:
                reward_proceed()
                steps.append("reward_proceed")
                time.sleep(1)
            else:
                state = get_full_state()
                state["steps"] = steps
                state["needs_decision"] = "reward_selection"
                return state
            continue

        # ── Treasure ──
        if "TREASURE" in screen:
            treasure_proceed()
            steps.append("treasure_proceed")
            time.sleep(1)
            continue

        # ── Rest site ──
        if "REST" in screen:
            rest_site_choice("rest")
            steps.append("rest_choice:rest")
            time.sleep(1)
            rest_site_proceed()
            steps.append("rest_proceed")
            time.sleep(1)
            continue

        # ── Shop ──
        if "SHOP" in screen or "MERCHANT" in screen:
            shop_proceed()
            steps.append("shop_proceed")
            time.sleep(1)
            continue

        # ── Event ──
        if "EVENT" in screen:
            make_event_choice(0)
            steps.append("event_choice:0")
            time.sleep(1.5)
            proceed()
            steps.append("proceed")
            time.sleep(1)
            continue

        # ── Settings / Timeline / other menus ──
        if screen in ("SETTINGS", "TIMELINE") or screen.startswith("MENU_"):
            proceed()
            steps.append(f"proceed ({screen})")
            time.sleep(1)
            continue

        # ── Fallback — generic proceed for any unknown screen ──
        proceed()
        steps.append(f"proceed (unknown: {screen})")
        time.sleep(1)

    # Timed out — return current state anyway
    state = get_full_state()
    state["steps"] = steps
    state["error"] = f"Timed out after {timeout_seconds}s"
    return state
