# Complex Mod Workflows

These tools close the gap between "generate a file" and "ship/test a real mod project".

## Project Editing and Packaging

Use these tools when an assistant needs to write multiple generated artifacts into an existing repo instead of returning loose snippets:

| Tool | When To Use It | Notes |
|------|----------------|-------|
| `inspect_mod_project` | Before touching an existing project | Reads the `.csproj` and `mod_manifest.json` to infer namespace, assembly name, resource root, PCK name, and localization layout |
| `apply_generated_output` | After any generator call that returns code/localization blobs | Writes source files into the right project folders, merges localization JSON, applies supported `project_edits`, supports `dry_run`, and rolls back on conflicts |
| `build_project_pck` | When a project owns scenes/images/resources under its manifest resource root | Builds the `.pck` using project metadata instead of requiring a manual `base_prefix` |
| `deploy_mod` | When you want one step to build, optionally pack, and install | Validates first, syncs all runtime artifacts from build output, and removes stale managed/PCK files from the target mod folder |
| `validate_mod_assets` | Before packaging a visual/content-heavy mod | Checks for broken `res://` references under the project-owned resource tree |
| `validate_mod_project` | Before deploy or before asking an assistant to continue | Runs both localization and asset validation in one pass |

## Bridge Automation Beyond Combat

The bridge is no longer limited to combat-only manipulations. These tools support deterministic playtest flows across map/event/reward/shop/rest/card-selection screens:

| Tool | What It Adds | Example Use |
|------|--------------|-------------|
| `bridge_start_run` | Seeded starts plus fixture setup for relics, cards, powers, modifiers, fights, and events | Start repeatable regression runs |
| `bridge_execute_action` | Generic screen-aware action execution | Travel on the map, take rewards, buy shop items, pick treasure, choose event options |
| `bridge_wait_for_screen` | Poll until a named screen is active and stable | Wait for `REWARD` before claiming a reward |
| `bridge_wait_until_idle` | Poll until loading/transitions settle | Synchronize scripted playtests between actions |
| `bridge_get_diagnostics` | Bundle recent logs with current bridge/runtime state | Debug why automation stalled or hit the wrong screen |
| `bridge_tail_log` | Read recent bridge log lines | Inspect the last few actions and screen transitions |
| `bridge_get_last_errors` | Filter recent failures from the bridge log | Triage action routing or state-detection problems quickly |

## Hook-Aware Generation and Build Triage

| Tool | Purpose |
|------|---------|
| `get_hook_signature` | Returns the exact hook signature plus a ready-to-paste override stub |
| `analyze_build_output` | Parses `dotnet build` stdout/stderr into structured compiler errors and warnings |

`generate_relic`, `generate_power`, and `generate_enchantment` now use hook-signature-aware fallback stubs when a trigger hook is supplied, so uncommon hooks no longer default to a bare `/* TODO: add parameters */` method.

## Extra Scaffolds for Complex Content

| Tool | Output |
|------|--------|
| `generate_ancient` | BaseLib `CustomAncientModel` scaffold with option pools and localization |
| `generate_create_visuals_patch` | Harmony patch required for static-image custom monster visuals |
| `generate_act_encounter_patch` | Encounter-pool injection patch for adding custom encounters to acts |

## Example Sequences

Update an existing project with multiple generated artifacts, validate it, then deploy it:

```text
inspect_mod_project
apply_generated_output
validate_mod_project
deploy_mod
```

Run a repeatable playtest that starts from a seed, waits for a reward screen, then claims the first reward:

```text
bridge_start_run
bridge_wait_until_idle
bridge_wait_for_screen
bridge_execute_action
```

Generate a relic using a less common hook, then explain any compiler failures structurally:

```text
get_hook_signature
generate_relic
build_mod
analyze_build_output
```

Explore the game's Godot assets to understand character scene structure before building a custom character:

```text
search_game_assets     # find "ironclad" scenes/resources
extract_game_assets    # extract the energy counter and combat scenes
convert_resource       # convert binary .scn to readable .tscn
generate_character     # generate character code with correct res:// paths
```

## Project Workflow Helpers (Library API)

For callers using `sts2mcp.mod_gen.ModGenerator` directly, the generator layer exposes project-aware helpers for multi-step workflows:

- `inspect_project(project_dir)` infers namespace, assembly name, PCK name, resource root, and localization directories from the `.csproj` plus `mod_manifest.json`
- `apply_generator_output(...)` / `apply_generator_outputs(...)` write generator results into an existing project, merge localization JSON, apply supported `project_edits`, and reject paths that escape the project root
- `build_project_pck(...)` builds a `.pck` using the project's manifest/resource layout instead of requiring the caller to supply `base_prefix` manually
- `deploy_mod(...)` builds, optionally packs, and installs a mod into the game's `mods/` folder in one step
- `validate_project_localization(...)`, `validate_project_assets(...)`, and `validate_project(...)` provide lightweight checks for JSON validity, missing localization coverage, and broken `res://` references under the project-owned resource tree

These helpers are intended for complex workflows where an assistant wants to generate several artifacts, merge them into a pre-existing project, validate the result, then package/deploy without hand-assembling paths.
