"""Tests for bridge expansion (new bridge tools).

These tests require the game running with the MCPTest bridge mod loaded.
Auto-skipped if bridge is not reachable.
"""

import pytest

from tests.conftest import skip_no_bridge


@skip_no_bridge
class TestBridgeGetCardPiles:
    """Test bridge_get_card_piles — requires being in combat."""

    def test_returns_dict(self):
        from sts2mcp.bridge_client import get_card_piles
        result = get_card_piles()
        # Either returns piles or an error (not in combat)
        assert isinstance(result, dict)
        if "error" not in result.get("result", result):
            r = result.get("result", result)
            assert "hand" in r
            assert "draw_pile" in r
            assert "discard_pile" in r
            assert "exhaust_pile" in r


@skip_no_bridge
class TestBridgeUsePotion:
    def test_returns_dict(self):
        from sts2mcp.bridge_client import use_potion
        result = use_potion(potion_index=0)
        assert isinstance(result, dict)
        # May fail if no potions — that's OK, we just verify no crash


@skip_no_bridge
class TestBridgeMakeEventChoice:
    def test_returns_dict(self):
        from sts2mcp.bridge_client import make_event_choice
        result = make_event_choice(choice_index=0)
        assert isinstance(result, dict)


@skip_no_bridge
class TestBridgeNavigateMap:
    def test_returns_dict(self):
        from sts2mcp.bridge_client import navigate_map
        result = navigate_map(row=1, col=0)
        assert isinstance(result, dict)


@skip_no_bridge
class TestBridgeRestSiteChoice:
    def test_returns_dict(self):
        from sts2mcp.bridge_client import rest_site_choice
        result = rest_site_choice(choice="rest")
        assert isinstance(result, dict)


@skip_no_bridge
class TestBridgeShopAction:
    def test_returns_dict(self):
        from sts2mcp.bridge_client import shop_action
        result = shop_action(action="buy_card", index=0)
        assert isinstance(result, dict)


@skip_no_bridge
class TestBridgeManipulateState:
    def test_single_change(self):
        from sts2mcp.bridge_client import manipulate_state
        result = manipulate_state({"gold": 100})
        assert isinstance(result, dict)

    def test_multiple_changes(self):
        from sts2mcp.bridge_client import manipulate_state
        result = manipulate_state({
            "gold": 50,
            "hp": 999,
            "godmode": True,
        })
        assert isinstance(result, dict)


# ─── Existing Bridge Tools (regression) ──────────────────────────────────────


@skip_no_bridge
class TestExistingBridge:
    def test_ping(self):
        from sts2mcp.bridge_client import ping
        result = ping()
        r = result.get("result", result)
        assert r.get("status") == "ok"

    def test_get_screen(self):
        from sts2mcp.bridge_client import get_screen
        result = get_screen()
        r = result.get("result", result)
        assert "screen" in r

    def test_get_run_state(self):
        from sts2mcp.bridge_client import get_run_state
        result = get_run_state()
        assert isinstance(result, dict)

    def test_get_player_state(self):
        from sts2mcp.bridge_client import get_player_state
        result = get_player_state()
        assert isinstance(result, dict)

    def test_get_map_state(self):
        from sts2mcp.bridge_client import get_map_state
        result = get_map_state()
        assert isinstance(result, dict)

    def test_get_available_actions(self):
        from sts2mcp.bridge_client import get_available_actions
        result = get_available_actions()
        assert isinstance(result, dict)

    def test_console_command(self):
        from sts2mcp.bridge_client import execute_console_command
        result = execute_console_command("help")
        assert isinstance(result, dict)
