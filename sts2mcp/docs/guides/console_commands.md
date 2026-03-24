# Custom Console Commands

## Overview

Mods can register custom commands in the developer console (backtick key). The game auto-discovers any class extending `AbstractConsoleCmd` in loaded mod assemblies via reflection — no manual registration needed.

## Required Namespaces

```csharp
using MegaCrit.Sts2.Core.DevConsole;                // CmdResult
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands; // AbstractConsoleCmd
using MegaCrit.Sts2.Core.Entities.Players;           // Player
```

## Minimal Example

```csharp
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace MyMod.Commands;

public class HelloCmd : AbstractConsoleCmd
{
    public override string CmdName => "hello";
    public override string Args => "[name:string]";
    public override string Description => "Say hello!";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        string name = args.Length > 0 ? string.Join(" ", args) : "world";
        return new CmdResult(true, $"Hello, {name}!");
    }
}
```

Type `hello` or `hello Claude` in the console and the message appears immediately.

## AbstractConsoleCmd Reference

### Required Overrides

| Member | Type | Purpose |
|--------|------|---------|
| `CmdName` | `string` | Console trigger word (lowercase convention) |
| `Args` | `string` | Argument description shown in `help`. Use `<required>` and `[optional]` syntax |
| `Description` | `string` | Help text shown by `help` and `help <cmd>` |
| `IsNetworked` | `bool` | `true` = synced across multiplayer via `ConsoleCmdGameAction`. `false` = local only |
| `Process(Player?, string[])` | `CmdResult` | Main execution. `args` is the input split by spaces (command name excluded) |

### Optional Overrides

| Member | Default | Purpose |
|--------|---------|---------|
| `DebugOnly` | `true` | If `true`, only available when debug commands are enabled (which any loaded mod enables automatically) |
| `GetArgumentCompletions(Player?, string[])` | Basic completion | Tab-completion support for arguments |

### Helper Methods

- `CompleteArgument(candidates, completedArgs, partialArg)` — builds tab-completion from a candidate list
- `BuildPrefix(completedArgs)` — reconstructs the command prefix for multi-arg completion
- `TryParseEnum<T>(input, out result)` — case-insensitive enum parsing

## CmdResult

```csharp
// Simple result
return new CmdResult(true, "It worked!");
return new CmdResult(false, "Something went wrong");

// Async result — the task runs after the command returns
Task task = SomeAsyncOperation();
return new CmdResult(task, true, "Started operation...");
```

The `msg` string is displayed in the console output. The optional `Task` is executed via `TaskHelper.RunSafely()` after the result is returned — use this for game state mutations that must go through the action queue.

## How Discovery Works

The `DevConsole` constructor finds commands from two sources:

1. **Built-in commands** — compile-time generated list (`AbstractConsoleCmdSubtypes.All`)
2. **Mod commands** — `ReflectionHelper.GetSubtypesInMods<AbstractConsoleCmd>()` scans all loaded mod assemblies

Each discovered type is instantiated with `Activator.CreateInstance()`, so your class **must have a public parameterless constructor** (the default is fine if you don't declare one).

Commands are stored by `CmdName` in a dictionary — if two commands share a name, the last one registered wins.

## Patterns

### Command with Validated Arguments

```csharp
public class SpawnGoldCmd : AbstractConsoleCmd
{
    public override string CmdName => "mygold";
    public override string Args => "<amount:int>";
    public override string Description => "Add gold to the player";
    public override bool IsNetworked => true;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length < 1)
            return new CmdResult(false, "Usage: mygold <amount>");

        if (!int.TryParse(args[0], out int amount))
            return new CmdResult(false, "Amount must be a number");

        if (issuingPlayer == null || !RunManager.Instance.IsInProgress)
            return new CmdResult(false, "No run in progress");

        Task task = PlayerCmd.GainGold(amount, issuingPlayer);
        return new CmdResult(task, true, $"Added {amount} gold");
    }
}
```

### Command with Tab Completion

```csharp
public class SpawnEnemyCmd : AbstractConsoleCmd
{
    public override string CmdName => "myspawn";
    public override string Args => "<enemy-id:string>";
    public override string Description => "Spawn an enemy by ID";
    public override bool IsNetworked => true;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length < 1)
            return new CmdResult(false, "Specify an enemy ID");

        // ... spawn logic ...
        return new CmdResult(true, $"Spawned {args[0]}");
    }

    public override CompletionResult GetArgumentCompletions(Player? player, string[] args)
    {
        if (args.Length <= 1)
        {
            var candidates = ModelDb.AllMonsters
                .Select(m => m.Id.Entry)
                .ToList();
            return CompleteArgument(candidates, Array.Empty<string>(), args.FirstOrDefault() ?? "");
        }
        return new CompletionResult { Type = CompletionType.Argument, ArgumentContext = CmdName };
    }
}
```

### Command That Opens a UI Window

Based on BaseLib's `showlog` command — spawns a Godot scene as a child window:

```csharp
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes;

public class OpenMyWindowCmd : AbstractConsoleCmd
{
    public override string CmdName => "mywindow";
    public override string Args => "";
    public override string Description => "Open custom window";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var instance = NGame.Instance;
        if (instance == null)
            return new CmdResult(false, "Game not ready");

        Window window = instance.GetWindow();
        window.GuiEmbedSubwindows = false;

        var scene = PreloadManager.Cache
            .GetScene("res://MyMod/scenes/MyWindow.tscn")
            .Instantiate<MyWindowNode>();
        scene.Size = DisplayServer.ScreenGetSize() * 2 / 3;
        window.AddChildSafely(scene);

        return new CmdResult(true, "Window opened");
    }
}
```

This requires:
- A `.tscn` scene file packed in your mod's PCK
- A C# script class backing the scene (marked `[GlobalClass]`)
- Calling `ScriptManagerBridge.LookupScriptsInAssembly(Assembly.GetExecutingAssembly())` during mod init so Godot discovers your scripts

### Toggle Command with State

```csharp
public class ToggleOverlayCmd : AbstractConsoleCmd
{
    private bool _active;

    public override string CmdName => "myoverlay";
    public override string Args => "";
    public override string Description => "Toggle debug overlay";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        _active = !_active;

        if (_active)
            DebugOverlay.Enable();
        else
            DebugOverlay.Disable();

        return new CmdResult(true, _active ? "Overlay ON" : "Overlay OFF");
    }
}
```

Command instances persist for the game session, so instance fields work for toggles and state tracking.

## IsNetworked: When to Use

- `true` — The command mutates game state (gold, HP, cards, combat). In multiplayer, it gets wrapped in `ConsoleCmdGameAction` and synchronized to all players via the action queue.
- `false` — The command is local-only (opening a window, toggling a debug display, logging). Safe to use without a run in progress.

If your command calls any `*Cmd` helper (like `PlayerCmd.GainGold`, `CardPileCmd.Add`, `PowerCmd.Apply`), it should be networked.

## Tips

- **Naming**: Prefix commands with your mod name to avoid collisions (`mymod_debug` not `debug`)
- **No registration needed**: Just extend `AbstractConsoleCmd` with a parameterless constructor — the game finds it automatically
- **Args format**: The `Args` string is purely descriptive for `help` output. You parse `string[] args` yourself in `Process()`
- **Async work**: Return a `CmdResult(task, success, msg)` for operations that need the game's action queue. The framework calls `TaskHelper.RunSafely()` on the task
- **Run state checks**: Always check `RunManager.Instance.IsInProgress` before accessing run/combat state
- **Error handling**: Return `new CmdResult(false, "reason")` for failures — the message shows in the console in red
