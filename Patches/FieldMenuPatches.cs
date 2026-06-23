using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Menus;
using FFI_ScreenReader.Utils;

using GameCursor = Il2CppLast.UI.Cursor;
using MenuManager = Il2CppLast.UI.MenuManager;
using MainMenuController = Il2CppLast.UI.KeyInput.MainMenuController;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Announces the initially-focused command of the FIELD menu (the in-game main menu opened
    /// while walking the map — Item / Magic / Equip / Status / Config / Save / etc.) on open.
    /// Navigation already flows through the generic cursor reader (CursorNavigation_Postfix →
    /// MenuTextDiscovery), but the initial cursor placement never fires Cursor.NextIndex, so
    /// nothing is announced on entry — the same gap the title menu had before TitleMenuPatches.
    /// We hook MainMenuController.Show and reuse the same cursor reader navigation uses.
    ///
    /// Gated on MenuManager.IsOpen so it never reads during the scene-construction flurry of a
    /// map/asset load (Show / cursor activation can fire then, not just on a real player open —
    /// the same trap that had to be fixed for the save and config menus). The non-Touch field
    /// command bar (Il2CppLast.UI.CommandMenuController) carries the generic selectCursor; we
    /// reach it via typed Il2CppInterop property accessors, no manual offsets. (Do NOT read
    /// MainMenuController.focusId — that is the SELECTED command, not the focused one.)
    /// </summary>
    internal static class FieldMenuReader
    {
        // Bumped on every AnnounceFocus call; the delayed read aborts if a newer call superseded it. On
        // open both Show and InitNone fire AnnounceFocus within a frame or two — the latch collapses them
        // to a single announce (the later one wins) while still re-announcing on every sub-menu back-out.
        private static int _gen;

        internal static void AnnounceFocus(MainMenuController inst)
        {
            if (inst == null) return;
            int gen = ++_gen;
            try { CoroutineManager.StartManaged(DelayedAnnounce(inst, gen)); }
            catch (Exception ex) { MelonLogger.Warning($"[Field] Error scheduling focus read: {ex.Message}"); }
        }

        // yield is OUTSIDE the try (yield-in-try-with-catch is illegal); the gate + cursor fetch
        // run in the try storing into a local, then the read is kicked off after it — the same
        // shape as SaveListPatches.DelayedReadSlot.
        private static IEnumerator DelayedAnnounce(MainMenuController inst, int gen)
        {
            yield return null; // let the cursor settle AND MenuManager.IsOpen flip true

            if (gen != _gen) yield break; // a newer AnnounceFocus superseded this one — collapse the double

            GameCursor cursor = null;
            try
            {
                if (inst != null && inst.gameObject != null && inst.gameObject.activeInHierarchy
                    && IsFieldMenuOpen())
                {
                    var cmd = inst.commandMenuController;        // Il2CppLast.UI.CommandMenuController @ 0x38
                    if (cmd != null) cursor = cmd.selectCursor;  // Il2CppLast.UI.Cursor @ 0x38
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Field] Error reading field-menu focus: {ex.Message}");
            }

            if (cursor != null)
                CoroutineManager.StartManaged(MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", 0, false));
        }

        // MenuManager.IsOpen is reliably FALSE during a map/asset load — the gate that fixed the
        // save and config menus reading stale/placeholder content during scene construction.
        private static bool IsFieldMenuOpen()
        {
            try { var mm = MenuManager.Instance; return mm != null && mm.IsOpen; }
            catch { return false; } // MenuManager not constructed yet during early load
        }

        /// <summary>
        /// Visible field-command count for the "(X of Y)" suffix, or -1 when <paramref name="cursor"/> is
        /// not the field menu's cursor (so it's inert on every other menu). The field menu stores its
        /// commands in a C# list (CommandMenuController.contents), not a Content transform, so the generic
        /// reader can't derive the count; this supplies it for navigation (which flows through
        /// MenuTextDiscovery.WaitAndReadCursor). Mirrors TitleMenuReader.TryGetActiveCommandCount.
        /// </summary>
        internal static int TryGetFieldCommandCount(GameCursor cursor)
        {
            try
            {
                if (cursor == null) return -1;
                var cmd = UnityEngine.Object.FindObjectOfType<Il2CppLast.UI.CommandMenuController>();
                if (cmd == null || cmd.gameObject == null || !cmd.gameObject.activeInHierarchy) return -1;
                var fieldCursor = cmd.selectCursor;
                if (fieldCursor == null || fieldCursor.Pointer != cursor.Pointer) return -1; // not the field cursor
                var contents = cmd.contents;
                return contents != null ? contents.Count : -1;
            }
            catch { return -1; } // best-effort; -1 → no suffix
        }
    }

    [HarmonyPatch(typeof(MainMenuController), "Show", new Type[] { typeof(bool) })]
    public static class MainMenuController_Show_FieldFocus_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(MainMenuController __instance) => FieldMenuReader.AnnounceFocus(__instance);
    }

    /// <summary>
    /// Re-announce the focused field command when the menu returns to the command-select (None) state from
    /// any sub-menu OR the quicksave popup. MainMenuController's state machine is keyed on MenuCommandId;
    /// every sub-menu (Item/Magic/Equipment/Status/Config/Sort/Words) and the Interruption (quicksave) state
    /// transitions back to None on cancel, firing InitNone. The generation latch in AnnounceFocus collapses
    /// the Show+InitNone pair on initial open so it announces once.
    /// </summary>
    [HarmonyPatch]
    public static class MainMenuController_InitNone_FieldReturn_Patch
    {
        static System.Reflection.MethodBase TargetMethod()
            => AccessTools.Method(typeof(MainMenuController), "InitNone");

        [HarmonyPostfix]
        public static void Postfix(MainMenuController __instance)
        {
            FieldMenuReader.AnnounceFocus(__instance);
        }
    }
}
