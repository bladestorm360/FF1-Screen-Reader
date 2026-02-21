using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

// Type aliases for IL2CPP types - Base
using BasePopup = Il2CppLast.UI.Popup;
using GameCursor = Il2CppLast.UI.Cursor;

// Type aliases for IL2CPP types - KeyInput Popups
using KeyInputCommonPopup = Il2CppLast.UI.KeyInput.CommonPopup;
using KeyInputChangeMagicStonePopup = Il2CppLast.UI.KeyInput.ChangeMagicStonePopup;
using KeyInputGameOverSelectPopup = Il2CppLast.UI.KeyInput.GameOverSelectPopup;
using KeyInputGameOverLoadPopup = Il2CppLast.UI.KeyInput.GameOverLoadPopup;
using KeyInputGameOverPopupController = Il2CppLast.UI.KeyInput.GameOverPopupController;
using KeyInputInfomationPopup = Il2CppLast.UI.KeyInput.InfomationPopup;
using KeyInputInputPopup = Il2CppLast.UI.KeyInput.InputPopup;
using KeyInputChangeNamePopup = Il2CppLast.UI.KeyInput.ChangeNamePopup;
using KeyInputShopController = Il2CppLast.UI.KeyInput.ShopController;

// Type aliases for IL2CPP types - Touch Popups
using TouchCommonPopup = Il2CppLast.UI.Touch.CommonPopup;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Patches for popup dialogs - handles ALL popup reading (message + buttons).
    /// Uses TryCast for IL2CPP-safe type detection.
    /// Title screen patches are in TitleScreenPatches.
    /// </summary>
    public static class PopupPatches
    {
        private static bool isPatched = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchBasePopup(harmony);
                TryPatchCommonPopupUpdateFocus(harmony);
                TryPatchGameOverSelectPopupUpdateFocus(harmony);
                TryPatchGameOverLoadPopup(harmony);

                // Title screen patches (separate class)
                TitleScreenPatches.ApplyPatches(harmony);

                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error applying patches: {ex.Message}");
            }
        }

        #region Patch Registration

        private static void TryPatchBasePopup(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type popupType = typeof(BasePopup);

                var openMethod = AccessTools.Method(popupType, "Open");
                if (openMethod != null)
                {
                    var openPostfix = typeof(PopupPatches).GetMethod(nameof(PopupOpen_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(openMethod, postfix: new HarmonyMethod(openPostfix));
                }

                var closeMethod = AccessTools.Method(popupType, "Close");
                if (closeMethod != null)
                {
                    var closePostfix = typeof(PopupPatches).GetMethod(nameof(PopupClose_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(closeMethod, postfix: new HarmonyMethod(closePostfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching base Popup: {ex.Message}");
            }
        }

        private static void TryPatchCommonPopupUpdateFocus(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type popupType = typeof(KeyInputCommonPopup);
                var updateFocusMethod = AccessTools.Method(popupType, "UpdateFocus");

                if (updateFocusMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(CommonPopup_UpdateFocus_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateFocusMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Popup] CommonPopup.UpdateFocus method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching CommonPopup.UpdateFocus: {ex.Message}");
            }
        }

        private static void TryPatchGameOverSelectPopupUpdateFocus(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type popupType = typeof(KeyInputGameOverSelectPopup);
                var updateFocusMethod = AccessTools.Method(popupType, "UpdateFocus");

                if (updateFocusMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(GameOverSelectPopup_UpdateFocus_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateFocusMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Popup] GameOverSelectPopup.UpdateFocus method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching GameOverSelectPopup.UpdateFocus: {ex.Message}");
            }
        }

        private static void TryPatchGameOverLoadPopup(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type loadPopupType = typeof(KeyInputGameOverLoadPopup);
                var updateCommandMethod = AccessTools.Method(loadPopupType, "UpdateCommand");

                if (updateCommandMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(GameOverLoadPopup_UpdateCommand_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateCommandMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Popup] GameOverLoadPopup.UpdateCommand method not found");
                }

                Type controllerType = typeof(KeyInputGameOverPopupController);
                var initMethod = AccessTools.Method(controllerType, "InitSaveLoadPopup");

                if (initMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(GameOverPopupController_InitSaveLoadPopup_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Popup] GameOverPopupController.InitSaveLoadPopup method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching GameOverLoadPopup: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        private static bool IsShopActive()
        {
            try
            {
                var shopController = UnityEngine.Object.FindObjectOfType<KeyInputShopController>();
                return shopController != null && shopController.IsOpne;
            }
            catch
            {
                return false; // Shop state unavailable; assume inactive
            }
        }

        private static string ReadTextFromPointer(IntPtr textPtr)
        {
            if (textPtr == IntPtr.Zero) return null;
            try
            {
                var text = new Text(textPtr);
                return text?.text;
            }
            catch { return null; } // IL2CPP text pointer may be stale
        }

        private static string ReadIconTextViewText(IntPtr iconTextViewPtr)
        {
            if (iconTextViewPtr == IntPtr.Zero) return null;
            try
            {
                IntPtr nameTextPtr = IL2CppFieldReader.ReadPointerSafe(iconTextViewPtr, IL2CppOffsets.Popup.IconTextViewNameText);
                return ReadTextFromPointer(nameTextPtr);
            }
            catch { return null; } // Nested pointer read may fail
        }

        private static string BuildAnnouncement(string title, string message)
        {
            title = string.IsNullOrWhiteSpace(title) ? null : TextUtils.StripIconMarkup(title.Trim());
            message = string.IsNullOrWhiteSpace(message) ? null : TextUtils.StripIconMarkup(message.Trim());

            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(message))
                return $"{title}. {message}";
            else if (!string.IsNullOrEmpty(title))
                return title;
            else if (!string.IsNullOrEmpty(message))
                return message;
            return null;
        }

        #endregion

        #region Type-Specific Readers

        private static string ReadCommonPopup(IntPtr ptr)
        {
            IntPtr titleViewPtr = IL2CppFieldReader.ReadPointerSafe(ptr, IL2CppOffsets.Popup.CommonTitle);
            string title = ReadIconTextViewText(titleViewPtr);
            IntPtr messagePtr = IL2CppFieldReader.ReadPointerSafe(ptr, IL2CppOffsets.Popup.CommonMessage);
            string message = ReadTextFromPointer(messagePtr);
            return BuildAnnouncement(title, message);
        }

        private static string ReadChangeMagicStonePopup(IntPtr ptr)
        {
            IntPtr namePtr = IL2CppFieldReader.ReadPointerSafe(ptr, IL2CppOffsets.Popup.MagicStoneName);
            string name = ReadTextFromPointer(namePtr);
            IntPtr descPtr = IL2CppFieldReader.ReadPointerSafe(ptr, IL2CppOffsets.Popup.MagicStoneDesc);
            string desc = ReadTextFromPointer(descPtr);
            return BuildAnnouncement(name, desc);
        }

        private static string ReadGameOverSelectPopup(IntPtr ptr)
        {
            return "Game Over";
        }

        private static string ReadInfomationPopup(IntPtr ptr)
        {
            IntPtr titleViewPtr = IL2CppFieldReader.ReadPointerSafe(ptr, IL2CppOffsets.Popup.InfoTitle);
            string title = ReadIconTextViewText(titleViewPtr);
            IntPtr messagePtr = IL2CppFieldReader.ReadPointerSafe(ptr, IL2CppOffsets.Popup.InfoMessage);
            string message = ReadTextFromPointer(messagePtr);
            return BuildAnnouncement(title, message);
        }

        private static string ReadInputPopup(IntPtr ptr)
        {
            IntPtr descPtr = IL2CppFieldReader.ReadPointerSafe(ptr, IL2CppOffsets.Popup.InputDesc);
            string desc = ReadTextFromPointer(descPtr);
            return string.IsNullOrWhiteSpace(desc) ? null : TextUtils.StripIconMarkup(desc.Trim());
        }

        private static string ReadChangeNamePopup(IntPtr ptr)
        {
            IntPtr descPtr = IL2CppFieldReader.ReadPointerSafe(ptr, IL2CppOffsets.Popup.ChangeNameDesc);
            string desc = ReadTextFromPointer(descPtr);
            return string.IsNullOrWhiteSpace(desc) ? null : TextUtils.StripIconMarkup(desc.Trim());
        }

        #endregion

        #region Button Reading

        /// <summary>
        /// Read current button label from active popup.
        /// Called by CursorNavigation_Postfix when popup is active.
        /// </summary>
        public static void ReadCurrentButton(GameCursor cursor)
        {
            try
            {
                if (PopupState.ActivePopupPtr == IntPtr.Zero)
                    return;

                if (PopupState.CommandListOffset < 0)
                    return;

                string buttonText = ReadButtonFromCommandList(
                    PopupState.ActivePopupPtr,
                    PopupState.CommandListOffset,
                    cursor.Index);

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText);
                    FFI_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error reading button: {ex.Message}");
            }
        }

        private static string ReadButtonFromCommandList(IntPtr popupPtr, int cmdListOffset, int index)
        {
            try
            {
                IntPtr listPtr = IL2CppFieldReader.ReadPointerSafe(popupPtr, cmdListOffset);
                if (listPtr == IntPtr.Zero) return null;

                int size = IL2CppFieldReader.ReadListSize(listPtr);
                if (index < 0 || index >= size) return null;

                IntPtr commandPtr = IL2CppFieldReader.ReadListElement(listPtr, index);
                if (commandPtr == IntPtr.Zero) return null;

                IntPtr textPtr = IL2CppFieldReader.ReadPointerSafe(commandPtr, IL2CppOffsets.Popup.CommonCommandText);
                return ReadTextFromPointer(textPtr);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error reading command list: {ex.Message}");
                return null;
            }
        }

        public static void CommonPopup_UpdateFocus_Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;

                var popup = __instance as KeyInputCommonPopup;
                if (popup == null) return;

                IntPtr popupPtr = popup.Pointer;
                if (popupPtr == IntPtr.Zero) return;

                IntPtr cursorPtr = IL2CppFieldReader.ReadPointerSafe(popupPtr, IL2CppOffsets.Popup.CommonSelectCursor);
                if (cursorPtr == IntPtr.Zero) return;

                var cursor = new GameCursor(cursorPtr);
                int cursorIndex = cursor.Index;

                if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.POPUP_BUTTON, cursorIndex))
                    return;

                IntPtr listPtr = IL2CppFieldReader.ReadPointerSafe(popupPtr, IL2CppOffsets.Popup.CommonCommandList);
                if (listPtr == IntPtr.Zero) return;

                int size = IL2CppFieldReader.ReadListSize(listPtr);
                if (cursorIndex < 0 || cursorIndex >= size) return;

                IntPtr commandPtr = IL2CppFieldReader.ReadListElement(listPtr, cursorIndex);
                if (commandPtr == IntPtr.Zero) return;

                IntPtr textPtr = IL2CppFieldReader.ReadPointerSafe(commandPtr, IL2CppOffsets.Popup.CommonCommandText);
                if (textPtr == IntPtr.Zero) return;

                var textComponent = new UnityEngine.UI.Text(textPtr);
                string buttonText = textComponent.text;

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText.Trim());
                    FFI_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in UpdateFocus postfix: {ex.Message}");
            }
        }

        public static void GameOverSelectPopup_UpdateFocus_Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;

                var popup = __instance as KeyInputGameOverSelectPopup;
                if (popup == null) return;

                IntPtr popupPtr = popup.Pointer;
                if (popupPtr == IntPtr.Zero) return;

                IntPtr cursorPtr = IL2CppFieldReader.ReadPointerSafe(popupPtr, IL2CppOffsets.Popup.GameOverSelectCursor);
                if (cursorPtr == IntPtr.Zero)
                    return;

                var cursor = new GameCursor(cursorPtr);
                int cursorIndex = cursor.Index;

                if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.POPUP_GAMEOVER_BUTTON, cursorIndex))
                    return;

                IntPtr listPtr = IL2CppFieldReader.ReadPointerSafe(popupPtr, IL2CppOffsets.Popup.GameOverCommandList);
                if (listPtr == IntPtr.Zero)
                    return;

                int size = IL2CppFieldReader.ReadListSize(listPtr);
                if (cursorIndex < 0 || cursorIndex >= size)
                    return;

                IntPtr commandPtr = IL2CppFieldReader.ReadListElement(listPtr, cursorIndex);
                if (commandPtr == IntPtr.Zero) return;

                IntPtr textPtr = IL2CppFieldReader.ReadPointerSafe(commandPtr, IL2CppOffsets.Popup.CommonCommandText);
                if (textPtr == IntPtr.Zero) return;

                var textComponent = new UnityEngine.UI.Text(textPtr);
                string buttonText = textComponent.text;

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText.Trim());
                    FFI_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in GameOverSelectPopup UpdateFocus postfix: {ex.Message}");
            }
        }

        public static void GameOverLoadPopup_UpdateCommand_Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;

                var popup = __instance as KeyInputGameOverLoadPopup;
                if (popup == null) return;

                IntPtr popupPtr = popup.Pointer;
                if (popupPtr == IntPtr.Zero) return;

                IntPtr cursorPtr = IL2CppFieldReader.ReadPointerSafe(popupPtr, IL2CppOffsets.Popup.GameOverLoadSelectCursor);
                if (cursorPtr == IntPtr.Zero)
                    return;

                var cursor = new GameCursor(cursorPtr);
                int cursorIndex = cursor.Index;

                if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.POPUP_GAMEOVER_LOAD_BUTTON, cursorIndex))
                    return;

                IntPtr listPtr = IL2CppFieldReader.ReadPointerSafe(popupPtr, IL2CppOffsets.Popup.GameOverLoadCommandList);
                if (listPtr == IntPtr.Zero)
                    return;

                int size = IL2CppFieldReader.ReadListSize(listPtr);
                if (cursorIndex < 0 || cursorIndex >= size)
                    return;

                IntPtr commandPtr = IL2CppFieldReader.ReadListElement(listPtr, cursorIndex);
                if (commandPtr == IntPtr.Zero) return;

                IntPtr textPtr = IL2CppFieldReader.ReadPointerSafe(commandPtr, IL2CppOffsets.Popup.CommonCommandText);
                if (textPtr == IntPtr.Zero) return;

                var textComponent = new UnityEngine.UI.Text(textPtr);
                string buttonText = textComponent.text;

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText.Trim());
                    FFI_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in GameOverLoadPopup UpdateCommand postfix: {ex.Message}");
            }
        }

        public static void GameOverPopupController_InitSaveLoadPopup_Postfix(object __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var controller = __instance as KeyInputGameOverPopupController;
                if (controller == null)
                    return;

                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return;

                CoroutineManager.StartManaged(DelayedGameOverLoadPopupRead(controllerPtr));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in InitSaveLoadPopup postfix: {ex.Message}");
            }
        }

        private static IEnumerator DelayedGameOverLoadPopupRead(IntPtr controllerPtr)
        {
            yield return null;

            try
            {
                if (controllerPtr == IntPtr.Zero) yield break;

                IntPtr viewPtr = IL2CppFieldReader.ReadPointerSafe(controllerPtr, IL2CppOffsets.Popup.GameOverPopupCtrlView);
                if (viewPtr == IntPtr.Zero)
                    yield break;

                IntPtr loadPopupPtr = IL2CppFieldReader.ReadPointerSafe(viewPtr, IL2CppOffsets.Popup.GameOverPopupViewLoadPopup);
                if (loadPopupPtr == IntPtr.Zero)
                    yield break;

                IntPtr messagePtr = IL2CppFieldReader.ReadPointerSafe(loadPopupPtr, IL2CppOffsets.Popup.GameOverLoadMessage);
                string message = ReadTextFromPointer(messagePtr);

                if (!string.IsNullOrWhiteSpace(message))
                {
                    message = TextUtils.StripIconMarkup(message.Trim());
                    FFI_ScreenReaderMod.SpeakText(message, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in GameOverLoad delayed read: {ex.Message}");
            }
        }

        #endregion

        #region Popup Open/Close Postfixes

        public static void PopupOpen_Postfix(BasePopup __instance)
        {
            try
            {
                if (__instance == null)
                    return;
                if (IsShopActive())
                    return;

                var commonPopup = __instance.TryCast<KeyInputCommonPopup>();
                if (commonPopup != null)
                {
                    HandlePopupDetected("CommonPopup", commonPopup.Pointer, IL2CppOffsets.Popup.CommonCommandList,
                        () => ReadCommonPopup(commonPopup.Pointer));
                    return;
                }

                var magicStone = __instance.TryCast<KeyInputChangeMagicStonePopup>();
                if (magicStone != null)
                {
                    HandlePopupDetected("ChangeMagicStonePopup", magicStone.Pointer, IL2CppOffsets.Popup.MagicStoneCommandList,
                        () => ReadChangeMagicStonePopup(magicStone.Pointer));
                    return;
                }

                var gameOver = __instance.TryCast<KeyInputGameOverSelectPopup>();
                if (gameOver != null)
                {
                    HandlePopupDetected("GameOverSelectPopup", gameOver.Pointer, IL2CppOffsets.Popup.GameOverCommandList,
                        () => ReadGameOverSelectPopup(gameOver.Pointer));
                    return;
                }

                var info = __instance.TryCast<KeyInputInfomationPopup>();
                if (info != null)
                {
                    HandlePopupDetected("InfomationPopup", info.Pointer, -1,
                        () => ReadInfomationPopup(info.Pointer));
                    return;
                }

                var input = __instance.TryCast<KeyInputInputPopup>();
                if (input != null)
                {
                    HandlePopupDetected("InputPopup", input.Pointer, -1,
                        () => ReadInputPopup(input.Pointer));
                    return;
                }

                var changeName = __instance.TryCast<KeyInputChangeNamePopup>();
                if (changeName != null)
                {
                    HandlePopupDetected("ChangeNamePopup", changeName.Pointer, -1,
                        () => ReadChangeNamePopup(changeName.Pointer));
                    return;
                }

                var touchCommon = __instance.TryCast<TouchCommonPopup>();
                if (touchCommon != null)
                {
                    HandlePopupDetected("TouchCommonPopup", touchCommon.Pointer, -1,
                        () => {
                            IntPtr titlePtr = IL2CppFieldReader.ReadPointerSafe(touchCommon.Pointer, 0x28);
                            string title = ReadTextFromPointer(titlePtr);
                            IntPtr msgPtr = IL2CppFieldReader.ReadPointerSafe(touchCommon.Pointer, 0x38);
                            string msg = ReadTextFromPointer(msgPtr);
                            return BuildAnnouncement(title, msg);
                        });
                    return;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in Open postfix: {ex.Message}");
            }
        }

        private static void HandlePopupDetected(string typeName, IntPtr ptr, int cmdListOffset, Func<string> readFunc)
        {
            PopupState.SetActive(typeName, ptr, cmdListOffset);
            CoroutineManager.StartManaged(DelayedPopupRead(ptr, typeName, readFunc));
        }

        private static IEnumerator DelayedPopupRead(IntPtr popupPtr, string typeName, Func<string> readFunc)
        {
            yield return null;

            try
            {
                if (popupPtr == IntPtr.Zero) yield break;

                string announcement = readFunc();
                if (!string.IsNullOrEmpty(announcement))
                {
                    FFI_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in delayed read: {ex.Message}");
            }
        }

        public static void PopupClose_Postfix()
        {
            try
            {
                if (PopupState.IsConfirmationPopupActive)
                {
                    PopupState.Clear();
                }
                AnnouncementDeduplicator.Reset(AnnouncementContexts.POPUP_BUTTON);
                AnnouncementDeduplicator.Reset(AnnouncementContexts.POPUP_GAMEOVER_BUTTON);
                AnnouncementDeduplicator.Reset(AnnouncementContexts.POPUP_GAMEOVER_LOAD_BUTTON);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in Close postfix: {ex.Message}");
            }
        }

        #endregion
    }
}
