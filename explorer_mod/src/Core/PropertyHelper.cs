using Godot;
using Godot.Collections;
using System.Collections.Generic;

namespace GodotExplorer.Core;

/// <summary>
/// Property introspection utilities. Parses GetPropertyList() dictionaries
/// into structured data for the inspector UI.
/// </summary>
public static class PropertyHelper
{
    // PropertyUsageFlags from Godot (core/object/property_info.h)
    public const int UsageStorage = 1 << 1;    // 2
    public const int UsageEditor = 1 << 2;     // 4
    public const int UsageGroup = 1 << 6;      // 64
    public const int UsageCategory = 1 << 7;   // 128
    public const int UsageSubgroup = 1 << 8;   // 256
    public const int UsageReadOnly = 1 << 28;

    public record PropertyEntry(
        string Name,
        Variant.Type VariantType,
        PropertyHint Hint,
        string HintString,
        int Usage,
        string Category,
        string Group,
        bool IsReadOnly
    );

    /// <summary>
    /// Get all editor-visible properties of a GodotObject, grouped by category.
    /// </summary>
    public static List<PropertyEntry> GetProperties(GodotObject obj)
    {
        var result = new List<PropertyEntry>();
        var propList = obj.GetPropertyList();

        string currentCategory = "";
        string currentGroup = "";

        foreach (var propDict in propList)
        {
            string name = propDict["name"].AsString();
            int typeInt = propDict["type"].AsInt32();
            int hintInt = propDict["hint"].AsInt32();
            string hintString = propDict.TryGetValue("hint_string", out var hs) ? hs.AsString() : "";
            int usage = propDict["usage"].AsInt32();

            // Category marker
            if ((usage & UsageCategory) != 0)
            {
                currentCategory = name;
                currentGroup = "";
                // Add a marker entry for UI rendering
                result.Add(new PropertyEntry(name, Variant.Type.Nil, PropertyHint.None, "",
                    usage, currentCategory, "", false));
                continue;
            }

            // Group marker
            if ((usage & UsageGroup) != 0)
            {
                currentGroup = name;
                result.Add(new PropertyEntry(name, Variant.Type.Nil, PropertyHint.None, "",
                    usage, currentCategory, currentGroup, false));
                continue;
            }

            // Subgroup marker
            if ((usage & UsageSubgroup) != 0)
            {
                result.Add(new PropertyEntry(name, Variant.Type.Nil, PropertyHint.None, "",
                    usage, currentCategory, currentGroup, false));
                continue;
            }

            // Only include editor-visible properties
            if ((usage & UsageEditor) == 0) continue;

            bool readOnly = (usage & UsageReadOnly) != 0;

            result.Add(new PropertyEntry(
                name,
                (Variant.Type)typeInt,
                (PropertyHint)hintInt,
                hintString,
                usage,
                currentCategory,
                currentGroup,
                readOnly
            ));
        }

        return result;
    }

    /// <summary>
    /// Safely read a property value.
    /// </summary>
    public static Variant ReadValue(GodotObject obj, string property)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(obj)) return default;
            return obj.Get(property);
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Safely write a property value. Returns true on success.
    /// </summary>
    public static bool WriteValue(GodotObject obj, string property, Variant value)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(obj)) return false;
            obj.Set(property, value);
            return true;
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"[GodotExplorer] Failed to set {property}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if a PropertyEntry is a category/group marker (not an actual property).
    /// </summary>
    public static bool IsMarker(PropertyEntry entry)
    {
        return (entry.Usage & UsageCategory) != 0
            || (entry.Usage & UsageGroup) != 0
            || (entry.Usage & UsageSubgroup) != 0;
    }

    public static bool IsCategoryMarker(PropertyEntry entry) => (entry.Usage & UsageCategory) != 0;
    public static bool IsGroupMarker(PropertyEntry entry) => (entry.Usage & UsageGroup) != 0;
    public static bool IsSubgroupMarker(PropertyEntry entry) => (entry.Usage & UsageSubgroup) != 0;

    /// <summary>
    /// Get a human-readable type name for a Variant.Type.
    /// </summary>
    public static string TypeName(Variant.Type type)
    {
        return type switch
        {
            Variant.Type.Bool => "bool",
            Variant.Type.Int => "int",
            Variant.Type.Float => "float",
            Variant.Type.String => "String",
            Variant.Type.Vector2 => "Vector2",
            Variant.Type.Vector2I => "Vector2i",
            Variant.Type.Vector3 => "Vector3",
            Variant.Type.Vector3I => "Vector3i",
            Variant.Type.Rect2 => "Rect2",
            Variant.Type.Color => "Color",
            Variant.Type.NodePath => "NodePath",
            Variant.Type.Object => "Object",
            Variant.Type.Dictionary => "Dictionary",
            Variant.Type.Array => "Array",
            Variant.Type.Transform2D => "Transform2D",
            Variant.Type.Transform3D => "Transform3D",
            _ => type.ToString()
        };
    }
}
