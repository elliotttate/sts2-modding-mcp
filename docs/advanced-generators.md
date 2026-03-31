# Advanced Generators

Inspired by patterns found across 21 community mods (ModConfig, BetterDrawing, Oddmelt, RMP, say-the-spire2, Archipelago, race-mod, and more), these generators cover the most common advanced modding needs.

## Community-Inspired Generators

| Tool | Description | Inspired By |
|------|-------------|-------------|
| `generate_net_message` | `INetMessage` + `IPacketSerializable` scaffold using `PacketWriter` / `PacketReader`, `Mode`, and `LogLevel` like the decompiled multiplayer messages | BetterDrawing, sts2_typing, BadApple |
| `generate_godot_ui` | Programmatic Godot UI panel with styled controls (labels, buttons, sliders, checkboxes) — no .tscn required | ModConfig, sts2_typing, 14+ mods |
| `generate_settings_panel` | Self-initializing ModConfig reflection bridge with JSON fallback and `ModEntry` patch hints | RouteSuggest, ModConfig |
| `generate_hover_tip` | HoverTip utility class for contextual tooltips on nodes or positions | sts2_typing, easyDmgCalc |
| `generate_overlay` | Auto-injected combat/map overlay with Harmony patch for scene tree injection | easyDmgCalc, sts2-agent |
| `generate_transpiler_patch` | IL bytecode Harmony transpiler for modifying method instructions directly | RMP-Mods, Oddmelt, race-mod |
| `generate_reflection_accessor` | Cached AccessTools field/property accessors with getters/setters | All 21 mods analyzed |
| `generate_custom_keyword` | [CustomEnum] CardKeyword with BaseLib, plus localization | Oddmelt (Stitch/Woven) |
| `generate_custom_pile` | [CustomEnum] PileType for custom card destinations | Oddmelt (StitchPile) |
| `generate_spire_field` | SpireField\<T, TValue\> for attaching data to game models without modification | Oddmelt |
| `generate_dynamic_var` | Custom DynamicVar subclass for card/power description variables | Oddmelt, More Bosses |

## Other Generator Scaffolds

| Tool | Description |
|------|-------------|
| `generate_event` | Event class with choice tree and handler methods |
| `generate_ancient` | BaseLib CustomAncientModel scaffold |
| `generate_orb` | Orb with passive/evoke effects (Defect mechanic) |
| `generate_enchantment` | Enchantment that attaches to and modifies cards |
| `generate_create_visuals_patch` | Harmony patch for custom monster visuals |
| `generate_act_encounter_patch` | Inject encounters into act monster pools |
| `generate_game_action` | Custom GameAction for the combat action queue |
| `generate_mechanic` | Full cross-cutting keyword mechanic (power + card + relic + localization) |
| `generate_custom_tooltip` | Localization-backed `HoverTip` helper for `ExtraHoverTips`-style usage |
| `generate_save_data` | Persistent JSON save data class |
| `generate_test_scenario` | Console command sequence for test setups |
| `generate_vfx_scene` | Godot .tscn particle effect scene |
