# STS2 Modding MCP

A [Model Context Protocol](https://modelcontextprotocol.io/) server for **Slay the Spire 2** modding. Connects to any MCP-compatible AI assistant (Claude Code, Claude Desktop, Cursor, Windsurf, etc.) and provides **151 tools** for reverse-engineering the game, generating mod code, building/deploying, live-inspecting the running Godot engine, and autonomously playtesting mods.

- **Reverse engineering** — decompiles C# assemblies with Roslyn syntax trees, indexes 3,048+ entities and 144 hooks, extracts 15,000+ Godot assets
- **Code generation** — production-ready C# for 30+ entity types, Harmony patches, Godot UI, VFX, network messages, and complete mod projects
- **Code intelligence** — hook recommendations from natural language, patch suggestions, call graphs, API compatibility checks
- **Build and deploy** — builds mods, packages Godot PCK files, deploys to the game, validates assets and localization, watches for changes
- **Live scene inspection** — browses the running Godot scene tree, reads/writes node properties, toggles visibility, animates with Tweens
- **Automated playtesting** — starts seeded runs, plays cards, navigates every screen, runs at 20x speed, captures screenshots, sets breakpoints
- **29 built-in guides** — hooks, Harmony, localization, multiplayer, Godot UI, IL transpilers, combat, save files, and more

> Your mileage will vary depending on which LLM you use. This project is a fun experiment — please ping me if you have issues, want to suggest a feature, or find a bug.

## Prerequisites

- **[Python 3.11+](https://www.python.org/downloads/)** — check with `python --version`
- **[.NET SDK 9.0](https://dotnet.microsoft.com/download/dotnet/9.0)** — for building mods, the Roslyn code analyzer, and decompilation
- **[.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)** — required by `ilspycmd` (see note below)
- **[ilspycmd](https://www.nuget.org/packages/ilspycmd/)** — `dotnet tool install -g ilspycmd` (for C# decompilation)
- **[GDRE Tools](https://github.com/GDRETools/gdsdecomp/releases)** — for Godot asset extraction (optional, setup wizard can download it)
- **Slay the Spire 2** — the game itself

> **.NET version note:** `ilspycmd` targets .NET 8.0. If you only have .NET 9.0+ installed, decompilation will fail with a runtime error. The fix is to **also** install the [.NET 8.0 runtime](https://dotnet.microsoft.com/download/dotnet/8.0) alongside your .NET 9.0 SDK. Both can coexist without issues.

## Quick Start

```bash
git clone https://github.com/elliotttate/sts2-modding-mcp.git
cd sts2-modding-mcp
python -m venv venv

# Activate the virtual environment:
source venv/bin/activate         # macOS / Linux
# source venv/Scripts/activate   # Windows (Git Bash)
# venv\Scripts\activate.bat      # Windows cmd
# venv\Scripts\Activate.ps1      # Windows PowerShell

pip install .
python -m sts2mcp.setup          # auto-finds game, installs tools, decompiles
```

The setup wizard automatically finds your Steam install, installs `ilspycmd` if needed, decompiles the game source, optionally downloads GDRE Tools for asset extraction, and builds the Roslyn code analyzer. In CI or non-interactive shells, use `python -m sts2mcp.setup -y` to auto-accept all prompts.

### Connect to an AI Assistant

The MCP server connects to any AI tool that supports the [Model Context Protocol](https://modelcontextprotocol.io/). Point the config at the **venv's Python** so dependencies are always available.

> Replace `/path/to/sts2-modding-mcp` with the actual path where you cloned the repo.

**Claude Code (CLI):**

```bash
# macOS / Linux:
claude mcp add sts2-modding /path/to/sts2-modding-mcp/venv/bin/python -- /path/to/sts2-modding-mcp/run.py

# Windows:
claude mcp add sts2-modding C:\path\to\sts2-modding-mcp\venv\Scripts\python.exe -- C:\path\to\sts2-modding-mcp\run.py
```

**Claude Desktop** — edit your config file (`%APPDATA%\Claude\claude_desktop_config.json` on Windows, `~/Library/Application Support/Claude/claude_desktop_config.json` on macOS):

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

On Windows, use full paths with backslashes: `"command": "C:\\Users\\YourName\\sts2-modding-mcp\\venv\\Scripts\\python.exe"`.

**Cursor / Windsurf / Other MCP Clients** — most editors use the same JSON config format. Check your editor's MCP documentation for where to place it.

### Verify It Works

- **Claude Desktop:** Click the hammer icon at the bottom of the chat input — you should see sts2-modding tools listed. Try: *"What modding guides are available?"*
- **Claude Code:** Run `/mcp` — you should see `sts2-modding` listed as connected. Try: *"Use get_game_info to show me the server status."*

If the server isn't connecting, run `python run.py` directly in the activated venv to check for startup errors.

## Usage Examples

Once connected, ask your AI assistant things like:

- *"Create a new mod called FlameForge with a card that deals 20 damage and applies 2 Vulnerable"*
- *"Which hook should I use to add extra card draw?"*
- *"Generate a relic that gives 3 Strength the first time you take damage each combat"*
- *"Build my mod and install it, then start a test run and play through a combat"*
- *"Show me the source code for the Bash card"*
- *"How do Harmony IL transpilers work?"*

## Updating After Game Patches

When STS2 updates:

1. **C# source** — run `decompile_game` (or manually re-run `ilspycmd`) to refresh the decompiled source. The Roslyn index automatically rebuilds on the next query.
2. **Godot assets** — run `recover_game_project` to re-extract scenes, textures, resources, and GDScript from the updated PCK.

## Documentation

Detailed reference material is in the [`docs/`](docs/) directory:

- **[Tool Reference](docs/tools-reference.md)** — all 151 tools organized by category
- **[Complex Workflows](docs/workflows.md)** — multi-step project editing, bridge automation, example sequences
- **[Advanced Generators](docs/advanced-generators.md)** — community-inspired generators and scaffold tools
- **[Modding Guides](docs/modding-guides.md)** — all 29 built-in guide topics
- **[Project Structure](docs/project-structure.md)** — repository layout, generated mod structure, BaseLib integration
- **[Detailed Setup](docs/detailed-setup.md)** — manual decompilation, GDRE Tools, path configuration, scoped configs

## License

MIT
