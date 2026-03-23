"""Shared fixtures for STS2 modding MCP tests."""

import os
import shutil
import tempfile
from pathlib import Path

import pytest

# Paths
PROJECT_ROOT = Path(__file__).parent.parent
DECOMPILED_DIR = os.environ.get(
    "STS2_DECOMPILED_DIR",
    str(PROJECT_ROOT / "decompiled"),
)
def _find_game_dir() -> str:
    """Resolve game dir from env var, config file, or auto-detection."""
    env = os.environ.get("STS2_GAME_DIR")
    if env:
        return env
    try:
        from sts2mcp.setup import load_config, find_game_install
        config_dir = load_config().get("game_dir")
        if config_dir:
            return config_dir
        found = find_game_install()
        if found:
            return found
    except Exception:
        pass
    return ""

GAME_DIR = _find_game_dir()

# Conditions
HAS_DECOMPILED = Path(DECOMPILED_DIR).exists() and any(Path(DECOMPILED_DIR).iterdir())
HAS_GAME = Path(GAME_DIR).exists()
HAS_DOTNET = shutil.which("dotnet") is not None

skip_no_decompiled = pytest.mark.skipif(
    not HAS_DECOMPILED,
    reason="Decompiled source not available",
)
skip_no_game = pytest.mark.skipif(
    not HAS_GAME,
    reason="Game directory not available",
)
skip_no_dotnet = pytest.mark.skipif(
    not HAS_DOTNET,
    reason="dotnet CLI not available",
)


def _bridge_is_up() -> bool:
    """Check if the bridge mod is reachable."""
    try:
        from sts2mcp.bridge_client import is_connected
        return is_connected()
    except Exception:
        return False


BRIDGE_UP = _bridge_is_up()
skip_no_bridge = pytest.mark.skipif(
    not BRIDGE_UP,
    reason="Game bridge not running (need game + MCPTest mod)",
)


@pytest.fixture
def game_data():
    """GameDataIndex instance (requires decompiled source)."""
    from sts2mcp.game_data import GameDataIndex
    gd = GameDataIndex(DECOMPILED_DIR)
    gd.ensure_indexed()
    return gd


@pytest.fixture
def mod_gen():
    """ModGenerator instance."""
    from sts2mcp.mod_gen import ModGenerator
    return ModGenerator(GAME_DIR)


@pytest.fixture
def code_analyzer(game_data):
    """CodeAnalyzer instance."""
    from sts2mcp.analysis import CodeAnalyzer
    return CodeAnalyzer(game_data)


@pytest.fixture
def tmp_mod_dir():
    """Temporary directory for mod project tests, cleaned up after."""
    d = tempfile.mkdtemp(prefix="sts2_test_mod_")
    yield d
    shutil.rmtree(d, ignore_errors=True)
