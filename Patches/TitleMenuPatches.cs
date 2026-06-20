using System;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Menus;
using FFI_ScreenReader.Utils;

using TitleWindowController = Il2CppLast.UI.KeyInput.TitleWindowController;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Announces the initially-focused item of the title menus (main menu + Options list) on entry.
    /// Both are rendered by the same TitleMenuCommandController (TitleWindowController.commandController),
    /// driven by the TitleWindowController state machine. Navigation already flows through the generic
    /// cursor reader, but the initial cursor placement doesn't fire Cursor.NextIndex, so nothing is
    /// announced on entry. We hook the per-state Init callbacks and reuse the same reader.
    ///   • InitSelect       → the main menu (New Game / Extras / Options) — fires on first entry AND back-out.
    ///   • InitializeOption → the Options list (Privacy Policy / Configuration / Display Settings / Language).
    /// (Config settings is OptionController.ShowConfig, handled in ConfigMenuPatches.)
    /// </summary>
    internal static class TitleMenuReader
    {
        internal static void AnnounceFocus(TitleWindowController inst, string source)
        {
            try
            {
                if (inst == null || inst.gameObject == null || !inst.gameObject.activeInHierarchy)
                    return;
                var cmd = inst.commandController;            // TitleMenuCommandController @ 0x50
                if (cmd == null) return;
                var cursor = cmd.selectCursor;               // @ 0x30
                if (cursor == null) return;
                MelonLogger.Msg($"[Title] Announcing initial title-menu focus via {source}");
                CoroutineManager.StartManaged(MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", 0, false));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] Error in {source} postfix: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(TitleWindowController), "InitSelect")]
    public static class TitleWindowController_InitSelect_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(TitleWindowController __instance) => TitleMenuReader.AnnounceFocus(__instance, "InitSelect");
    }

    [HarmonyPatch(typeof(TitleWindowController), "InitializeOption")]
    public static class TitleWindowController_InitializeOption_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(TitleWindowController __instance) => TitleMenuReader.AnnounceFocus(__instance, "InitializeOption");
    }

    [HarmonyPatch(typeof(TitleWindowController), "InitializeExtra")]
    public static class TitleWindowController_InitializeExtra_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(TitleWindowController __instance) => TitleMenuReader.AnnounceFocus(__instance, "InitializeExtra");
    }
}
