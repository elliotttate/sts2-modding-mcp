# Game Log Parsing

## Log Location
The game writes to Godot's standard log:
- Windows: `%APPDATA%/Godot/app_userdata/Slay the Spire 2/logs/godot.log`
- The log is recreated each game launch

## Log Format
```
[INFO] Timestamp - Message
[DEBUG] Timestamp - Message
[WARN] Timestamp - Message
[ERROR] Timestamp - Message
```

## Common Log Patterns (Regex)
```python
# Card obtained
r"\[INFO\] Obtained (CARD\.\w+) from card reward"

# Potion used
r"\[INFO\] Player \d+ using potion (\w+)"

# Combat start
r"\[INFO\] Creating NCombatRoom with mode=ActiveCombat encounter=(\w+)"

# Combat end
r"\[INFO\] (CHARACTER\.\w+) has won against encounter (ENCOUNTER\.\w+)"

# Room entered
r"\[INFO\] Entering room: (\w+)"

# Character selected
r"Received LobbyPlayerChangedCharacterMessage for \d+ (CHARACTER\.\w+)"
```

## In-Code Logging
```csharp
using MegaCrit.Sts2.Core.Logging;

Log.Info("Normal message");       // [INFO] level
Log.Warn("Warning message");      // [WARN] level, yellow in console
Log.Error("Error message");       // [ERROR] level, red
Log.Debug("Debug message");       // [DEBUG] level, hidden by default

// Set log level via console: log Generic DEBUG
```

## File Logging for Mods
```csharp
var logPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    ".sts2mods", "mymod", "debug.log");
File.AppendAllText(logPath, $"[{DateTime.Now}] {message}\n");
```
