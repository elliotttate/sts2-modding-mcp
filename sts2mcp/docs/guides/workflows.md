# Common Modding Workflows

Step-by-step tool chains for common goals. Each section shows which tools to use and in what order.

## Create a new mod from scratch

```
1. create_mod_project        → Scaffold project structure
2. generate_card / relic / etc  → Generate entity code + localization
3. Write the generated files into the project
4. validate_mod              → Catch manifest, csproj, localization issues
5. build_mod                 → Compile C# code
6. build_pck                 → Pack images/scenes/localization into .pck
7. install_mod               → Deploy to game's mods/ folder
8. launch_game               → Test in-game
```

## Add content to an existing mod

```
1. Read the mod's .csproj and mod_manifest.json to understand namespace/layout
2. generate_card / relic / etc  → Produce code + localization
3. Write source files into the project's Code/ directory
4. Merge localization entries into existing JSON files
5. validate_mod              → Check for issues
6. build_mod + build_pck     → Rebuild
7. install_mod               → Redeploy
```

## Create a card with AI-generated art

```
1. generate_card             → Card class + localization entries
2. generate_art(             → AI-generate card art with all size variants
     description="...",
     asset_type="card",
     name="my_card",
     project_dir="..."
   )
3. build_pck                 → Pack images + localization into .pck
4. build_mod                 → Compile the C# code
5. install_mod               → Deploy
```

## Understand a game mechanic before modding it

```
1. search_game_code "keyword"       → Find relevant classes
2. get_entity_source "ClassName"    → Read the full source
3. get_entity_relationships "Name"  → See what it interacts with
4. analyze_method_callers "Class" "Method" → Trace the call graph
5. list_hooks category="modify"     → Find hooks you can use
```

## Figure out what to Harmony patch

```
1. suggest_patches "desired behavior change"  → Get patch target suggestions
2. get_entity_source "TargetClass"            → Read the method you'd patch
3. analyze_method_callers "Class" "Method"    → Check side effects
4. generate_harmony_patch                     → Generate the patch code
```

## Create a custom keyword mechanic (like Poison or Mantra)

```
1. generate_mechanic          → Power + sample card + sample relic + localization
2. generate_custom_tooltip    → Hover tooltip for the keyword
3. generate_custom_keyword    → CardKeyword enum entry (BaseLib)
4. Write files into the project
5. Add more cards that use the keyword
```

## Create a custom run modifier

```
1. generate_modifier          → Modifier class + registration patch + loc patch
2. build_mod + install_mod    → Deploy
3. Start a custom run and select your modifier
```

## Create a custom playable character

```
1. generate_character               → Character class + pool models
2. scaffold_character_assets        → Scene files + image checklist
3. get_character_asset_paths        → Reference all required res:// paths
4. Generate starter cards and relics with the character's pool
5. build_pck                        → Pack assets into .pck
6. build_mod + install_mod          → Deploy both .dll and .pck
```

## Create a custom monster + encounter

```
1. generate_monster            → Monster class + .tscn scene
2. generate_encounter          → Encounter that spawns your monster
3. generate_create_visuals_patch → Required patch for static-image monsters
4. generate_act_encounter_patch  → Patch to add encounter to an act
```

## Automated playtest loop (requires bridge)

```
1. bridge_ping                    → Verify bridge is up
2. bridge_start_run               → Start a seeded run
3. bridge_get_screen              → Check current screen
4. bridge_get_available_actions   → See what's possible
5. bridge_play_card / bridge_end_turn → Take actions
6. bridge_get_combat_state        → Observe results
```

## Debug a build failure

```
1. build_mod                      → Attempt build
2. analyze_build_output           → Parse errors structurally
3. get_entity_source "ClassName"  → Check API signatures
4. search_hooks_by_signature "ParamType" → Find correct hook signatures
5. get_hook_signature "HookName"  → Get exact override stub
```

## Validate a mod before shipping

```
1. validate_mod                   → Check manifest, csproj, entry point, localization
2. check_mod_compatibility        → Verify API references still exist in current game
3. build_mod                      → Ensure it compiles
4. Test in-game via bridge or manually
```

## Prepare for a game update

```
1. decompile_game                  → Re-decompile sts2.dll
2. diff_game_versions old/ new/    → Find changed hooks, methods, files
3. check_mod_compatibility         → See if your mod is affected
4. Fix any broken references
```

## Explore game assets (scenes, textures, scripts)

```
1. list_game_assets              → See all 15K+ files with extension breakdown
2. search_game_assets "query"    → Find assets by name
3. extract_game_assets           → Pull out specific files
4. convert_resource              → Convert binary .scn to readable .tscn
```
