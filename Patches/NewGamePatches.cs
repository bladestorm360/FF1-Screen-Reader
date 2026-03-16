using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

// FF1 Type aliases - FF1-specific types in Serial.FF1 namespace
using NewGameWindowController = Il2CppSerial.FF1.UI.KeyInput.NewGameWindowController;
using NewGamePopup = Il2CppSerial.FF1.UI.KeyInput.NewGamePopup;
// Shared types in Last namespace
using CharacterContentListController = Il2CppLast.UI.KeyInput.CharacterContentListController;
using NameContentListController = Il2CppLast.UI.KeyInput.NameContentListController;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Patches for new game character creation (Light Warrior naming and class selection).
    /// Helper methods (grid decoding, name/job reading) are in NewGameHelpers.
    /// </summary>
    public static class NewGamePatches
    {
        // Track controller for cross-method access
        private static object currentController = null;

        // Track last target index from SetTargetSelectContent (for UpdateView fallback)
        private static int lastTargetIndex = -1;

        // Track last slot names for change detection
        private static string[] lastSlotNames = new string[4];

        // SelectContent state tracking
        private static int lastCharacterIndex = -1;
        private static int lastFieldType = -1; // -1 = none, 0 = name, 1 = class

        // Decoded character slot from SelectContent (for UpdateView)
        private static int decodedCharacterSlot = -1;

        // One-time diagnostic dump flag
        private static bool hasLoggedContentList = false;

        /// <summary>
        /// Flag indicating NewGame is handling cursor events.
        /// Used to prevent MenuTextDiscovery from double-reading.
        /// </summary>
        public static bool IsHandlingCursor { get; private set; } = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(NewGameWindowController);
                Type charListType = typeof(CharacterContentListController);
                Type nameListType = typeof(NameContentListController);

                PatchSelectContent(harmony, charListType);
                PatchMethod(harmony, controllerType, "InitSelect", nameof(InitSelect_Postfix));
                PatchMethod(harmony, charListType, "SetTargetSelectContent", nameof(SetTargetSelectContent_Postfix));
                PatchMethod(harmony, controllerType, "InitNameSelect", nameof(InitNameSelect_Postfix));
                PatchMethod(harmony, nameListType, "SetFocus", nameof(SetFocus_Postfix));
                PatchMethod(harmony, charListType, "UpdateView", nameof(UpdateView_Postfix));
                PatchMethod(harmony, controllerType, "InitNameInput", nameof(InitNameInput_Postfix));
                PatchMethod(harmony, controllerType, "InitStartPopup", nameof(InitStartPopup_Postfix));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[New Game] Failed to apply patches: {ex.Message}");
            }
        }

        private static void PatchSelectContent(HarmonyLib.Harmony harmony, Type charListType)
        {
            try
            {
                var selectMethod = AccessTools.Method(charListType, "SelectContent");
                if (selectMethod != null)
                {
                    var postfix = typeof(NewGamePatches).GetMethod("SelectContent_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(selectMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[New Game] SelectContent method not found on CharacterContentListController");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[New Game] Error patching SelectContent: {ex.Message}");
            }
        }

        private static void PatchMethod(HarmonyLib.Harmony harmony, Type targetType, string methodName, string postfixName)
        {
            try
            {
                var method = AccessTools.Method(targetType, methodName);
                if (method == null)
                {
                    method = targetType.GetMethod(methodName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }

                if (method != null)
                {
                    var postfix = typeof(NewGamePatches).GetMethod(postfixName,
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning($"[New Game] {targetType.Name}.{methodName} not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[New Game] Error patching {methodName}: {ex.Message}");
            }
        }

        #region SelectContent (Primary Navigation)

        public static void SelectContent_Postfix(object __instance, object targetCursor)
        {
            IsHandlingCursor = true;

            try
            {
                if (targetCursor == null)
                    return;

                var indexProp = AccessTools.Property(targetCursor.GetType(), "Index");
                if (indexProp == null)
                    return;

                int cursorIndex = (int)indexProp.GetValue(targetCursor);

                NewGameHelpers.DecodeGridIndex(cursorIndex, out int charIdx, out bool isClass, out bool isDone);
                if (!isDone && charIdx >= 0 && charIdx < 4)
                {
                    decodedCharacterSlot = charIdx;
                }

                CoroutineManager.StartUntracked(AnnounceFieldAfterDelay(__instance, cursorIndex));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SelectContent_Postfix: {ex.Message}");
                IsHandlingCursor = false;
            }
        }

        private static IEnumerator AnnounceFieldAfterDelay(object listController, int cursorIndex)
        {
            yield return null;

            try
            {
                if (!hasLoggedContentList)
                {
                    hasLoggedContentList = true;
                    try
                    {
                        var listProp = AccessTools.Property(listController.GetType(), "ContentList");
                        var list = listProp?.GetValue(listController);
                        var countProp = list?.GetType().GetProperty("Count");
                        int count = countProp != null ? (int)countProp.GetValue(list) : -1;
                        var indexer = list?.GetType().GetProperty("Item");
                        for (int i = 0; i < Math.Min(count, 12); i++)
                        {
                            var item = indexer?.GetValue(list, new object[] { i });
                            var nameProp = item != null ? AccessTools.Property(item.GetType(), "CharacterName") : null;
                            string name = nameProp?.GetValue(item) as string ?? "(null)";
                        }
                        var statusesProp = AccessTools.Property(listController.GetType(), "Characterstatuses");
                        var statusesList = statusesProp?.GetValue(listController);
                        var sCountProp = statusesList?.GetType().GetProperty("Count");
                        int sCount = sCountProp != null ? (int)sCountProp.GetValue(statusesList) : -1;
                    }
                    catch (Exception ex) { MelonLogger.Warning($"[New Game] Diagnostic dump error: {ex.Message}"); }
                }

                NewGameHelpers.DecodeGridIndex(cursorIndex, out int characterIndex, out bool isClassField, out bool isDone);

                if (isDone)
                {
                    lastCharacterIndex = -1;
                    lastFieldType = -1;

                    string doneText = T("Done");
                    AnnouncementHelper.AnnounceIfNew(AnnouncementContexts.NEW_GAME_FIELD, doneText);
                    yield break;
                }

                int fieldType = isClassField ? 1 : 0;
                string fieldLabel = isClassField ? T("Class") : T("Name");

                string formulaName = NewGameHelpers.GetCharacterSlotNameOnly(listController, characterIndex);
                string formulaJob = isClassField ? NewGameHelpers.GetJobName(listController, characterIndex) : null;

                string directName = NewGameHelpers.GetCharacterSlotNameOnly(listController, cursorIndex);
                string directJob = isClassField ? NewGameHelpers.GetJobName(listController, cursorIndex) : null;

                string fieldValue;
                if (isClassField)
                {
                    fieldValue = formulaJob ?? directJob ?? T("unknown");
                }
                else
                {
                    fieldValue = formulaName ?? directName ?? T("unnamed");
                }

                bool characterChanged = (characterIndex != lastCharacterIndex);
                bool fieldChanged = (fieldType != lastFieldType);

                string announcement;

                if (characterChanged)
                {
                    announcement = string.Format(T("Light Warrior {0}: {1}: {2}"), characterIndex + 1, fieldLabel, fieldValue);
                }
                else if (fieldChanged)
                {
                    announcement = $"{fieldLabel}: {fieldValue}";
                }
                else
                {
                    yield break;
                }

                lastCharacterIndex = characterIndex;
                lastFieldType = fieldType;

                AnnouncementHelper.AnnounceIfNew(AnnouncementContexts.NEW_GAME_FIELD, announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in AnnounceFieldAfterDelay: {ex.Message}");
            }
            finally
            {
                IsHandlingCursor = false;
            }
        }

        public static void ClearHandlingFlag()
        {
            IsHandlingCursor = false;
        }

        #endregion

        #region Existing Patches

        public static void InitSelect_Postfix(object __instance)
        {
            try
            {
                currentController = __instance;

                lastTargetIndex = -1;
                lastCharacterIndex = -1;
                lastFieldType = -1;
                decodedCharacterSlot = -1;
                for (int i = 0; i < lastSlotNames.Length; i++)
                    lastSlotNames[i] = null;

                AnnouncementDeduplicator.Reset(AnnouncementContexts.NEW_GAME_SLOT, AnnouncementContexts.NEW_GAME_NAME, AnnouncementContexts.NEW_GAME_AUTO_INDEX, AnnouncementContexts.NEW_GAME_FIELD);

                FFI_ScreenReaderMod.SpeakText(T("Character selection"), interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitSelect_Postfix: {ex.Message}");
            }
        }

        public static void SetTargetSelectContent_Postfix(object __instance, int index)
        {
            try
            {
                NewGameHelpers.DecodeGridIndex(index, out int charIdx, out bool isClass, out bool isDone);
                if (!isDone && charIdx >= 0 && charIdx < 4)
                    lastTargetIndex = charIdx;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetTargetSelectContent_Postfix: {ex.Message}");
            }
        }

        public static void InitNameSelect_Postfix(object __instance)
        {
            try
            {
                currentController = __instance;

                AnnouncementDeduplicator.Reset(AnnouncementContexts.NEW_GAME_NAME, AnnouncementContexts.NEW_GAME_AUTO_INDEX);

                string characterName = NewGameHelpers.GetCurrentCharacterName(__instance);
                string suggestedName = NewGameHelpers.GetCurrentSuggestedName(__instance);

                string announcement;
                if (!string.IsNullOrEmpty(characterName))
                {
                    announcement = string.Format(T("Select name for {0}"), characterName);
                }
                else
                {
                    announcement = T("Select name");
                }

                if (!string.IsNullOrEmpty(suggestedName))
                {
                    announcement += string.Format(T(". Current: {0}"), suggestedName);
                    AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.NEW_GAME_NAME, suggestedName);
                }

                FFI_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitNameSelect_Postfix: {ex.Message}");
            }
        }

        public static void SetFocus_Postfix(int index)
        {
            try
            {
                if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.NEW_GAME_AUTO_INDEX, index))
                    return;

                string currentName = null;
                if (currentController != null)
                {
                    currentName = NewGameHelpers.GetAutoNameByIndex(currentController, index);
                }

                if (!string.IsNullOrEmpty(currentName) &&
                    AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.NEW_GAME_NAME, currentName))
                {
                    FFI_ScreenReaderMod.SpeakText(currentName);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetFocus_Postfix: {ex.Message}");
            }
        }

        public static void UpdateView_Postfix(object __instance)
        {
            try
            {
                int currentSlot = decodedCharacterSlot >= 0 ? decodedCharacterSlot : lastTargetIndex;

                if (currentSlot < 0 || currentSlot >= 4)
                    return;

                string currentName = NewGameHelpers.GetCharacterSlotNameOnly(__instance, currentSlot);
                string lastName = lastSlotNames[currentSlot];

                if (!string.IsNullOrEmpty(currentName) && currentName != lastName)
                {
                    lastSlotNames[currentSlot] = currentName;
                    FFI_ScreenReaderMod.SpeakText(currentName);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in UpdateView_Postfix: {ex.Message}");
            }
        }

        public static void InitNameInput_Postfix(object __instance)
        {
            try
            {
                string characterName = NewGameHelpers.GetCurrentCharacterName(__instance);

                string announcement;
                if (!string.IsNullOrEmpty(characterName))
                {
                    announcement = string.Format(T("Enter name for {0}. Type using keyboard."), characterName);
                }
                else
                {
                    announcement = T("Enter name using keyboard");
                }

                FFI_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitNameInput_Postfix: {ex.Message}");
            }
        }

        public static void InitStartPopup_Postfix(object __instance)
        {
            try
            {
                var popupField = __instance.GetType().GetField("newGamePopup",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (popupField != null)
                {
                    var popup = popupField.GetValue(__instance);
                    if (popup != null)
                    {
                        var msgProp = popup.GetType().GetProperty("Message",
                            BindingFlags.Public | BindingFlags.Instance);

                        if (msgProp != null)
                        {
                            string message = msgProp.GetValue(popup) as string;
                            if (!string.IsNullOrEmpty(message))
                            {
                                message = TextUtils.StripIconMarkup(message.Trim());
                                FFI_ScreenReaderMod.SpeakText(message);
                                return;
                            }
                        }
                    }
                }

                FFI_ScreenReaderMod.SpeakText(T("Start game?"));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitStartPopup_Postfix: {ex.Message}");
            }
        }

        #endregion
    }
}
