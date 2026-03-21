"""Tests for MCP server tool registration and handler dispatching.

Verifies all 65 tools are registered and handlers exist.
"""

import pytest


class TestToolRegistration:
    """Verify all tools are defined in list_tools."""

    @pytest.fixture(autouse=True)
    def setup(self):
        from sts2mcp.server import list_tools
        import asyncio
        self.tools = asyncio.run(list_tools())
        self.tool_names = {t.name for t in self.tools}

    def test_total_count(self):
        assert len(self.tools) >= 65, f"Expected at least 65 tools, got {len(self.tools)}"

    # ── Original tools (41) ──

    def test_game_data_tools(self):
        expected = {
            "list_entities", "get_entity_source", "search_game_code",
            "list_hooks", "get_game_info", "get_console_commands",
            "browse_namespace", "get_modding_guide",
        }
        assert expected.issubset(self.tool_names), f"Missing: {expected - self.tool_names}"

    def test_mod_creation_tools(self):
        expected = {
            "create_mod_project", "generate_card", "generate_relic",
            "generate_power", "generate_potion", "generate_monster",
            "generate_encounter", "generate_harmony_patch", "generate_localization",
        }
        assert expected.issubset(self.tool_names)

    def test_baselib_tools(self):
        expected = {"generate_character", "generate_mod_config", "get_baselib_reference"}
        assert expected.issubset(self.tool_names)

    def test_build_deploy_tools(self):
        expected = {
            "build_mod", "install_mod", "uninstall_mod",
            "list_installed_mods", "launch_game", "decompile_game",
        }
        assert expected.issubset(self.tool_names)

    def test_asset_tools(self):
        expected = {
            "build_pck", "list_pck", "scaffold_character_assets",
            "get_character_asset_paths",
        }
        assert expected.issubset(self.tool_names)

    def test_original_bridge_tools(self):
        expected = {
            "bridge_ping", "bridge_get_screen", "bridge_get_run_state",
            "bridge_get_combat_state", "bridge_get_player_state",
            "bridge_get_map_state", "bridge_get_available_actions",
            "bridge_start_run", "bridge_play_card", "bridge_end_turn",
            "bridge_console",
        }
        assert expected.issubset(self.tool_names)

    # ── New generators (10) ──

    def test_new_generator_tools(self):
        expected = {
            "generate_event", "generate_orb", "generate_enchantment",
            "generate_game_action", "generate_mechanic",
            "generate_custom_tooltip", "generate_save_data",
            "generate_test_scenario", "generate_vfx_scene",
        }
        assert expected.issubset(self.tool_names), f"Missing: {expected - self.tool_names}"

    # ── Code intelligence (4) ──

    def test_intelligence_tools(self):
        expected = {
            "suggest_patches", "analyze_method_callers",
            "get_entity_relationships", "search_hooks_by_signature",
        }
        assert expected.issubset(self.tool_names)

    # ── Validation & compatibility (4) ──

    def test_validation_tools(self):
        expected = {
            "validate_mod", "diff_game_versions",
            "check_mod_compatibility", "list_game_vfx",
        }
        assert expected.issubset(self.tool_names)

    # ── New bridge tools (7) ──

    def test_new_bridge_tools(self):
        expected = {
            "bridge_use_potion", "bridge_make_event_choice",
            "bridge_navigate_map", "bridge_rest_site_choice",
            "bridge_shop_action", "bridge_get_card_piles",
            "bridge_manipulate_state",
        }
        assert expected.issubset(self.tool_names), f"Missing: {expected - self.tool_names}"

    # ── Tool schema validation ──

    def test_all_tools_have_schemas(self):
        for tool in self.tools:
            assert tool.inputSchema is not None, f"Tool {tool.name} has no inputSchema"
            assert "type" in tool.inputSchema, f"Tool {tool.name} schema missing 'type'"
            assert tool.inputSchema["type"] == "object"

    def test_all_tools_have_descriptions(self):
        for tool in self.tools:
            assert tool.description, f"Tool {tool.name} has empty description"
            assert len(tool.description) > 10, f"Tool {tool.name} description too short"

    def test_required_params_are_in_properties(self):
        """Every 'required' param should exist in 'properties'."""
        for tool in self.tools:
            schema = tool.inputSchema
            required = schema.get("required", [])
            properties = schema.get("properties", {})
            for param in required:
                assert param in properties, \
                    f"Tool {tool.name}: required param '{param}' not in properties"


class TestHandlerCoverage:
    """Verify every registered tool has a handler in _handle_tool."""

    def test_all_tools_handled(self):
        """Parse server.py to confirm every tool name appears in a handler branch."""
        from pathlib import Path
        import re

        server_path = Path(__file__).parent.parent / "sts2mcp" / "server.py"
        content = server_path.read_text()

        # Get all tool names from registration
        tool_names = re.findall(r'types\.Tool\(\s*name="(\w+)"', content)
        assert len(tool_names) >= 65, f"Expected at least 65 tools, got {len(tool_names)}"

        # Get all handler branches
        handler_names = set()
        handler_names.update(re.findall(r'if name == "(\w+)"', content))
        handler_names.update(re.findall(r'elif name == "(\w+)"', content))

        missing = set(tool_names) - handler_names
        assert not missing, f"Tools without handlers: {missing}"


class TestModuleImports:
    """Verify all modules import cleanly."""

    def test_import_server(self):
        from sts2mcp import server
        assert hasattr(server, "server")
        assert hasattr(server, "game_data")
        assert hasattr(server, "mod_gen")
        assert hasattr(server, "analyzer")

    def test_import_analysis(self):
        from sts2mcp.analysis import CodeAnalyzer
        assert callable(CodeAnalyzer)

    def test_import_mod_gen(self):
        from sts2mcp.mod_gen import ModGenerator
        mg = ModGenerator(".")
        # Verify new methods exist
        assert hasattr(mg, "generate_event")
        assert hasattr(mg, "generate_orb")
        assert hasattr(mg, "generate_enchantment")
        assert hasattr(mg, "generate_game_action")
        assert hasattr(mg, "generate_mechanic")
        assert hasattr(mg, "generate_custom_tooltip")
        assert hasattr(mg, "generate_save_data")
        assert hasattr(mg, "generate_test_scenario")
        assert hasattr(mg, "generate_vfx_scene")

    def test_import_bridge_client(self):
        from sts2mcp import bridge_client
        # Verify new functions exist
        assert callable(bridge_client.use_potion)
        assert callable(bridge_client.make_event_choice)
        assert callable(bridge_client.navigate_map)
        assert callable(bridge_client.rest_site_choice)
        assert callable(bridge_client.shop_action)
        assert callable(bridge_client.get_card_piles)
        assert callable(bridge_client.manipulate_state)
