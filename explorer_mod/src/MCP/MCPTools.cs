using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Godot;
using GodotExplorer.Core;

namespace GodotExplorer.MCP;

/// <summary>
/// MCP tool implementations for Godot scene inspection and manipulation.
/// Each tool runs on the main thread via MainThreadDispatcher.
/// </summary>
public static class MCPTools
{
    public static List<MCPToolInfo> GetToolList() => new()
    {
        Tool("get_scene_tree", "Get the scene tree hierarchy as JSON. Returns node names, types, paths, and child counts.",
            Props(("depth", "integer", "Max depth to traverse (default 3)"),
                  ("root_path", "string", "Root node path to start from (default /root)"))),

        Tool("find_nodes", "Find nodes by name pattern or type. Returns matching node paths.",
            Props(("pattern", "string", "Name pattern to search (supports * wildcards)"),
                  ("type", "string", "Class type to filter by (e.g. Control, Sprite2D, NCard)"),
                  ("limit", "integer", "Max results (default 50)")),
            required: new[] { "pattern" }),

        Tool("inspect_node", "Get detailed info about a node: all properties, class, children, signals.",
            Props(("path", "string", "Node path (e.g. /root/Game)")),
            required: new[] { "path" }),

        Tool("get_property", "Get a specific property value from a node.",
            Props(("path", "string", "Node path"), ("property", "string", "Property name")),
            required: new[] { "path", "property" }),

        Tool("set_property", "Set a property value on a node.",
            Props(("path", "string", "Node path"), ("property", "string", "Property name"),
                  ("value", "string", "Value to set (auto-parsed to correct type)")),
            required: new[] { "path", "property", "value" }),

        Tool("call_method", "Call a method on a node.",
            Props(("path", "string", "Node path"), ("method", "string", "Method name"),
                  ("args", "string", "Comma-separated arguments (optional)")),
            required: new[] { "path", "method" }),

        Tool("toggle_visibility", "Toggle visibility of a CanvasItem node.",
            Props(("path", "string", "Node path")),
            required: new[] { "path" }),

        Tool("get_node_count", "Get total node count in the scene tree.", Props()),

        Tool("list_groups", "List all nodes in a group, or list all groups if no group specified.",
            Props(("group", "string", "Group name (optional)"))),

        Tool("get_game_info", "Get game engine info: Godot version, window size, FPS, node count.",
            Props()),

        Tool("list_assemblies", "List all loaded .NET assemblies with type counts.",
            Props()),

        Tool("search_types", "Search for types across loaded assemblies.",
            Props(("query", "string", "Type name to search for (partial match)")),
            required: new[] { "query" }),

        Tool("inspect_type", "Get detailed info about a .NET type: methods, properties, fields.",
            Props(("type_name", "string", "Fully qualified type name")),
            required: new[] { "type_name" }),

        Tool("tween_property", "Animate a node property over time using a Tween. Can loop.",
            Props(("path", "string", "Node path"),
                  ("property", "string", "Property name to animate (e.g. rotation, modulate, position)"),
                  ("from", "string", "Start value"),
                  ("to", "string", "End value"),
                  ("duration", "string", "Duration in seconds (default 1.0)"),
                  ("loops", "integer", "Number of loops, 0 = infinite (default 0)"),
                  ("trans", "string", "Transition type: linear, sine, quad, cubic, back, bounce, elastic (default linear)")),
            required: new[] { "path", "property", "to" }),
    };

    public static MCPToolResult ExecuteTool(string name, JsonElement? args)
    {
        try
        {
            return name switch
            {
                "get_scene_tree" => MainThreadDispatcher.RunOnMainThread(() => GetSceneTree(args)),
                "find_nodes" => MainThreadDispatcher.RunOnMainThread(() => FindNodes(args)),
                "inspect_node" => MainThreadDispatcher.RunOnMainThread(() => InspectNode(args)),
                "get_property" => MainThreadDispatcher.RunOnMainThread(() => GetProperty(args)),
                "set_property" => MainThreadDispatcher.RunOnMainThread(() => SetProperty(args)),
                "call_method" => MainThreadDispatcher.RunOnMainThread(() => CallMethod(args)),
                "toggle_visibility" => MainThreadDispatcher.RunOnMainThread(() => ToggleVisibility(args)),
                "get_node_count" => MainThreadDispatcher.RunOnMainThread(() => GetNodeCount()),
                "list_groups" => MainThreadDispatcher.RunOnMainThread(() => ListGroups(args)),
                "get_game_info" => MainThreadDispatcher.RunOnMainThread(() => GetGameInfo()),
                "list_assemblies" => ListAssemblies(),
                "search_types" => SearchTypes(args),
                "inspect_type" => InspectType(args),
                "tween_property" => MainThreadDispatcher.RunOnMainThread(() => TweenProperty(args)),
                _ => MCPHelpers.ErrorResult($"Unknown tool: {name}")
            };
        }
        catch (Exception ex)
        {
            return MCPHelpers.ErrorResult($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // ==================== Tool Implementations ====================

    private static MCPToolResult GetSceneTree(JsonElement? args)
    {
        int depth = GetInt(args, "depth", 3);
        string rootPath = GetString(args, "root_path", "/root");

        var root = ExplorerCore.SceneTree?.Root?.GetNodeOrNull(rootPath);
        if (root == null) return MCPHelpers.ErrorResult($"Node not found: {rootPath}");

        var tree = BuildTreeJson(root, 0, depth);
        return MCPHelpers.TextResult(JsonSerializer.Serialize(tree, MCPHelpers.JsonOptions));
    }

    private static Dictionary<string, object?> BuildTreeJson(Node node, int currentDepth, int maxDepth)
    {
        var result = new Dictionary<string, object?>
        {
            ["name"] = node.Name.ToString(),
            ["class"] = node.GetClass(),
            ["path"] = node.GetPath().ToString(),
            ["child_count"] = node.GetChildCount()
        };

        if (node is CanvasItem ci)
            result["visible"] = ci.IsVisibleInTree();
        if (node is Control ctrl)
        {
            result["size"] = $"{ctrl.Size.X:F0}x{ctrl.Size.Y:F0}";
            result["position"] = $"{ctrl.Position.X:F0},{ctrl.Position.Y:F0}";
        }

        if (currentDepth < maxDepth)
        {
            var children = new List<Dictionary<string, object?>>();
            for (int i = 0; i < node.GetChildCount(); i++)
            {
                var child = node.GetChild(i);
                if (child.Name.ToString().StartsWith("GodotExplorer")) continue;
                children.Add(BuildTreeJson(child, currentDepth + 1, maxDepth));
            }
            if (children.Count > 0)
                result["children"] = children;
        }

        return result;
    }

    private static MCPToolResult FindNodes(JsonElement? args)
    {
        string pattern = GetString(args, "pattern", "*");
        string type = GetString(args, "type", "");
        int limit = GetInt(args, "limit", 50);

        var root = ExplorerCore.SceneTree?.Root;
        if (root == null) return MCPHelpers.ErrorResult("No scene tree");

        Godot.Collections.Array<Node> results;
        if (!string.IsNullOrEmpty(type))
            results = root.FindChildren(pattern, type, true, false);
        else
            results = root.FindChildren(pattern, "", true, false);

        var items = results
            .Where(n => !n.Name.ToString().StartsWith("GodotExplorer"))
            .Take(limit)
            .Select(n => new { name = n.Name.ToString(), type = n.GetClass(), path = n.GetPath().ToString() })
            .ToList();

        return MCPHelpers.TextResult(JsonSerializer.Serialize(new { count = results.Count, results = items }, MCPHelpers.JsonOptions));
    }

    private static MCPToolResult InspectNode(JsonElement? args)
    {
        string path = GetString(args, "path", "");
        var node = ExplorerCore.SceneTree?.Root?.GetNodeOrNull(path);
        if (node == null) return MCPHelpers.ErrorResult($"Node not found: {path}");

        var properties = PropertyHelper.GetProperties(node);
        var propList = new List<object>();
        foreach (var prop in properties)
        {
            if (PropertyHelper.IsMarker(prop)) continue;
            var value = PropertyHelper.ReadValue(node, prop.Name);
            propList.Add(new
            {
                name = prop.Name,
                type = PropertyHelper.TypeName(prop.VariantType),
                value = value.Obj?.ToString() ?? "(null)",
                category = prop.Category,
                readOnly = prop.IsReadOnly
            });
        }

        var children = new List<object>();
        for (int i = 0; i < node.GetChildCount(); i++)
        {
            var child = node.GetChild(i);
            children.Add(new { name = child.Name.ToString(), type = child.GetClass() });
        }

        var info = new
        {
            name = node.Name.ToString(),
            className = node.GetClass(),
            path = node.GetPath().ToString(),
            childCount = node.GetChildCount(),
            properties = propList,
            children
        };

        return MCPHelpers.TextResult(JsonSerializer.Serialize(info, MCPHelpers.JsonOptions));
    }

    private static MCPToolResult GetProperty(JsonElement? args)
    {
        string path = GetString(args, "path", "");
        string prop = GetString(args, "property", "");
        var node = ExplorerCore.SceneTree?.Root?.GetNodeOrNull(path);
        if (node == null) return MCPHelpers.ErrorResult($"Node not found: {path}");

        var value = PropertyHelper.ReadValue(node, prop);
        return MCPHelpers.TextResult($"{prop} = {value}");
    }

    private static MCPToolResult SetProperty(JsonElement? args)
    {
        string path = GetString(args, "path", "");
        string prop = GetString(args, "property", "");
        string valueStr = GetString(args, "value", "");
        var node = ExplorerCore.SceneTree?.Root?.GetNodeOrNull(path);
        if (node == null) return MCPHelpers.ErrorResult($"Node not found: {path}");

        Variant value;
        if (bool.TryParse(valueStr, out bool bv)) value = bv;
        else if (int.TryParse(valueStr, out int iv)) value = iv;
        else if (float.TryParse(valueStr, out float fv)) value = fv;
        else value = valueStr;

        if (PropertyHelper.WriteValue(node, prop, value))
            return MCPHelpers.TextResult($"Set {prop} = {value}");
        return MCPHelpers.ErrorResult($"Failed to set {prop}");
    }

    private static MCPToolResult CallMethod(JsonElement? args)
    {
        string path = GetString(args, "path", "");
        string method = GetString(args, "method", "");
        var node = ExplorerCore.SceneTree?.Root?.GetNodeOrNull(path);
        if (node == null) return MCPHelpers.ErrorResult($"Node not found: {path}");

        var result = node.Call(method);
        return MCPHelpers.TextResult($"{method}() = {result}");
    }

    private static MCPToolResult ToggleVisibility(JsonElement? args)
    {
        string path = GetString(args, "path", "");
        var node = ExplorerCore.SceneTree?.Root?.GetNodeOrNull(path);
        if (node == null) return MCPHelpers.ErrorResult($"Node not found: {path}");
        if (!node.HasMethod("set_visible")) return MCPHelpers.ErrorResult("Node is not a CanvasItem");

        bool current = node.Call("is_visible").AsBool();
        node.Call("set_visible", !current);
        return MCPHelpers.TextResult($"Visibility: {current} -> {!current}");
    }

    private static MCPToolResult GetNodeCount()
    {
        int count = ExplorerCore.SceneTree?.GetNodeCount() ?? 0;
        return MCPHelpers.TextResult($"Total nodes: {count}");
    }

    private static MCPToolResult ListGroups(JsonElement? args)
    {
        string group = GetString(args, "group", "");
        if (string.IsNullOrEmpty(group))
            return MCPHelpers.TextResult("Specify a group name to list nodes in that group.");

        var nodes = ExplorerCore.SceneTree?.GetNodesInGroup(group);
        if (nodes == null || nodes.Count == 0)
            return MCPHelpers.TextResult($"No nodes in group '{group}'.");

        var items = nodes.Select(n => new { name = n.Name.ToString(), path = n.GetPath().ToString() }).ToList();
        return MCPHelpers.TextResult(JsonSerializer.Serialize(items, MCPHelpers.JsonOptions));
    }

    private static MCPToolResult GetGameInfo()
    {
        var info = new
        {
            engine = Engine.GetVersionInfo()["string"].AsString(),
            fps = Engine.GetFramesPerSecond(),
            nodeCount = ExplorerCore.SceneTree?.GetNodeCount() ?? 0,
            windowSize = $"{DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y}",
            processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName
        };
        return MCPHelpers.TextResult(JsonSerializer.Serialize(info, MCPHelpers.JsonOptions));
    }

    private static MCPToolResult ListAssemblies()
    {
        var asms = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .Select(a => new { name = a.GetName().Name, version = a.GetName().Version?.ToString(), types = a.GetTypes().Length })
            .OrderBy(a => a.name)
            .ToList();
        return MCPHelpers.TextResult(JsonSerializer.Serialize(asms, MCPHelpers.JsonOptions));
    }

    private static MCPToolResult SearchTypes(JsonElement? args)
    {
        string query = GetString(args, "query", "").ToLowerInvariant();
        if (string.IsNullOrEmpty(query)) return MCPHelpers.ErrorResult("Provide a search query");

        var results = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
            .Where(t => t.FullName?.ToLowerInvariant().Contains(query) == true)
            .Take(50)
            .Select(t => new { name = t.FullName, assembly = t.Assembly.GetName().Name, isPublic = t.IsPublic })
            .ToList();

        return MCPHelpers.TextResult(JsonSerializer.Serialize(new { count = results.Count, types = results }, MCPHelpers.JsonOptions));
    }

    private static MCPToolResult InspectType(JsonElement? args)
    {
        string typeName = GetString(args, "type_name", "");
        Type? type = Type.GetType(typeName);
        if (type == null)
        {
            type = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.FullName == typeName || t.Name == typeName);
        }
        if (type == null) return MCPHelpers.ErrorResult($"Type not found: {typeName}");

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Take(30).Select(m => $"{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})").ToList();
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Take(30).Select(p => $"{p.PropertyType.Name} {p.Name} {{ {(p.CanRead ? "get; " : "")}{(p.CanWrite ? "set; " : "")}}}").ToList();

        var info = new { fullName = type.FullName, baseType = type.BaseType?.FullName, assembly = type.Assembly.GetName().Name, methods, properties = props };
        return MCPHelpers.TextResult(JsonSerializer.Serialize(info, MCPHelpers.JsonOptions));
    }

    private static MCPToolResult TweenProperty(JsonElement? args)
    {
        string path = GetString(args, "path", "");
        string property = GetString(args, "property", "");
        string toStr = GetString(args, "to", "");
        string fromStr = GetString(args, "from", "");
        float duration = float.TryParse(GetString(args, "duration", "1.0"), out float d) ? d : 1.0f;
        int loops = GetInt(args, "loops", 0);
        string transStr = GetString(args, "trans", "linear");

        var node = ExplorerCore.SceneTree?.Root?.GetNodeOrNull(path);
        if (node == null) return MCPHelpers.ErrorResult($"Node not found: {path}");

        // Parse target value based on current property type
        Variant currentValue = node.Get(property);
        Variant toValue = ParseVariant(toStr, currentValue.VariantType);
        Variant fromValue = string.IsNullOrEmpty(fromStr) ? currentValue : ParseVariant(fromStr, currentValue.VariantType);

        // Map transition name
        Tween.TransitionType trans = transStr.ToLowerInvariant() switch
        {
            "sine" => Tween.TransitionType.Sine,
            "quad" => Tween.TransitionType.Quad,
            "cubic" => Tween.TransitionType.Cubic,
            "back" => Tween.TransitionType.Back,
            "bounce" => Tween.TransitionType.Bounce,
            "elastic" => Tween.TransitionType.Elastic,
            "expo" => Tween.TransitionType.Expo,
            _ => Tween.TransitionType.Linear
        };

        // Set from value first
        node.Set(property, fromValue);

        // Create tween
        var tween = node.CreateTween();
        tween.SetTrans(trans);
        if (loops == 0)
            tween.SetLoops(); // infinite
        else
            tween.SetLoops(loops);

        tween.TweenProperty(node, property, toValue, duration);

        return MCPHelpers.TextResult($"Tweening {property} from {fromValue} to {toValue} over {duration}s ({transStr}, loops={loops})");
    }

    private static Variant ParseVariant(string str, Variant.Type typeHint)
    {
        return typeHint switch
        {
            Variant.Type.Float => float.TryParse(str, out float f) ? Variant.From(f) : Variant.From(0f),
            Variant.Type.Int => int.TryParse(str, out int i) ? Variant.From(i) : Variant.From(0),
            Variant.Type.Bool => Variant.From(bool.TryParse(str, out bool b) && b),
            Variant.Type.Vector2 => ParseVector2(str),
            Variant.Type.Color => ParseColor(str),
            _ => Variant.From(str)
        };
    }

    private static Variant ParseVector2(string str)
    {
        var parts = str.Trim('(', ')').Split(',');
        if (parts.Length == 2 && float.TryParse(parts[0].Trim(), out float x) && float.TryParse(parts[1].Trim(), out float y))
            return Variant.From(new Vector2(x, y));
        return Variant.From(Vector2.Zero);
    }

    private static Variant ParseColor(string str)
    {
        var parts = str.Trim('(', ')').Split(',');
        if (parts.Length >= 3)
        {
            float.TryParse(parts[0].Trim(), out float r);
            float.TryParse(parts[1].Trim(), out float g);
            float.TryParse(parts[2].Trim(), out float b);
            float a = parts.Length >= 4 && float.TryParse(parts[3].Trim(), out float aa) ? aa : 1f;
            return Variant.From(new Color(r, g, b, a));
        }
        return Variant.From(Colors.White);
    }

    // ==================== Helpers ====================

    private static MCPToolInfo Tool(string name, string desc, MCPToolSchema schema, string[]? required = null)
    {
        if (required != null) schema.Required = required.ToList();
        return new MCPToolInfo { Name = name, Description = desc, InputSchema = schema };
    }

    private static MCPToolSchema Props(params (string name, string type, string desc)[] properties)
    {
        var schema = new MCPToolSchema();
        foreach (var (name, type, desc) in properties)
            schema.Properties[name] = new MCPPropertySchema { Type = type, Description = desc };
        return schema;
    }

    private static string GetString(JsonElement? args, string key, string defaultValue = "")
    {
        if (args?.ValueKind == JsonValueKind.Object && args.Value.TryGetProperty(key, out var val))
            return val.GetString() ?? defaultValue;
        return defaultValue;
    }

    private static int GetInt(JsonElement? args, string key, int defaultValue = 0)
    {
        if (args?.ValueKind == JsonValueKind.Object && args.Value.TryGetProperty(key, out var val))
        {
            if (val.ValueKind == JsonValueKind.Number) return val.TryGetInt32(out int v) ? v : defaultValue;
            if (val.ValueKind == JsonValueKind.String && int.TryParse(val.GetString(), out int sv)) return sv;
        }
        return defaultValue;
    }
}
