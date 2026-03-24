using Godot;
using GodotExplorer.Core;
using System;
using System.Collections.Generic;

namespace GodotExplorer.UI;

/// <summary>
/// Debug console with two modes:
/// - Commands: built-in commands (tree, find, inspect, etc.)
/// - C#: live C# REPL using Roslyn scripting (evaluate expressions, access game types)
/// </summary>
public class ConsolePanel
{
    public VBoxContainer Root { get; }

    private RichTextLabel _logOutput;
    private LineEdit _commandInput;
    private Button _modeToggle;
    private readonly List<string> _commandHistory = new();
    private int _historyIndex = -1;
    private int _lineCount;
    private const int MaxLines = 1000;

    private bool _csharpMode;
    private CSharpEvaluator? _evaluator;

    private readonly Dictionary<string, Action<string[]>> _commands = new();

    public ConsolePanel()
    {
        Root = new VBoxContainer();
        Root.Name = "ConsolePanel";
        Root.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        Root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Root.AddThemeConstantOverride("separation", ExplorerTheme.ItemSpacing);

        // Header row
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 6);
        Root.AddChild(headerRow);

        var header = new Label();
        header.Text = "Console";
        ExplorerTheme.StyleLabel(header, ExplorerTheme.TextHeader, ExplorerTheme.FontSizeHeader);
        headerRow.AddChild(header);

        // Mode toggle button
        _modeToggle = new Button();
        _modeToggle.Text = "Mode: Commands";
        _modeToggle.TooltipText = "Switch between Commands and C# REPL";
        ExplorerTheme.StyleButton(_modeToggle);
        _modeToggle.Pressed += ToggleMode;
        headerRow.AddChild(_modeToggle);

        var spacer = new Control();
        spacer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        headerRow.AddChild(spacer);

        // Reset button (for C# state)
        var resetBtn = new Button();
        resetBtn.Text = "Reset";
        resetBtn.TooltipText = "Reset C# evaluator state (clear variables)";
        ExplorerTheme.StyleButton(resetBtn);
        resetBtn.Pressed += () => { _evaluator?.Reset(); LogSuccess("C# state reset."); };
        headerRow.AddChild(resetBtn);

        var clearBtn = new Button();
        clearBtn.Text = "Clear";
        ExplorerTheme.StyleButton(clearBtn);
        clearBtn.Pressed += ClearLog;
        headerRow.AddChild(clearBtn);

        // Log output
        _logOutput = new RichTextLabel();
        _logOutput.BbcodeEnabled = true;
        _logOutput.ScrollFollowing = true;
        _logOutput.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _logOutput.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _logOutput.SelectionEnabled = true;

        var logBg = ExplorerTheme.MakeFlatStyleBox(new Color(0.06f, 0.06f, 0.08f, 0.98f));
        logBg.SetContentMarginAll(4);
        _logOutput.AddThemeStyleboxOverride("normal", logBg);
        _logOutput.AddThemeColorOverride("default_color", ExplorerTheme.TextColor);
        _logOutput.AddThemeFontSizeOverride("normal_font_size", ExplorerTheme.FontSizeSmall);
        Root.AddChild(_logOutput);

        // Command input
        _commandInput = new LineEdit();
        _commandInput.PlaceholderText = "Type a command... (type 'help' for commands)";
        _commandInput.ClearButtonEnabled = true;
        ExplorerTheme.StyleLineEdit(_commandInput);
        _commandInput.TextSubmitted += OnInputSubmitted;
        _commandInput.GuiInput += OnInputKey;
        Root.AddChild(_commandInput);

        RegisterCommands();
        Log("[color=#5588cc]GodotExplorer Console[/color] — type [color=#66ee77]'help'[/color] for commands, or click [color=#66ee77]Mode[/color] to switch to C# REPL.");
    }

    private void ToggleMode()
    {
        _csharpMode = !_csharpMode;

        if (_csharpMode)
        {
            _modeToggle.Text = "Mode: C#";
            _commandInput.PlaceholderText = "Enter C# expression... (e.g. Tree.Root.GetChildCount())";

            // Lazy-init the evaluator
            if (_evaluator == null)
            {
                Log("[color=#5588cc]Initializing C# evaluator (first use may take a moment)...[/color]");
                _evaluator = new CSharpEvaluator();
                _evaluator.OutputReceived += (msg) => LogSuccess(msg);
                _evaluator.ErrorReceived += (msg) => LogError(msg);
                Log("[color=#5588cc]C# REPL ready![/color] Variables: [color=#66ee77]Tree[/color], [color=#66ee77]Root[/color], [color=#66ee77]Find(name)[/color], [color=#66ee77]FindAll(type)[/color], [color=#66ee77]NodeAt(path)[/color]");
                Log("[color=#aaaaaa]Examples:[/color]");
                Log("  [color=#cccccc]Tree.Root.GetChildCount()[/color]");
                Log("  [color=#cccccc]FindAll(\"Control\").Count[/color]");
                Log("  [color=#cccccc]var g = NodeAt(\"/root/Game\"); g.GetClass()[/color]");
                Log("  [color=#cccccc]Root.GetChildren().Select(c => c.Name.ToString()).ToList()[/color]");
            }
        }
        else
        {
            _modeToggle.Text = "Mode: Commands";
            _commandInput.PlaceholderText = "Type a command... (type 'help' for commands)";
        }
    }

    private void OnInputSubmitted(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        _commandInput.Clear();
        _commandHistory.Add(text);
        _historyIndex = _commandHistory.Count;

        if (_csharpMode)
        {
            SubmitCSharp(text);
        }
        else
        {
            SubmitCommand(text);
        }
    }

    private void SubmitCSharp(string code)
    {
        Log($"[color=#66ee77]cs>[/color] [color=#cccccc]{EscapeBBCode(code)}[/color]");

        if (_evaluator == null)
        {
            LogError("C# evaluator not initialized.");
            return;
        }

        // Run synchronously on the main thread (required since scripts access Godot APIs).
        // First evaluation may pause briefly for Roslyn compilation.
        string result = _evaluator.EvaluateSync(code);
        Log($"  [color=#aabbdd]{EscapeBBCode(result)}[/color]");
    }

    private void SubmitCommand(string text)
    {
        Log($"[color=#aaaaaa]> {EscapeBBCode(text)}[/color]");

        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string cmd = parts[0].ToLowerInvariant();
        string[] args = parts.Length > 1 ? parts[1..] : Array.Empty<string>();

        if (_commands.TryGetValue(cmd, out var handler))
        {
            try { handler(args); }
            catch (Exception ex) { LogError($"Error: {ex.Message}"); }
        }
        else
        {
            LogError($"Unknown command: '{cmd}'. Type 'help' for commands, or switch to C# mode.");
        }
    }

    private void OnInputKey(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed)
        {
            if (key.Keycode == Key.Up && _commandHistory.Count > 0)
            {
                _historyIndex = Math.Max(0, _historyIndex - 1);
                _commandInput.Text = _commandHistory[_historyIndex];
                _commandInput.CaretColumn = _commandInput.Text.Length;
            }
            else if (key.Keycode == Key.Down && _commandHistory.Count > 0)
            {
                _historyIndex = Math.Min(_commandHistory.Count, _historyIndex + 1);
                _commandInput.Text = _historyIndex < _commandHistory.Count
                    ? _commandHistory[_historyIndex] : "";
                _commandInput.CaretColumn = _commandInput.Text.Length;
            }
        }
    }

    private static string EscapeBBCode(string text)
    {
        // Escape BBCode brackets so user input doesn't break formatting
        return text.Replace("[", "[lb]");
    }

    // ==================== Logging ====================

    public void Log(string message)
    {
        _logOutput.AppendText(message + "\n");
        _lineCount++;
        if (_lineCount > MaxLines)
        {
            _logOutput.RemoveParagraph(0);
            _lineCount--;
        }
    }

    public void LogError(string message) => Log($"[color=#ff6666]{message}[/color]");
    public void LogWarning(string message) => Log($"[color=#ffdd55]{message}[/color]");
    public void LogSuccess(string message) => Log($"[color=#66ee77]{message}[/color]");
    private void ClearLog() { _logOutput.Clear(); _lineCount = 0; }

    // ==================== Built-in Commands ====================

    private void RegisterCommands()
    {
        _commands["help"] = _ => ShowHelp();
        _commands["tree"] = _ => PrintTree();
        _commands["inspect"] = args => InspectNode(args);
        _commands["get"] = args => GetProperty(args);
        _commands["set"] = args => SetProperty(args);
        _commands["find"] = args => FindNodes(args);
        _commands["freecam"] = _ => ToggleFreecam();
        _commands["hud"] = _ => ToggleHud();
        _commands["count"] = _ => CountNodes();
        _commands["groups"] = args => ListGroups(args);
        _commands["clear"] = _ => ClearLog();
        _commands["cs"] = _ => { if (!_csharpMode) ToggleMode(); };
    }

    private void ShowHelp()
    {
        Log("[color=#5588cc]Commands:[/color]");
        Log("  help                    — Show this help");
        Log("  tree                    — Print scene tree");
        Log("  inspect <path>          — Select a node in the inspector");
        Log("  get <path> <property>   — Get a property value");
        Log("  set <path> <prop> <val> — Set a property value");
        Log("  find <pattern>          — Find nodes by name");
        Log("  count                   — Count all nodes");
        Log("  groups [group]          — List groups or nodes in a group");
        Log("  freecam                 — Toggle free camera");
        Log("  hud                     — Toggle game HUD");
        Log("  cs                      — Switch to C# REPL mode");
        Log("  clear                   — Clear console");
    }

    private void PrintTree()
    {
        var root = ExplorerCore.SceneTree?.Root;
        if (root == null) { LogError("No scene tree."); return; }
        PrintTreeRecursive(root, 0, 200);
    }

    private int PrintTreeRecursive(Node node, int depth, int remaining)
    {
        if (remaining <= 0) { Log("  ... (truncated)"); return 0; }
        string indent = new string(' ', depth * 2);
        Log($"  {indent}{node.Name} [{node.GetClass()}]");
        remaining--;
        foreach (var child in node.GetChildren())
        {
            if (child.Name.ToString().StartsWith("GodotExplorer")) continue;
            remaining = PrintTreeRecursive(child, depth + 1, remaining);
            if (remaining <= 0) break;
        }
        return remaining;
    }

    private void InspectNode(string[] args)
    {
        if (args.Length == 0) { LogError("Usage: inspect <node_path>"); return; }
        var node = ExplorerCore.SceneTree?.Root?.GetNodeOrNull(args[0]);
        if (node == null) { LogError($"Node not found: {args[0]}"); return; }
        ExplorerCore.SelectNode(node);
        LogSuccess($"Selected: {node.Name} [{node.GetClass()}]");
    }

    private void GetProperty(string[] args)
    {
        if (args.Length < 2) { LogError("Usage: get <node_path> <property>"); return; }
        var node = ExplorerCore.SceneTree?.Root?.GetNodeOrNull(args[0]);
        if (node == null) { LogError($"Node not found: {args[0]}"); return; }
        var value = PropertyHelper.ReadValue(node, args[1]);
        Log($"  {args[1]} = {value}");
    }

    private void SetProperty(string[] args)
    {
        if (args.Length < 3) { LogError("Usage: set <node_path> <property> <value>"); return; }
        var node = ExplorerCore.SceneTree?.Root?.GetNodeOrNull(args[0]);
        if (node == null) { LogError($"Node not found: {args[0]}"); return; }
        string valueStr = string.Join(' ', args[2..]);
        Variant value;
        if (bool.TryParse(valueStr, out bool bVal)) value = bVal;
        else if (int.TryParse(valueStr, out int iVal)) value = iVal;
        else if (float.TryParse(valueStr, out float fVal)) value = fVal;
        else value = valueStr;
        if (PropertyHelper.WriteValue(node, args[1], value))
            LogSuccess($"  Set {args[1]} = {value}");
        else LogError($"  Failed to set {args[1]}");
    }

    private void FindNodes(string[] args)
    {
        if (args.Length == 0) { LogError("Usage: find <pattern>"); return; }
        var results = ExplorerCore.SceneTree?.Root?.FindChildren($"*{args[0]}*", "", true, false);
        if (results == null || results.Count == 0) { Log("  No results found."); return; }
        int count = 0;
        foreach (var node in results)
        {
            if (node.Name.ToString().StartsWith("GodotExplorer")) continue;
            Log($"  {node.GetPath()} [{node.GetClass()}]");
            count++;
            if (count >= 50) { Log($"  ... and {results.Count - 50} more"); break; }
        }
        Log($"  {results.Count} total result(s).");
    }

    private void ToggleFreecam() => Log("Use the Freecam button in the toolbar.");
    private void ToggleHud()
    {
        var root = ExplorerCore.SceneTree?.Root;
        if (root == null) return;
        int toggled = 0;
        foreach (var child in root.GetChildren())
        {
            if (child is CanvasLayer cl && !cl.Name.ToString().StartsWith("GodotExplorer"))
            { cl.Visible = !cl.Visible; toggled++; }
        }
        LogSuccess($"Toggled {toggled} CanvasLayer(s).");
    }

    private void CountNodes()
    {
        var root = ExplorerCore.SceneTree?.Root;
        if (root == null) return;
        Log($"  Total nodes: {CountRecursive(root)}");
    }

    private int CountRecursive(Node node)
    {
        int count = 1;
        foreach (var child in node.GetChildren()) count += CountRecursive(child);
        return count;
    }

    private void ListGroups(string[] args)
    {
        if (args.Length > 0)
        {
            var nodes = ExplorerCore.SceneTree?.GetNodesInGroup(args[0]);
            if (nodes == null || nodes.Count == 0) { Log($"  No nodes in group '{args[0]}'."); return; }
            foreach (var node in nodes) Log($"  {node.GetPath()} [{node.GetClass()}]");
            Log($"  {nodes.Count} node(s) in group '{args[0]}'.");
        }
        else { Log("  Usage: groups <group_name>"); }
    }
}
