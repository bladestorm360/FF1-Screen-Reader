using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Menus;
using FFI_ScreenReader.Utils;

using GameCursor = Il2CppLast.UI.Cursor;
using SaveListController = Il2CppLast.UI.KeyInput.SaveListController;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Announces the initially-highlighted save slot when the save/load list opens. SaveListController
    /// is shared by all entry points (load from the title screen, save and load from the field menu),
    /// so this one hook covers them all. Navigation is already read by the generic cursor reader; this
    /// is purely the open-read the game doesn't otherwise trigger.
    /// </summary>
    [HarmonyPatch(typeof(SaveListController), "SetActive", new Type[] { typeof(bool), typeof(bool), typeof(bool) })]
    public static class SaveListController_SetActive_Patch
    {
        private const int OFFSET_SELECT_CURSOR = 0x58;  // SaveListController.selectCursor

        [HarmonyPostfix]
        public static void Postfix(SaveListController __instance, bool isActive)
        {
            if (!isActive || __instance == null) return;
            try
            {
                CoroutineManager.StartManaged(DelayedReadSlot(__instance));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveList] Error scheduling slot read: {ex.Message}");
            }
        }

        private static IEnumerator DelayedReadSlot(SaveListController controller)
        {
            yield return null;   // let the list populate + cursor settle
            string announcement = null;
            try
            {
                // Only announce when a real save/load menu is up (see ShouldReadSaveSlot). The background
                // autosave during a map load also fires SaveListController.SetActive, but no menu is open
                // then, so it's excluded.
                if (controller != null && controller.gameObject != null && controller.gameObject.activeInHierarchy
                    && ShouldReadSaveSlot())
                {
                    IntPtr cursorPtr = IL2CppFieldReader.ReadPointerSafe(controller.Pointer, OFFSET_SELECT_CURSOR);
                    if (cursorPtr != IntPtr.Zero)
                    {
                        var cursor = new GameCursor(cursorPtr);
                        var t = cursor.transform;
                        if (t != null)
                            announcement = SaveSlotReader.TryReadSaveSlot(t, cursor.Index);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveList] Error reading slot: {ex.Message}");
            }
            if (!string.IsNullOrWhiteSpace(announcement))
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
        }

        /// <summary>
        /// True when save-slot content should be announced. A real in-game save/load/quick-save menu is
        /// open (MenuManager.IsOpen — reliably FALSE during the scene-construction flurry on a map load,
        /// the same signal that fixed the "Battle Mode: wait" game-load read), OR the title Load screen is
        /// on-screen (it is NOT a MenuManager menu). This excludes the background autosave whose
        /// SaveListController is momentarily active during construction.
        /// </summary>
        public static bool ShouldReadSaveSlot()
        {
            try
            {
                try
                {
                    var mm = Il2CppLast.UI.MenuManager.Instance;
                    if (mm != null && mm.IsOpen) return true;
                }
                catch { } // MenuManager not available yet
                // Title Load screen (LoadGameWindowController) is not a MenuManager menu.
                return Active(UnityEngine.Object.FindObjectOfType<Il2CppLast.UI.KeyInput.LoadGameWindowController>());
            }
            catch { return false; }
        }

        private static bool Active(UnityEngine.Component c)
            => c != null && c.gameObject != null && c.gameObject.activeInHierarchy;
    }
}
