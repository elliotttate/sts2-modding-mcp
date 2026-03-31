# Detailed Setup

The [Quick Start](../README.md#quick-start) covers the happy path. This page has additional details for manual setup, path configuration, and alternative AI tool configs.

## Manual Decompilation

If you skipped `python -m sts2mcp.setup` or need to re-decompile manually:

```bash
ilspycmd -p -o ./decompiled "<your Steam path>/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/sts2.dll"
```

Find your Steam path by right-clicking the game in Steam → Manage → Browse Local Files.

## GDRE Tools (Optional — Godot Assets)

`python -m sts2mcp.setup` can download GDRE Tools automatically. If you prefer to install manually, download the latest [release](https://github.com/GDRETools/gdsdecomp/releases) and extract to `tools/` so the binary ends up at `tools/gdre_tools.exe` (Windows) or `tools/gdre_tools` (macOS/Linux).

## Path Configuration

The server **auto-detects** your game installation on all platforms (Windows registry + Steam libraries, Linux `~/.steam`, macOS `~/Library/Application Support/Steam`). Running `python -m sts2mcp.setup` saves the result to `sts2mcp_config.json`.

If auto-detection doesn't find your game, edit `sts2mcp_config.json` at the project root:

```json
{
  "game_dir": "D:\\Games\\Slay the Spire 2",
  "decompiled_dir": "./decompiled",
  "gdre_tools_path": "./tools/gdre_tools.exe"
}
```

All keys are optional — only set what you need to override. Environment variables (`STS2_GAME_DIR`, `STS2_DECOMPILED_DIR`, `GDRE_TOOLS_PATH`) take highest priority.

## Claude Code — Project or User Scope

**Project scope** (`.mcp.json` in your working directory):

```json
{
  "mcpServers": {
    "sts2-modding": {
      "command": "/path/to/sts2-modding-mcp/venv/bin/python",
      "args": ["/path/to/sts2-modding-mcp/run.py"]
    }
  }
}
```

**User scope** (`~/.claude/mcp.json`, shared across all projects): same format.

> **Note:** Claude Code does **not** support `mcpServers` inside `~/.claude/settings.json`. Use `.mcp.json` (project), `~/.claude/mcp.json` (user), or `claude mcp add` instead.
