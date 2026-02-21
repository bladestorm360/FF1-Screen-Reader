using System;
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

// FF1 Save/Load UI types
// All controllers use SavePopup with messageText at 0x40, commandList at 0x60
using TitleLoadController = Il2CppLast.UI.KeyInput.LoadGameWindowController;  // Title screen load (savePopup at 0x58)
using MainMenuLoadController = Il2CppLast.UI.KeyInput.LoadWindowController;   // Main menu load (savePopup at 0x28)
using MainMenuSaveController = Il2CppLast.UI.KeyInput.SaveWindowController;   // Main menu save (savePopup at 0x28)
using InterruptionController = Il2CppLast.UI.KeyInput.InterruptionWindowController;  // QuickSave (savePopup at 0x38)
using KeyInputSavePopup = Il2CppLast.UI.KeyInput.SavePopup;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Tracks save/load menu state for suppression.
    /// </summary>
    public static class SaveLoadMenuState
    {
        /// <summary>
        /// True when save/load menu is active.
        /// Delegates to MenuStateRegistry for centralized state tracking.
        /// </summary>
        public static bool IsActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.SAVE_LOAD_MENU);
            set => MenuStateRegistry.SetActive(MenuStateRegistry.SAVE_LOAD_MENU, value);
        }
        public static bool IsInConfirmation { get; set; } = false;

        public static bool ShouldSuppress()
        {
            return IsActive && IsInConfirmation;
        }

        public static void ResetState()
        {
            IsActive = false;
            IsInConfirmation = false;
        }
    }

    /// <summary>
    /// Patches for Save/Load confirmation popups.
    ///
    /// Hooks SetPopupActive(bool isEnable) on three controllers:
    /// - LoadGameWindowController (title screen load)
    /// - LoadWindowController (main menu load)
    /// - SaveWindowController (main menu save)
    ///
    /// All use SavePopup with messageText at 0x40, commandList at 0x60.
    /// </summary>
    public static class SaveLoadPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch SetPopupActive(bool) on save/load controllers
                TryPatchTitleLoad(harmony);
                TryPatchMainMenuLoad(harmony);
                TryPatchMainMenuSave(harmony);

                // Patch SetEnablePopup(bool) on QuickSave controller
                TryPatchInterruption(harmony);

            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[SaveLoad] Failed to apply patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches LoadGameWindowController.SetPopupActive (title screen load).
        /// </summary>
        private static void TryPatchTitleLoad(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(TitleLoadController);
                var method = AccessTools.Method(controllerType, "SetPopupActive");

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(TitleLoadSetPopupActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] TitleLoadController.SetPopupActive not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch TitleLoadController: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches LoadWindowController.SetPopupActive (main menu load).
        /// </summary>
        private static void TryPatchMainMenuLoad(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(MainMenuLoadController);
                var method = AccessTools.Method(controllerType, "SetPopupActive");

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(MainMenuLoadSetPopupActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] MainMenuLoadController.SetPopupActive not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch MainMenuLoadController: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches SaveWindowController.SetPopupActive (main menu save).
        /// </summary>
        private static void TryPatchMainMenuSave(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(MainMenuSaveController);
                var method = AccessTools.Method(controllerType, "SetPopupActive");

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(MainMenuSaveSetPopupActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] MainMenuSaveController.SetPopupActive not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch MainMenuSaveController: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches InterruptionWindowController.SetEnablePopup (QuickSave).
        /// </summary>
        private static void TryPatchInterruption(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(InterruptionController);
                var method = AccessTools.Method(controllerType, "SetEnablePopup");

                if (method != null)
                {
                    var postfix = typeof(SaveLoadPatches).GetMethod(nameof(InterruptionSetEnablePopup_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[SaveLoad] InterruptionController.SetEnablePopup not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Failed to patch InterruptionController: {ex.Message}");
            }
        }

        // ============ Postfix Methods ============

        public static void TitleLoadSetPopupActive_Postfix(object __instance, bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    var controller = __instance as TitleLoadController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, IL2CppOffsets.SaveLoad.TitleLoadSavePopup, "TitleLoad");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in TitleLoadSetPopupActive_Postfix: {ex.Message}");
            }
        }

        public static void MainMenuLoadSetPopupActive_Postfix(object __instance, bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    var controller = __instance as MainMenuLoadController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, IL2CppOffsets.SaveLoad.MainMenuSavePopup, "MainMenuLoad");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in MainMenuLoadSetPopupActive_Postfix: {ex.Message}");
            }
        }

        public static void MainMenuSaveSetPopupActive_Postfix(object __instance, bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    var controller = __instance as MainMenuSaveController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, IL2CppOffsets.SaveLoad.MainMenuSavePopup, "MainMenuSave");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in MainMenuSaveSetPopupActive_Postfix: {ex.Message}");
            }
        }

        public static void InterruptionSetEnablePopup_Postfix(object __instance, bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    var controller = __instance as InterruptionController;
                    if (controller != null)
                    {
                        ReadSavePopup(controller.Pointer, IL2CppOffsets.SaveLoad.InterruptionSavePopup, "QuickSave");
                    }
                }
                else
                {
                    ClearPopupState();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error in InterruptionSetEnablePopup_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts a coroutine to read SavePopup message after a short delay.
        /// The delay allows the UI to populate the text before we read it.
        /// </summary>
        private static void ReadSavePopup(IntPtr controllerPtr, int savePopupOffset, string context)
        {
            if (controllerPtr == IntPtr.Zero)
            {
                MelonLogger.Warning($"[SaveLoad] {context}: Controller pointer is null");
                return;
            }

            try
            {
                // Read savePopup pointer from controller
                IntPtr popupPtr = IL2CppFieldReader.ReadPointer(controllerPtr, savePopupOffset);
                if (popupPtr == IntPtr.Zero)
                {
                    MelonLogger.Warning($"[SaveLoad] {context}: SavePopup pointer is null");
                    return;
                }

                // Set state for button navigation immediately
                SaveLoadMenuState.IsActive = true;
                SaveLoadMenuState.IsInConfirmation = true;
                PopupState.SetActive($"{context}Popup", popupPtr, IL2CppOffsets.SaveLoad.CommandList);

                // Start coroutine to read text after delay (allows UI to populate)
                CoroutineManager.StartManaged(ReadPopupTextDelayed(popupPtr, context));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error reading {context} popup: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine that waits a frame then reads the popup text.
        /// </summary>
        private static IEnumerator ReadPopupTextDelayed(IntPtr popupPtr, string context)
        {
            // Wait 2 frames to let UI populate
            yield return null;
            yield return null;

            try
            {
                // Read messageText from SavePopup
                IntPtr messageTextPtr = IL2CppFieldReader.ReadPointer(popupPtr, IL2CppOffsets.SaveLoad.MessageText);
                if (messageTextPtr == IntPtr.Zero)
                {
                    MelonLogger.Warning($"[SaveLoad] {context}: messageText pointer is null");
                    yield break;
                }

                var textComponent = new UnityEngine.UI.Text(messageTextPtr);
                string message = textComponent.text;

                if (!string.IsNullOrWhiteSpace(message))
                {
                    // Strip Unity rich text tags (like <color=#ff4040>...</color>)
                    message = StripRichTextTags(message);
                    FFI_ScreenReaderMod.SpeakText(message);
                }
                else
                {
                    MelonLogger.Warning($"[SaveLoad] {context}: Message is empty");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveLoad] Error reading {context} popup text: {ex.Message}");
            }
        }

        /// <summary>
        /// Strips Unity rich text tags from a string.
        /// Removes tags like <color=#xxxxxx>, </color>, <b>, </b>, etc.
        /// </summary>
        private static string StripRichTextTags(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Remove all XML-style tags: <tagname>, </tagname>, <tagname=value>, etc.
            return Regex.Replace(text, @"<[^>]+>", string.Empty);
        }

        private static void ClearPopupState()
        {
            SaveLoadMenuState.ResetState();
            PopupState.Clear();
        }
    }
}
