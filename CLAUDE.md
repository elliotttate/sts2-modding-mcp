# CLAUDE.md — Project context for Claude Code

## What this project is

An MCP server for Slay the Spire 2 modding. It decompiles the game's C# assemblies and Godot PCK, indexes 3,048+ entities and 144 hooks, and exposes tools for querying game data, generating mod code, building/deploying, and driving in-game playtests.

## Quick reference

- **Language:** Python 3.11+ (server), C# .NET 9.0 (generated mods, bridge mod)
- **Entry point:** `run.py` → `sts2mcp/server.py:main()`
- **Tests:** `pytest tests/` — bridge tests auto-skip when game isn't running
- **Game engine:** Godot 4.5.1 C# with .NET 9.0, Harmony 2.4.2 for runtime patching

## Project structure

```
sts2mcp/
  server.py          — MCP tool definitions + handler dispatch
  mod_gen.py         — Code generators (loads templates from templates/)
  game_data.py       — Decompiled source indexer (loads Roslyn JSON index or falls back to regex)
  analysis.py        — Code intelligence (call graphs, patch suggestions, validation)
  bridge_client.py   — TCP JSON-RPC client to in-game MCPTest mod (port 21337)
  pck_builder.py     — Pure Python Godot PCK builder
  character_assets.py — Character asset scaffolding
  templates/         — 43 C# template files (*.cs.tpl) with {placeholder} format strings
  docs/guides/       — 29 modding guide markdown files (loaded by get_modding_guide)
  docs/baselib/      — 15 BaseLib reference markdown files
tools/roslyn_analyzer/ — C# Roslyn-based source analyzer (outputs roslyn_index.json)
test_mod/            — MCPTest bridge mod (C# source, runs inside the game)
tests/               — pytest suite (generators, analysis, bridge, server, integration)
decompiled/          — Decompiled C# source from sts2.dll (gitignored, ~23MB)
```

## Key conventions

- **Templates live in files, not code.** C# templates are in `sts2mcp/templates/*.cs.tpl`. Use `_load_template("name")` to load them. Guide text is in `sts2mcp/docs/guides/*.md`.
- **Generators return dicts.** Every `generate_*` method returns `{"source": str, "file_name": str, "folder": str, ...}`. Some include `"extra_files"` (list of additional dicts), `"localization"` (dict of filename → entries), and `"notes"` (list of strings).
- **Tool registration pattern.** Each tool needs: (1) a `types.Tool()` definition in `list_tools()`, (2) an `elif name == "tool_name":` handler in `_handle_tool()`, and (3) the actual implementation in a module method. Test `TestHandlerCoverage` verifies 1:1 alignment.
- **Bridge protocol.** Python sends JSON-RPC over TCP to port 21337. The C# `BridgeHandler.HandleRequest()` dispatches via a switch expression. Adding a bridge method requires changes to both `bridge_client.py` and `test_mod/Code/BridgeHandler.cs`.
- **Brace escaping in templates.** Template `.cs.tpl` files use Python `.format()` — literal C# braces must be doubled: `{{` / `}}`. Format placeholders use single braces: `{class_name}`.

## Running tests

```bash
pytest tests/                    # all tests (bridge tests skip if game not running)
pytest tests/test_generators.py  # just generator output validation
pytest tests/test_server.py      # tool registration and handler coverage
pytest tests/test_analysis.py    # code intelligence (needs decompiled/)
```

## Adding a new tool

1. If it generates C# code, create a template in `sts2mcp/templates/my_thing_template.cs.tpl`
2. Add the generator method to `ModGenerator` in `mod_gen.py` (or `CodeAnalyzer` in `analysis.py` for analysis tools)
3. Add a `types.Tool(name="my_tool", ...)` definition in `server.py:list_tools()`
4. Add an `elif name == "my_tool":` handler in `server.py:_handle_tool()`
5. Add tests in the appropriate test file
6. Run `pytest` to verify `TestHandlerCoverage` passes (it checks every tool has a handler)

## Adding a new guide topic

1. Create `sts2mcp/docs/guides/my_topic.md` with the guide content
2. Add `"my_topic"` to the enum list in the `get_modding_guide` tool definition in `server.py`
3. The file-based loader picks it up automatically — no code changes needed beyond the enum

## Environment variables

- `STS2_GAME_DIR` — Game install path (default: `E:\SteamLibrary\steamapps\common\Slay the Spire 2`)
- `STS2_DECOMPILED_DIR` — Decompiled source path (default: `./decompiled`)
- `GDRE_TOOLS_PATH` — Path to gdre_tools binary (default: `./tools/gdre_tools.exe`)

## Roslyn analyzer

The `tools/roslyn_analyzer/` directory contains a C# .NET 9.0 console app that uses `Microsoft.CodeAnalysis.CSharp` to parse all decompiled `.cs` files into Roslyn syntax trees and extract structured data: classes, methods, properties, fields, constructors, attributes, invocations, type references, enums, and inheritance chains. Output is `decompiled/roslyn_index.json` (~17MB).

```bash
# Build (one-time, requires .NET SDK)
cd tools/roslyn_analyzer && dotnet restore --configfile nuget.config && dotnet build -c Release --no-restore

# Run (regenerate after decompiling a new game version)
dotnet tools/roslyn_analyzer/bin/Release/net9.0/RoslynAnalyzer.dll decompiled decompiled/roslyn_index.json
```

When `roslyn_index.json` exists, `game_data.py` loads from it instead of regex parsing. This gives:
- Exact property extraction (from constructor args and expression-bodied members)
- Full inheritance chains (`get_class_hierarchy()`)
- Indexed call graph for O(1) caller/callee lookups
- 135 enum definitions with all members
- Override detection without grep

If the index file is absent, the system falls back to the original regex-based parsing automatically.

## Bridge mod (MCPTest)

The `test_mod/` directory contains a C# mod that runs inside the game. It opens a TCP server on port 21337 and handles JSON-RPC requests for state queries, card plays, navigation, etc. It must be built with `dotnet build` and installed to the game's `mods/` folder before bridge tools work.
