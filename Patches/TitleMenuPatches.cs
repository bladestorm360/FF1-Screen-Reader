using System;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Menus;
using FFI_ScreenReader.Utils;

using TitleWindowController = Il2CppLast.UI.KeyInput.TitleWindowController;
using TitleMenuCommandController = Il2CppLast.UI.KeyInput.TitleMenuCommandController;
using GameCursor = Il2CppLast.UI.Cursor;

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

        // TitleMenuCommandController.activeContents (List<TitleCommandContentView>) — the visible commands.
        private const int OFFSET_ACTIVE_CONTENTS = 0x28;

        /// <summary>
        /// Visible title-command count for the "(X of Y)" suffix, or -1 when <paramref name="cursor"/> is
        /// not the title menu's cursor (so it's inert on every other menu — no false positives). The title
        /// menu stores its commands in a C# list (activeContents), not a Content transform, so the generic
        /// reader can't derive the count; this supplies it for both the on-entry and per-navigation reads
        /// (both flow through MenuTextDiscovery.WaitAndReadCursor). Uses activeContents (visible) so disabled
        /// commands (e.g. Continue with no save) are excluded, matching what the cursor navigates.
        /// </summary>
        internal static int TryGetActiveCommandCount(GameCursor cursor)
        {
            try
            {
                if (cursor == null) return -1;
                var cmd = UnityEngine.Object.FindObjectOfType<TitleMenuCommandController>();
                if (cmd == null || cmd.gameObject == null || !cmd.gameObject.activeInHierarchy) return -1;
                var titleCursor = cmd.selectCursor;
                if (titleCursor == null || titleCursor.Pointer != cursor.Pointer) return -1; // not the title cursor
                IntPtr listPtr = IL2CppFieldReader.ReadPointer(cmd.Pointer, OFFSET_ACTIVE_CONTENTS);
                if (listPtr == IntPtr.Zero) return -1;
                return IL2CppFieldReader.ReadListSize(listPtr);
            }
            catch { return -1; } // best-effort; -1 → no suffix
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
