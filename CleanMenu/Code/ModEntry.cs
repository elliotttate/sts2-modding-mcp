using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace CleanMenu;

[ModInitializer("Init")]
public static class ModEntry
{
    private static Harmony? _harmony;

    public static void Init()
    {
        _harmony = new Harmony("com.elliotttate.cleanmenu");
        _harmony.PatchAll();
        Log.Warn("[CleanMenu] Loaded! Press F1 on main menu to toggle clean mode.");
    }
}
