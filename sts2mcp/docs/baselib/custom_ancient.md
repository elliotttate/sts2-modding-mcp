# BaseLib: CustomAncientModel

Create ancient (legendary) events:

**Key Features:**
- `OptionPools` system - Manages 3 option slots with weighted random selection
- Automatic dialogue loading from localization
- Force-spawn control with `ForceSpawn` / `ForceSpawnConflicts`
- Map/run history icon paths

**Required Overrides:**
- `OptionPools` property - Return OptionPools<AncientOption> instance
- `GenerateOptions()` - Create the 3 options from pools
- `ProcessOption()` - Handle player's choice

**Localization Format:**
- `{ID}.intro.text` / `.sfx` - Introduction dialogue
- `{ID}.option_{N}.text` / `.sfx` - Option dialogue for each slot
