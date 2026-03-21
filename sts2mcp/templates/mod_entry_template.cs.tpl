using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace {namespace};

[ModInitializer("Init")]
public static class ModEntry
{{
    private static Harmony? _harmony;

    public static void Init()
    {{
        Log.Warn("[{mod_name}] Initializing...");

        _harmony = new Harmony("{harmony_id}");
        _harmony.PatchAll();

        Log.Warn("[{mod_name}] Loaded successfully!");
    }}
}}
