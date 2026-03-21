# Localization System

## File Structure
Place JSON files in: `{ModName}/localization/eng/`
- `cards.json` - Card text
- `relics.json` - Relic text
- `powers.json` - Power text
- `potions.json` - Potion text
- `monsters.json` - Monster names
- `encounters.json` - Encounter text

## Key Format
Keys use SCREAMING_SNAKE_CASE model IDs:
`MY_CARD.title`, `MY_CARD.description`, `MY_CARD.upgrade.description`

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

## Languages
eng, zhs, deu, esp, fra, ita, jpn, kor, pol, ptb, rus, spa, tha, tur
