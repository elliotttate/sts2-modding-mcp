# Localization System

## File Structure
Place JSON files in: `{mod_id}/localization/eng/`
- `cards.json` - Card text
- `relics.json` - Relic text
- `powers.json` - Power text
- `potions.json` - Potion text
- `monsters.json` - Monster names
- `encounters.json` - Encounter text
- `events.json` - Event text
- `orbs.json` - Orb text
- `enchantments.json` - Enchantment text
- `characters.json` - Character text
- `ancients.json` - Ancient text
- `ascension.json` - Ascension level text (if adding new levels)

## Critical: Path Case Sensitivity

**Godot's `res://` virtual filesystem is case-sensitive**, even on Windows. The game's mod loader looks for localization at:
```
res://{manifest_id}/localization/{language}/{file}.json
```
where `manifest_id` comes from the `"id"` field in `mod_manifest.json`.

The PCK `base_prefix` (set via `pck_name` in the manifest) **must exactly match** the manifest `id`. For example:

| manifest id | pck_name | PCK paths | Result |
|---|---|---|---|
| `mymod` | `mymod` | `mymod/localization/eng/cards.json` | Works |
| `mymod` | `MyMod` | `MyMod/localization/eng/cards.json` | **Broken** — files not found |

**Best practice:** Always use lowercase for both `id` and `pck_name` in `mod_manifest.json`.

## Key Format
Keys use SCREAMING_SNAKE_CASE model IDs derived from the C# class name via `StringHelper.Slugify()`:
- Insert underscore before each capital letter following a lowercase letter/digit
- Convert to uppercase
- Remove non-alphanumeric characters (except underscore)

Examples: `ShrugItOff` → `SHRUG_IT_OFF`, `GoForTheEyes` → `GO_FOR_THE_EYES`, `DefendIronclad` → `DEFEND_IRONCLAD`, `IAmInvincible` → `I_AM_INVINCIBLE`

Standard suffixes: `.title`, `.description`, `.upgrade.description`, `.flavor`

The game resolves card titles via: `new LocString("cards", Id.Entry + ".title")` where `Id.Entry` = `StringHelper.Slugify(className)`.

## Overriding Base Game Text (Total Conversion)
Since mod localization keys **overwrite** base game keys, you can rename every entity in the game by providing JSON files with matching keys. For example, to rename the Ironclad's "Bash" card:
```json
{
  "BASH.title": "Goblin Stomper"
}
```
The original description text is preserved unless you also override `.description`. This makes localization-only total conversion mods straightforward — no C# code needed for pure renames.

## Rich Text Tags
- `[gold]keyword name[/gold]` - Gold color for game keywords
- `[blue]{Value}[/blue]` - Blue for dynamic numbers
- Other colors: `[red]`, `[green]`, `[gray]`

## SmartFormat Variables
- `{Damage}` - Card damage value
- `{Block}` - Block value
- `{Amount}` - Power stack count
- `{StrengthPower}` - Power amount reference
- `{Amount:plural:card|cards}` - Pluralization
- `{count:conditional:text_if_true|text_if_false}` - Conditionals

See the `smart_format` guide for the full SmartFormat reference.

## How Mod Localization Loads

1. Game loads base localization tables from `res://localization/{lang}/*.json`
2. For each table file, the game checks every loaded mod for a matching file at `res://{mod_id}/localization/{lang}/{filename}`
3. If found, entries are **merged** into the base table via `LocTable.MergeWith()` — new keys are added, existing keys are overwritten
4. User overrides in `%APPDATA%/SlayTheSpire2/localization_override/{lang}/` are applied last

## Localization Override (Development)

For testing without rebuilding the PCK, place JSON files in:
```
%APPDATA%/SlayTheSpire2/localization_override/eng/{table_name}.json
```
These are merged into the base tables at startup. Useful for rapid iteration.

## Analyzer-Based Localization Generation

If using BaseLib with the `Alchyr.Sts2.ModAnalyzers` NuGet package, missing localization keys are detected as code warnings. In Rider or Visual Studio:

1. Hover over a class name with a missing localization warning
2. Click the class name and press **Alt+Enter**
3. Select **"Generate localization"**
4. Cut the generated JSON text and paste into the appropriate localization file (e.g., `cards.json`)

The analyzer supports cards, relics, ancients, powers, and other content types. Keep the analyzer package up-to-date to get the latest supported types.

## Languages
eng, zhs, deu, esp, fra, ita, jpn, kor, pol, ptb, rus, spa, tha, tur
