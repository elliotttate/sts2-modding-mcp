# STS2 Save File Format

## File Locations
- Windows: `%APPDATA%/Godot/app_userdata/Slay the Spire 2/`
- Linux: `~/.local/share/godot/app_userdata/Slay the Spire 2/`
- macOS: `~/Library/Application Support/Godot/app_userdata/Slay the Spire 2/`

## Key Files
- `current_run.save` - Active singleplayer run state
- `current_run_mp.save` - Active multiplayer run state
- `progress.save` - Unlocks, stats, discovered entities, epoch progress
- `settings.save` - Game settings (includes `full_console` flag)
- `history/*.run` - Completed run history (floor-by-floor)

## Save Format: JSON
All save files are JSON. Example current_run.save structure:
```json
{
  "run_state": {
    "act_index": 0,
    "floor": 5,
    "seed": "ABC123",
    "ascension_level": 0
  },
  "players": [{
    "character_id": "CHARACTER.IRONCLAD",
    "current_hp": 65,
    "max_hp": 80,
    "gold": 150,
    "deck": ["CARD.STRIKE", "CARD.DEFEND", ...],
    "relics": ["RELIC.BURNING_BLOOD"],
    "potions": []
  }]
}
```

## Custom Mod Save Data
Store mod data at: `%APPDATA%/.sts2mods/{mod_id}/`
Use `System.Text.Json` for serialization:
```csharp
var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(savePath, json);
```

## Godot User Data Path
```csharp
// In-game Godot path:
var path = OS.GetUserDataDir();  // user:// in Godot
// Or via .NET:
var path = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
```
