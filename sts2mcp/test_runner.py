"""Automated test scenario runner for STS2 mods."""

import time
from typing import Any

from . import bridge_client


def run_test_scenario(scenario: dict) -> dict:
    """Run a test scenario and return results.

    A scenario has:
    - name: scenario name
    - setup: run start params (character, ascension, seed, relics, cards, etc.)
    - steps: list of {action, params, assert, wait_for_screen, wait_idle, delay, stop_on_fail}
    """
    results: dict[str, Any] = {
        "scenario_name": scenario.get("name", "unnamed"),
        "steps": [],
        "passed": True,
        "error": None,
    }

    # Setup
    setup = scenario.get("setup", {})
    if setup:
        start_result = bridge_client.start_run(**setup)
        if isinstance(start_result, dict) and "error" in start_result:
            results["passed"] = False
            results["error"] = f"Setup failed: {start_result['error']}"
            return results

        wait = bridge_client.wait_until_idle(timeout_seconds=15)
        if not wait.get("success"):
            results["passed"] = False
            results["error"] = "Setup timed out waiting for idle"
            return results

    # Execute steps
    for i, step in enumerate(scenario.get("steps", [])):
        step_result = _execute_step(step, i)
        results["steps"].append(step_result)
        if not step_result.get("passed"):
            results["passed"] = False
            if step.get("stop_on_fail", True):
                break

    return results


def _execute_step(step: dict, index: int) -> dict:
    """Execute a single test step."""
    action = step.get("action", "")
    params = step.get("params", {})
    assertions = step.get("assert", {})
    wait_screen = step.get("wait_for_screen")
    wait_idle = step.get("wait_idle", False)
    delay = step.get("delay", 0)

    result: dict[str, Any] = {
        "step": index,
        "action": action,
        "passed": True,
        "action_result": None,
        "assertion_results": [],
    }

    # Execute action
    try:
        action_map = {
            "play_card": lambda: bridge_client.play_card(**params),
            "end_turn": lambda: bridge_client.end_turn(),
            "use_potion": lambda: bridge_client.use_potion(**params),
            "console": lambda: bridge_client.execute_console_command(params.get("command", "")),
            "manipulate_state": lambda: bridge_client.manipulate_state(params),
            "navigate_map": lambda: bridge_client.navigate_map(**params),
            "event_choice": lambda: bridge_client.make_event_choice(**params),
            "rest_choice": lambda: bridge_client.rest_site_choice(**params),
            "wait": lambda: (time.sleep(params.get("seconds", 1)), {"success": True})[1],
            "noop": lambda: {"success": True},
        }

        handler = action_map.get(action)
        if handler:
            result["action_result"] = handler()
        elif action == "execute_action":
            act_name = params.pop("action", "") if "action" in params else ""
            result["action_result"] = bridge_client.execute_action(act_name, **params)
        else:
            result["action_result"] = {"error": f"Unknown action: {action}"}
            result["passed"] = False
            return result
    except Exception as e:
        result["action_result"] = {"error": str(e)}
        result["passed"] = False
        return result

    # Check for action errors
    if isinstance(result["action_result"], dict) and "error" in result["action_result"]:
        result["passed"] = False
        return result

    # Wait if needed
    if delay > 0:
        time.sleep(delay)

    if wait_screen:
        wait_result = bridge_client.wait_for_screen(wait_screen, timeout_seconds=10)
        if not wait_result.get("success"):
            result["passed"] = False
            result["assertion_results"].append({
                "assertion": f"wait_for_screen({wait_screen})",
                "passed": False,
                "message": "Screen wait timed out",
            })
            return result
    elif wait_idle:
        wait_result = bridge_client.wait_until_idle(timeout_seconds=10)
        if not wait_result.get("success"):
            result["passed"] = False
            result["assertion_results"].append({
                "assertion": "wait_idle",
                "passed": False,
                "message": "Idle wait timed out",
            })
            return result

    # Check assertions
    if assertions:
        assertion_results = _check_assertions(assertions)
        result["assertion_results"] = assertion_results
        if not all(a["passed"] for a in assertion_results):
            result["passed"] = False

    return result


def _check_assertions(assertions: dict) -> list[dict]:
    """Check assertions against current game state."""
    state: dict[str, Any] = {}

    # Lazy-fetch state as needed
    needs_combat = any(
        k in ("hp", "max_hp", "block", "energy", "hand_size", "draw_pile",
               "discard_pile", "round", "in_combat") or k.startswith("enemy_") or k.startswith("has_power_") or k.startswith("power_")
        for k in assertions
    )
    needs_player = any(k in ("gold", "deck_count", "relic_count") for k in assertions)
    needs_screen = "screen" in assertions

    if needs_combat:
        state["combat"] = bridge_client._payload(bridge_client.get_combat_state())
    if needs_player:
        state["player"] = bridge_client._payload(bridge_client.get_player_state())
    if needs_screen:
        state["screen_data"] = bridge_client._payload(bridge_client.get_screen())

    results = []
    for key, expected in assertions.items():
        assertion_result: dict[str, Any] = {"assertion": f"{key} == {expected}", "passed": False, "actual": None}

        try:
            actual = _resolve_assertion_value(key, state)
            assertion_result["actual"] = actual

            if isinstance(expected, dict):
                op = expected.get("op", "eq")
                val = expected.get("value")
                ops = {
                    "gt": lambda a, v: a > v,
                    "lt": lambda a, v: a < v,
                    "gte": lambda a, v: a >= v,
                    "lte": lambda a, v: a <= v,
                    "not_eq": lambda a, v: a != v,
                    "contains": lambda a, v: v in a,
                    "eq": lambda a, v: a == v,
                }
                assertion_result["passed"] = ops.get(op, ops["eq"])(actual, val)
                assertion_result["assertion"] = f"{key} {op} {val}"
            else:
                assertion_result["passed"] = actual == expected
        except Exception as e:
            assertion_result["error"] = str(e)

        results.append(assertion_result)

    return results


def _resolve_assertion_value(key: str, state: dict) -> Any:
    """Resolve an assertion key to an actual value from game state."""
    combat = state.get("combat", {})
    player_data = state.get("player", {})
    screen_data = state.get("screen_data", {})

    if key == "screen":
        return screen_data.get("screen", "UNKNOWN")
    if key == "in_combat":
        return combat.get("in_combat", False)
    if key == "round":
        return combat.get("round", 0)

    # Player combat state
    players = combat.get("players", [])
    p = players[0] if players else {}

    simple_fields = {
        "hp": "hp", "max_hp": "max_hp", "block": "block",
        "energy": "energy", "hand_size": "hand_size",
        "draw_pile": "draw_pile", "discard_pile": "discard_pile",
    }
    if key in simple_fields:
        return p.get(simple_fields[key], 0)

    # Player run state
    player_players = player_data.get("players", [])
    pp = player_players[0] if player_players else {}

    if key == "gold":
        return pp.get("gold", 0)
    if key == "deck_count":
        return pp.get("deck_count", 0)
    if key == "relic_count":
        return len(pp.get("relics", []))

    # Enemy state
    if key.startswith("enemy_"):
        parts = key.split("_", 2)
        if len(parts) >= 3:
            idx = int(parts[1])
            field = parts[2]
            enemies = combat.get("enemies", [])
            if idx < len(enemies):
                return enemies[idx].get(field)

    # Power checks
    if key.startswith("has_power_"):
        power_name = key[len("has_power_"):]
        powers = p.get("powers", [])
        return any(pw.get("name") == power_name for pw in powers)

    if key.startswith("power_"):
        power_name = key[len("power_"):]
        powers = p.get("powers", [])
        match = next((pw for pw in powers if pw.get("name") == power_name), None)
        return match.get("amount", 0) if match else 0

    return None
