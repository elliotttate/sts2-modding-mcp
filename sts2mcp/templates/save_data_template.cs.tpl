using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace {namespace};

/// <summary>
/// Persistent save data for the mod. Call Load() in ModEntry.Init() and Save() when data changes.
/// Data is stored per-save-slot at: %APPDATA%/.sts2mods/{mod_id}/save_data.json
/// </summary>
public static class {class_name}
{{
    private static readonly string SaveDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ".sts2mods", "{mod_id}");
    private static readonly string SavePath = Path.Combine(SaveDir, "save_data.json");

{fields}

    public static void Load()
    {{
        try
        {{
            if (File.Exists(SavePath))
            {{
                var json = File.ReadAllText(SavePath);
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (data != null)
                {{
{load_body}
                }}
            }}
            Log.Info("[{mod_id}] Save data loaded.");
        }}
        catch (Exception ex)
        {{
            Log.Warn($"[{mod_id}] Failed to load save data: {{ex.Message}}");
        }}
    }}

    public static void Save()
    {{
        try
        {{
            Directory.CreateDirectory(SaveDir);
            var data = new Dictionary<string, object>
            {{
{save_body}
            }};
            File.WriteAllText(SavePath, JsonSerializer.Serialize(data, new JsonSerializerOptions {{ WriteIndented = true }}));
        }}
        catch (Exception ex)
        {{
            Log.Warn($"[{mod_id}] Failed to save data: {{ex.Message}}");
        }}
    }}

    public static void Reset()
    {{
{reset_body}
    }}
}}
