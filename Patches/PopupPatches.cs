using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

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

        // The popups' SetCommandSelectCursor also fires on open and on a click on the focused button, so
        // each handler skips re-announcing when the cursor index hasn't changed. Reset to -1 on popup
        // close so reopening starts fresh.
        private static int lastCommonPopupCursorIndex = -1;
        private static int lastGameOverSelectCursorIndex = -1;
        private static int lastGameOverLoadCursorIndex = -1;

        // On a CommonPopup open, the message must be read BEFORE the focused button. CommonPopup.Open calls
        // SetCommandSelectCursor right after base Popup.Open (whose postfix sets this flag), so the button
        // would be read at once while the message read is one frame delayed: backwards ("No. Would you
        // like to return to title screen?"). The open-read speaks "message. button" together and this flag
        // suppresses the open-time focus announce.
        private static bool _suppressNextCommonFocus = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchBasePopup(harmony);
                TryPatchCommonPopupFocus(harmony);
                TryPatchGameOverSelectPopupFocus(harmony);
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

        private static void TryPatchCommonPopupFocus(HarmonyLib.Harmony harmony)
        {
            try
            {
                // SetCommandSelectCursor (0x72A700) is the popup's own cursor-set: called from Open, the
                // move callback (<UpdateSelect>b__2/b__4) and a click. It replaces the per-frame UpdateFocus
                // hook (UpdateFocus runs at the top of every UpdateSelect; round 2, 2026-09-24).
                Type popupType = typeof(KeyInputCommonPopup);
                var cursorSetMethod = AccessTools.Method(popupType, "SetCommandSelectCursor", Type.EmptyTypes);

                if (cursorSetMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(CommonPopup_FocusChanged_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(cursorSetMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Popup] CommonPopup.SetCommandSelectCursor method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching CommonPopup.SetCommandSelectCursor: {ex.Message}");
            }
        }

        private static void TryPatchGameOverSelectPopupFocus(HarmonyLib.Harmony harmony)
        {
            try
            {
                // SetCommandSelectCursor (0x48E030): Open, ResetCursor, move callback, click. Replaces the
                // per-frame UpdateFocus hook (round 2, 2026-09-24).
                Type popupType = typeof(KeyInputGameOverSelectPopup);
                var cursorSetMethod = AccessTools.Method(popupType, "SetCommandSelectCursor", Type.EmptyTypes);

                if (cursorSetMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(GameOverSelectPopup_FocusChanged_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(cursorSetMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Popup] GameOverSelectPopup.SetCommandSelectCursor method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching GameOverSelectPopup.SetCommandSelectCursor: {ex.Message}");
            }
        }

        private static void TryPatchGameOverLoadPopup(HarmonyLib.Harmony harmony)
        {
            try
            {
                // SetCommandSelectCursor (0x6EDF30): ResetCursor (SetPopupData = open), SetData, the move
                // callback, UpdateSelect's skip-disabled-command correction, click. Replaces the per-frame
                // UpdateCommand hook (round 2, 2026-09-24).
                Type loadPopupType = typeof(KeyInputGameOverLoadPopup);
                var cursorSetMethod = AccessTools.Method(loadPopupType, "SetCommandSelectCursor", Type.EmptyTypes);

                if (cursorSetMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(GameOverLoadPopup_FocusChanged_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(cursorSetMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Popup] GameOverLoadPopup.SetCommandSelectCursor method not found");
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
            return T("Game Over");
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

        /// <summary>
        /// Reads the focused button label of a popup given its selectCursor + commandList offsets. Reusable
        /// for any Cursor-driven button popup (e.g. SavePopup for save/load/quicksave). Returns the stripped
        /// label, or null. Used to announce the initially-focused button right after a popup's body message.
        /// </summary>
        public static string ReadFocusedButton(IntPtr popupPtr, int selectCursorOffset, int commandListOffset)
        {
            try
            {
                if (popupPtr == IntPtr.Zero) return null;
                IntPtr cursorPtr = IL2CppFieldReader.ReadPointerSafe(popupPtr, selectCursorOffset);
                if (cursorPtr == IntPtr.Zero) return null;
                int index = new GameCursor(cursorPtr).Index;
                string btn = ReadButtonFromCommandList(popupPtr, commandListOffset, index);
                return string.IsNullOrWhiteSpace(btn) ? null : TextUtils.StripIconMarkup(btn);
            }
            catch { return null; }
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

        public static void CommonPopup_FocusChanged_Postfix(object __instance)
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

                if (cursorIndex == lastCommonPopupCursorIndex)
                    return;
                lastCommonPopupCursorIndex = cursorIndex;

                // The open-read speaks the message + this button; don't also speak it here (out of order).
                if (_suppressNextCommonFocus)
                {
                    _suppressNextCommonFocus = false;
                    return;
                }

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
                MelonLogger.Warning($"[Popup] Error in CommonPopup focus postfix: {ex.Message}");
            }
        }

        public static void GameOverSelectPopup_FocusChanged_Postfix(object __instance)
        {
            var popup = __instance as KeyInputGameOverSelectPopup;
            if (popup == null) return;
            IntPtr popupPtr = popup.Pointer;
            if (popupPtr == IntPtr.Zero) return;
            if (!ReadGameOverButton(popupPtr, IL2CppOffsets.Popup.GameOverSelectCursor, IL2CppOffsets.Popup.GameOverCommandList,
                    ref lastGameOverSelectCursorIndex))
                CoroutineManager.StartManaged(RetryGameOverButton(popupPtr, IL2CppOffsets.Popup.GameOverSelectCursor,
                    IL2CppOffsets.Popup.GameOverCommandList, isLoadPopup: false));
        }

        public static void GameOverLoadPopup_FocusChanged_Postfix(object __instance)
        {
            var popup = __instance as KeyInputGameOverLoadPopup;
            if (popup == null) return;
            IntPtr popupPtr = popup.Pointer;
            if (popupPtr == IntPtr.Zero) return;
            if (!ReadGameOverButton(popupPtr, IL2CppOffsets.Popup.GameOverLoadSelectCursor, IL2CppOffsets.Popup.GameOverLoadCommandList,
                    ref lastGameOverLoadCursorIndex))
                CoroutineManager.StartManaged(RetryGameOverButton(popupPtr, IL2CppOffsets.Popup.GameOverLoadSelectCursor,
                    IL2CppOffsets.Popup.GameOverLoadCommandList, isLoadPopup: true));
        }

        /// <summary>
        /// Speaks the game-over popup's focused button when its index changed. Returns false only when the
        /// button text is not set yet (the cursor can be set before the popup's texts, e.g. at open); the
        /// index is then NOT stored, so the caller's short retry can still speak it.
        /// </summary>
        private static bool ReadGameOverButton(IntPtr popupPtr, int cursorOffset, int listOffset, ref int lastIndex)
        {
            try
            {
                IntPtr cursorPtr = IL2CppFieldReader.ReadPointerSafe(popupPtr, cursorOffset);
                if (cursorPtr == IntPtr.Zero) return true;
                int cursorIndex = new GameCursor(cursorPtr).Index;
                if (cursorIndex == lastIndex) return true;

                IntPtr listPtr = IL2CppFieldReader.ReadPointerSafe(popupPtr, listOffset);
                if (listPtr == IntPtr.Zero) return false;
                int size = IL2CppFieldReader.ReadListSize(listPtr);
                if (cursorIndex < 0 || cursorIndex >= size) return false;

                IntPtr commandPtr = IL2CppFieldReader.ReadListElement(listPtr, cursorIndex);
                if (commandPtr == IntPtr.Zero) return false;
                IntPtr textPtr = IL2CppFieldReader.ReadPointerSafe(commandPtr, IL2CppOffsets.Popup.CommonCommandText);
                if (textPtr == IntPtr.Zero) return false;

                string buttonText = new UnityEngine.UI.Text(textPtr).text;
                if (string.IsNullOrWhiteSpace(buttonText)) return false;

                lastIndex = cursorIndex;
                FFI_ScreenReaderMod.SpeakText(TextUtils.StripIconMarkup(buttonText.Trim()), interrupt: true);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error reading game-over button: {ex.Message}");
                return true;
            }
        }

        // Frames a game-over button read may wait for the popup's button text (open only).
        private const int GAME_OVER_BUTTON_MAX_FRAMES = 3;

        private static IEnumerator RetryGameOverButton(IntPtr popupPtr, int cursorOffset, int listOffset, bool isLoadPopup)
        {
            for (int i = 0; i < GAME_OVER_BUTTON_MAX_FRAMES; i++)
            {
                yield return null;
                bool done = isLoadPopup
                    ? ReadGameOverButton(popupPtr, cursorOffset, listOffset, ref lastGameOverLoadCursorIndex)
                    : ReadGameOverButton(popupPtr, cursorOffset, listOffset, ref lastGameOverSelectCursorIndex);
                if (done) yield break;
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
                    // Read message + focused button together (message first); suppress the open-time
                    // focus announce so the button isn't spoken before the message. CommonPopup has its own
                    // focus reader, so the generic cursor reader must not also read the button.
                    _suppressNextCommonFocus = true;
                    lastCommonPopupCursorIndex = -1;
                    PopupState.SetActive("CommonPopup", commonPopup.Pointer, IL2CppOffsets.Popup.CommonCommandList,
                        hasOwnFocusReader: true);
                    CoroutineManager.StartManaged(DelayedCommonPopupRead(commonPopup.Pointer));
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
                    // Has its own focus reader — don't let the generic reader also read the button.
                    HandlePopupDetected("GameOverSelectPopup", gameOver.Pointer, IL2CppOffsets.Popup.GameOverCommandList,
                        () => ReadGameOverSelectPopup(gameOver.Pointer), hasOwnFocusReader: true);
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

        private static void HandlePopupDetected(string typeName, IntPtr ptr, int cmdListOffset, Func<string> readFunc, bool hasOwnFocusReader = false)
        {
            PopupState.SetActive(typeName, ptr, cmdListOffset, hasOwnFocusReader);
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

        // CommonPopup open-read: message FIRST, then the focused button — so "Would you like to return
        // to title screen? No" instead of the reversed "No. Would you like to return to title screen?".
        private static IEnumerator DelayedCommonPopupRead(IntPtr popupPtr)
        {
            yield return null;
            string announcement = null;
            try
            {
                if (popupPtr != IntPtr.Zero)
                {
                    string message = ReadCommonPopup(popupPtr);
                    string button = ReadFocusedCommonButton(popupPtr);
                    if (!string.IsNullOrWhiteSpace(message) && !string.IsNullOrWhiteSpace(button))
                        announcement = $"{message} {button}";
                    else
                        announcement = string.IsNullOrWhiteSpace(message) ? button : message;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in CommonPopup read: {ex.Message}");
            }
            finally
            {
                _suppressNextCommonFocus = false;
            }
            if (!string.IsNullOrWhiteSpace(announcement))
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: false);
        }

        /// <summary>Reads the CommonPopup's currently-focused button label (and primes the dedup index).</summary>
        private static string ReadFocusedCommonButton(IntPtr popupPtr)
        {
            try
            {
                IntPtr cursorPtr = IL2CppFieldReader.ReadPointerSafe(popupPtr, IL2CppOffsets.Popup.CommonSelectCursor);
                if (cursorPtr == IntPtr.Zero) return null;
                int idx = new GameCursor(cursorPtr).Index;
                lastCommonPopupCursorIndex = idx;   // so the focus reader won't re-announce the same button
                string btn = ReadButtonFromCommandList(popupPtr, IL2CppOffsets.Popup.CommonCommandList, idx);
                return string.IsNullOrWhiteSpace(btn) ? null : TextUtils.StripIconMarkup(btn);
            }
            catch { return null; }
        }

        public static void PopupClose_Postfix()
        {
            try
            {
                if (PopupState.IsConfirmationPopupActive)
                {
                    PopupState.Clear();
                }
                lastCommonPopupCursorIndex = -1;
                lastGameOverSelectCursorIndex = -1;
                lastGameOverLoadCursorIndex = -1;
                _suppressNextCommonFocus = false;

                // A Quit Game / Return to Title popup over the config menu closes WITHOUT changing config
                // state, so SelectCommand doesn't re-fire — re-announce the focused config option. Gated on
                // ConfigMenuState.IsActive so save/load/battle/dialogue popup closes don't trigger it.
                if (ConfigMenuState.IsActive)
                    ConfigController_SetActive_Patch.ReannounceFocusedConfigOption();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in Close postfix: {ex.Message}");
            }
        }

        #endregion
    }
}
