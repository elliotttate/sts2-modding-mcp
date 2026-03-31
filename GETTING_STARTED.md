# Getting Started with STS2 Modding MCP

This guide walks you through setting up the STS2 Modding MCP from scratch. By the end, you'll have an AI assistant connected to the game that can generate mods, build them, deploy them, and playtest them for you.

## What Is This?

The STS2 Modding MCP is a [Model Context Protocol](https://modelcontextprotocol.io/) server that connects any MCP-compatible AI assistant to Slay the Spire 2. It gives the AI **151 tools** to:

- **Reverse-engineer** the game's C# source code and Godot assets
- **Generate** complete mod code — cards, relics, powers, potions, monsters, characters, and more
- **Build and deploy** mods to the game with one command
- **Inspect** the running game's scene tree in real time
- **Playtest** mods by controlling the game — starting runs, playing cards, navigating maps, and verifying behavior

You don't need to be a programmer to use it. The AI handles the code — you describe what you want.

---

## Prerequisites

| Requirement | Why | How to Get It |
|-------------|-----|---------------|
| **Slay the Spire 2** | The game itself | [Steam](https://store.steampowered.com/app/2868840/Slay_the_Spire_2/) |
| **Python 3.11+** | Runs the MCP server | [python.org/downloads](https://www.python.org/downloads/) — check "Add to PATH" during install |
| **.NET 9.0 SDK** | Builds C# mods | [dotnet.microsoft.com/download](https://dotnet.microsoft.com/en-us/download/dotnet/9.0) |
| **An MCP-compatible AI client** | Talks to the MCP | See [Step 3](#step-3-connect-an-ai-client) for options |

Optional but recommended:
- **GDRE Tools** — for extracting Godot assets from the game PCK ([github.com/GDRETools/gdsdecomp/releases](https://github.com/GDRETools/gdsdecomp/releases))

---

## Step 1: Install the MCP Server

### Option A: Clone from GitHub (recommended)

```bash
git clone https://github.com/elliotttate/sts2-modding-mcp.git
cd sts2-modding-mcp
pip install -e .
```

### Option B: From a downloaded zip

1. Extract the zip to a folder (e.g., `C:\sts2-modding-mcp`)
2. Open a terminal in that folder
3. Run:

```bash
pip install -e .
```

### Verify the install

```bash
python run.py --help
```

You should see usage info. If Python isn't found, make sure it's on your PATH.

---

## Step 2: First-Time Setup

Run the setup command to configure game paths and decompile the game source:

```bash
python run.py
```

On first launch, the server will:

1. **Detect your game install** — it checks the default Steam path. If your game is elsewhere, set the `STS2_GAME_DIR` environment variable:
   ```bash
   # Windows (PowerShell)
   $env:STS2_GAME_DIR = "D:\Steam\steamapps\common\Slay the Spire 2"

   # Linux/Mac
   export STS2_GAME_DIR="/path/to/Slay the Spire 2"
   ```

2. **Decompile the game assemblies** — extracts C# source from `sts2.dll` into the `decompiled/` folder using ilspycmd. This takes a minute or two the first time.

3. **Build and install the bridge mods** — the `test_mod` (playtesting bridge) and `explorer_mod` (scene inspector) are automatically compiled and deployed to the game's `mods/` folder.

4. **Index game entities** — catalogs 3,048+ cards, relics, powers, potions, monsters, and other entities.

---

## Step 3: Connect an AI Client

The MCP server needs an AI client to talk to. Here are the main options:

### Claude Code (CLI) — Recommended

[Claude Code](https://docs.anthropic.com/en/docs/claude-code) is Anthropic's CLI tool. Install it:

```bash
npm install -g @anthropic-ai/claude-code
```

Then add this MCP server to your project. Create or edit `.mcp.json` in the project root:

```json
{
  "mcpServers": {
    "sts2-modding": {
      "command": "python",
      "args": ["run.py"],
      "cwd": "/path/to/sts2-modding-mcp"
    }
  }
}
```

Now run `claude` from the project directory and you'll have access to all 151 tools.

### Claude Desktop

1. Open Claude Desktop settings
2. Go to **Developer** > **MCP Servers**
3. Add a new server with this config:

```json
{
  "sts2-modding": {
    "command": "python",
    "args": ["run.py"],
    "cwd": "/path/to/sts2-modding-mcp"
  }
}
```

4. Restart Claude Desktop

### Cursor / Windsurf / Other MCP Clients

Most MCP clients use a similar configuration format. Point them at `python run.py` in the MCP server directory. Check your client's docs for the exact config file location.

---

## Step 4: Make Your First Mod

Once connected, try asking the AI:

> "Create a new mod called 'MyFirstMod' that adds a card called 'Power Surge' — a 1-cost common Ironclad attack that deals 8 damage and draws 1 card."

The AI will:
1. Scaffold a mod project with `create_mod_project`
2. Generate the card code with `generate_card`
3. Build it with `build_mod`
4. Install it with `install_mod`

You can then launch the game and find your card in the game.

### More things to try

- **"Add a relic that gives 1 strength at the start of each combat"**
- **"Create a potion that applies 5 vulnerable to all enemies"**
- **"Make a power that draws an extra card each turn"**
- **"Generate a custom character with their own card pool"**
- **"Explain how the damage system works in STS2"**

---

## Step 5: Playtest with the Bridge (Optional)

The bridge lets the AI control the running game. To use it:

1. **Launch the game** — you can ask the AI: *"launch the game"*
2. **Wait for the bridge** — ask: *"ping the bridge"* to verify the connection
3. **Start a test run** — ask: *"start a run with Ironclad and test my mod"*

The AI can then play cards, navigate maps, make event choices, and verify that your mod works correctly — all without you touching the game.

---

## Step 6: Live Scene Inspection (Optional)

The GodotExplorer mod lets the AI see the game's visual scene tree in real time. This is useful for:

- Understanding how the game's UI is structured
- Building custom UI overlays
- Debugging visual issues with your mod

Ask: *"Show me the scene tree"* or *"Find all nodes related to the player's hand"*

---

## Troubleshooting

### "Python not found" / "pip not found"

Make sure Python 3.11+ is installed and on your PATH. On Windows, re-run the Python installer and check **"Add Python to PATH"**.

### "dotnet not found"

Install the [.NET 9.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0). After installing, restart your terminal.

### Bridge won't connect

- Make sure the game is running
- Check that `MCPTest` appears in the game's mod list (main menu > Mods)
- The bridge runs on TCP port 21337 — make sure nothing else is using it
- Try asking the AI: *"check bridge diagnostics"*

### Game can't find mods

The mods folder should be at: `<game install>/mods/`. The MCP server creates this automatically. If mods don't appear in-game, verify the game path is correct with: *"show game info"*

### Build errors

Ask the AI to *"analyze the build output"* — it parses compiler errors into structured diagnostics and can usually fix them automatically.

---

## Useful Commands to Ask the AI

| What to ask | What it does |
|-------------|--------------|
| *"Show me all card entities"* | Lists all 500+ cards in the game |
| *"Show the source code for Strike"* | Displays the decompiled C# for a specific card |
| *"What hooks are available for damage?"* | Lists hooks for modifying damage behavior |
| *"Create a mod project called X"* | Scaffolds a full mod project |
| *"Build and deploy my mod"* | Compiles and installs to the game |
| *"Start a run and test my mod"* | Launches automated playtest |
| *"What's the getting started guide?"* | Shows the built-in modding guide |
| *"Explain how combat works"* | Deep dive into the combat system |

---

## Project Structure (For the Curious)

```
sts2-modding-mcp/
  run.py              — Entry point: starts the MCP server
  sts2mcp/
    server.py         — Tool definitions and request handling
    mod_gen.py        — Code generators (cards, relics, powers, etc.)
    game_data.py      — Game source indexer
    analysis.py       — Code intelligence (hooks, patches, call graphs)
    bridge_client.py  — TCP client to the in-game bridge mod
    templates/        — 43 C# code templates
    docs/guides/      — 29 modding guide topics
    docs/baselib/     — 15 BaseLib reference docs
  test_mod/           — Bridge mod (runs inside the game, port 21337)
  explorer_mod/       — Scene inspector mod (runs inside the game, port 27020)
  tools/              — Roslyn analyzer for deep C# parsing
```

---

## Getting Help

- **In-app:** Ask the AI *"get modding guide for [topic]"* — there are 29 built-in topics
- **GitHub Issues:** [github.com/elliotttate/sts2-modding-mcp/issues](https://github.com/elliotttate/sts2-modding-mcp/issues)
- **STS2 Modding Discord:** Community support for Slay the Spire 2 modding

---

## Next Steps

Once you're comfortable with basic mods, explore:

- **Custom characters** with unique card pools, relics, and mechanics
- **Harmony patches** to modify existing game behavior
- **Godot UI** for in-game panels, overlays, and visual effects
- **Multiplayer networking** with custom net messages
- **Advanced debugging** with breakpoints and state snapshots

Ask the AI for the modding guide on any of these topics — it has detailed walkthroughs for each.
