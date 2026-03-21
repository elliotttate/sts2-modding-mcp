using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;

namespace {namespace}.Config;

/// <summary>
/// Settings via optional ModConfig + JSON fallback. No hard dependency.
/// </summary>
public static class {class_name}
{{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ".sts2mods", "{mod_id}", "config.json");

    // Settings values
{settings_fields}

    public static void Initialize()
    {{
        LoadFromFile();
        TryRegisterModConfig();
    }}

    private static void TryRegisterModConfig()
    {{
        try
        {{
            var tree = NGame.Instance?.GetTree();
            if (tree == null) return;
            var apiType = Type.GetType("ModConfig.ModConfigApi, ModConfig");
            if (apiType == null) return;

            // ModConfig is available - register settings via reflection
            var register = apiType.GetMethod("RegisterMod", BindingFlags.Static | BindingFlags.Public);
            if (register == null) return;

            // Defer by 2 frames so ModConfig initializes first
            tree.ProcessFrame += () => tree.ProcessFrame += () =>
            {{
{modconfig_registration}
                Log.Info("[{mod_id}] Settings registered with ModConfig.");
            }};
        }}
        catch (Exception ex)
        {{
            Log.Warn($"[{mod_id}] ModConfig integration failed: {{ex.Message}}");
        }}
    }}

    public static void LoadFromFile()
    {{
        try
        {{
            if (!File.Exists(ConfigPath)) return;
            var json = File.ReadAllText(ConfigPath);
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
            if (data == null) return;
{load_body}
        }}
        catch (Exception ex) {{ Log.Warn($"[{mod_id}] Config load failed: {{ex.Message}}"); }}
    }}

    public static void SaveToFile()
    {{
        try
        {{
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
            var data = new Dictionary<string, object>
            {{
{save_body}
            }};
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(data, new JsonSerializerOptions {{ WriteIndented = true }}));
        }}
        catch (Exception ex) {{ Log.Warn($"[{mod_id}] Config save failed: {{ex.Message}}"); }}
    }}
}}
