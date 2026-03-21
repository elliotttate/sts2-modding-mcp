# BaseLib: CustomCharacterModel

Full pipeline for creating new playable characters. Override:

**Visual Assets:**
- `VisualsPath` - Character .tscn scene
- `SelectScreenBgPath` - Character select background
- `EnergyCounterPath` - Energy counter .tscn
- Trail settings, icon paths

**Animation:**
- `SetupAnimator()` - Custom animation configuration
- Attack/Cast/Death animation name overrides

**Audio:**
- `AttackSfx`, `CastSfx`, `DeathSfx` paths

**Character Select UI:**
- `CharSelectInfoPath` - Info panel scene

**Gameplay:**
- `StartingMaxHp`, `StartingGold`, `OrbSlots`
- `StarterDeck()` - Returns initial card list
- `StarterRelics()` - Returns initial relic list
- `CardPoolModel` - Must return your CustomCardPoolModel

**Pool Models (create alongside character):**
- `CustomCardPoolModel` - Card pool with custom frames, materials, shader colors
- `CustomRelicPoolModel` - Relic pool
- `CustomPotionPoolModel` - Potion pool
- All support `ICustomEnergyIconPool` for custom energy icons

Register with `[Pool(typeof(YourCharacterCardPool))]` on cards.
