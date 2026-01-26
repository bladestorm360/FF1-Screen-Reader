using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

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
    /// Port of FF3's NewGameNamingPatches with FF1-specific changes.
    ///
    /// Patches:
    /// - SelectContent: Primary navigation handler for character grid (Name/Class/Done)
    /// - InitSelect: Entering character slot selection mode
    /// - SetTargetSelectContent: Index tracking for UpdateView
    /// - InitNameSelect: Entering name selection mode
    /// - SetFocus: Name cycling (event-driven)
    /// - UpdateView: Name change detection
    /// - InitNameInput: Keyboard input mode
    /// - InitStartPopup: "Start game?" confirmation popup
    /// </summary>
    public static class NewGamePatches
    {
        // Deduplication contexts
        private const string CONTEXT_SLOT = "NewGame.Slot";
        private const string CONTEXT_NAME = "NewGame.Name";
        private const string CONTEXT_AUTO_INDEX = "NewGame.AutoIndex";
        private const string CONTEXT_FIELD = "NewGame.Field";

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
                MelonLogger.Msg("[New Game] Applying new game naming patches...");

                Type controllerType = typeof(NewGameWindowController);
                Type charListType = typeof(CharacterContentListController);
                Type nameListType = typeof(NameContentListController);

                // SelectContent - primary navigation handler for character grid
                PatchSelectContent(harmony, charListType);

                // InitSelect - entering character selection mode
                PatchMethod(harmony, controllerType, "InitSelect", nameof(InitSelect_Postfix));

                // SetTargetSelectContent - index tracking for UpdateView
                PatchMethod(harmony, charListType, "SetTargetSelectContent", nameof(SetTargetSelectContent_Postfix));

                // InitNameSelect - entering name selection mode
                PatchMethod(harmony, controllerType, "InitNameSelect", nameof(InitNameSelect_Postfix));

                // SetFocus - name cycling (event-driven)
                PatchMethod(harmony, nameListType, "SetFocus", nameof(SetFocus_Postfix));

                // UpdateView - name change detection
                PatchMethod(harmony, charListType, "UpdateView", nameof(UpdateView_Postfix));

                // InitNameInput - keyboard input mode
                PatchMethod(harmony, controllerType, "InitNameInput", nameof(InitNameInput_Postfix));

                // InitStartPopup - start game confirmation popup
                PatchMethod(harmony, controllerType, "InitStartPopup", nameof(InitStartPopup_Postfix));

                MelonLogger.Msg("[New Game] All new game patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[New Game] Failed to apply patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches SelectContent on CharacterContentListController.
        /// Same manual pattern as JobSelectionPatches.
        /// </summary>
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
                    MelonLogger.Msg("[New Game] Patched CharacterContentListController.SelectContent");
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
                    MelonLogger.Msg($"[New Game] Patched {targetType.Name}.{methodName}");
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

        #region Grid Decoding

        /// <summary>
        /// Decodes a cursor grid index into character index and field type.
        /// Character-major encoding: flat index = characterIndex * 2 + commandIndex
        /// Grid layout (2 commands per character, 2 columns of characters):
        ///   Index 0: LW1 Name  (char=0, cmd=0)  |  Index 2: LW2 Name  (char=1, cmd=0)
        ///   Index 1: LW1 Class (char=0, cmd=1)  |  Index 3: LW2 Class (char=1, cmd=1)
        ///   Index 4: LW3 Name  (char=2, cmd=0)  |  Index 6: LW4 Name  (char=3, cmd=0)
        ///   Index 5: LW3 Class (char=2, cmd=1)  |  Index 7: LW4 Class (char=3, cmd=1)
        ///   Index 8+: Done
        /// Formula: characterIndex = cursorIndex / 2
        ///          isClass = cursorIndex % 2 == 1
        /// </summary>
        private static void DecodeGridIndex(int cursorIndex, out int characterIndex, out bool isClassField, out bool isDone)
        {
            if (cursorIndex >= 8)
            {
                characterIndex = -1;
                isClassField = false;
                isDone = true;
                MelonLogger.Msg($"[New Game] DecodeGridIndex: cursor={cursorIndex} -> Done");
                return;
            }

            characterIndex = cursorIndex / 2;
            isClassField = cursorIndex % 2 == 1;
            isDone = false;

            MelonLogger.Msg($"[New Game] DecodeGridIndex: cursor={cursorIndex} -> char={characterIndex}, isClass={isClassField}");
        }

        #endregion

        #region SelectContent (Primary Navigation)

        /// <summary>
        /// Postfix for CharacterContentListController.SelectContent.
        /// Primary navigation handler for the character creation grid.
        /// Announces Name/Class field with character context.
        /// </summary>
        public static void SelectContent_Postfix(object __instance, object targetCursor)
        {
            IsHandlingCursor = true;

            try
            {
                if (targetCursor == null)
                {
                    MelonLogger.Msg("[New Game] SelectContent: targetCursor is null");
                    return;
                }

                // Get cursor index
                var indexProp = AccessTools.Property(targetCursor.GetType(), "Index");
                if (indexProp == null)
                {
                    MelonLogger.Msg("[New Game] SelectContent: Cursor.Index property not found");
                    return;
                }

                int cursorIndex = (int)indexProp.GetValue(targetCursor);
                MelonLogger.Msg($"[New Game] SelectContent index={cursorIndex}");

                // Decode grid index synchronously for UpdateView tracking
                DecodeGridIndex(cursorIndex, out int charIdx, out bool isClass, out bool isDone);
                if (!isDone && charIdx >= 0 && charIdx < 4)
                {
                    decodedCharacterSlot = charIdx;
                }

                // Start coroutine for announcement (waits one frame for UI to settle)
                CoroutineManager.StartUntracked(AnnounceFieldAfterDelay(__instance, cursorIndex));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SelectContent_Postfix: {ex.Message}");
                IsHandlingCursor = false;
            }
        }

        /// <summary>
        /// Coroutine to announce character field info after one frame delay.
        /// Handles Name, Class, and Done announcements with character context.
        /// </summary>
        private static IEnumerator AnnounceFieldAfterDelay(object listController, int cursorIndex)
        {
            yield return null; // Wait one frame

            try
            {
                // One-time diagnostic dump of ContentList structure
                if (!hasLoggedContentList)
                {
                    hasLoggedContentList = true;
                    try
                    {
                        var listProp = AccessTools.Property(listController.GetType(), "ContentList");
                        var list = listProp?.GetValue(listController);
                        var countProp = list?.GetType().GetProperty("Count");
                        int count = countProp != null ? (int)countProp.GetValue(list) : -1;
                        MelonLogger.Msg($"[New Game] ContentList count={count}");
                        var indexer = list?.GetType().GetProperty("Item");
                        for (int i = 0; i < Math.Min(count, 12); i++)
                        {
                            var item = indexer?.GetValue(list, new object[] { i });
                            var nameProp = item != null ? AccessTools.Property(item.GetType(), "CharacterName") : null;
                            string name = nameProp?.GetValue(item) as string ?? "(null)";
                            MelonLogger.Msg($"[New Game]   ContentList[{i}] CharacterName='{name}'");
                        }
                        var statusesProp = AccessTools.Property(listController.GetType(), "Characterstatuses");
                        var statusesList = statusesProp?.GetValue(listController);
                        var sCountProp = statusesList?.GetType().GetProperty("Count");
                        int sCount = sCountProp != null ? (int)sCountProp.GetValue(statusesList) : -1;
                        MelonLogger.Msg($"[New Game] Characterstatuses count={sCount}");
                    }
                    catch (Exception ex) { MelonLogger.Warning($"[New Game] Diagnostic dump error: {ex.Message}"); }
                }

                DecodeGridIndex(cursorIndex, out int characterIndex, out bool isClassField, out bool isDone);

                if (isDone)
                {
                    // Reset tracking
                    lastCharacterIndex = -1;
                    lastFieldType = -1;

                    string doneText = "Done";
                    if (AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_FIELD, doneText))
                    {
                        MelonLogger.Msg("[New Game] Done button");
                        FFI_ScreenReaderMod.SpeakText(doneText);
                    }
                    yield break;
                }

                // Determine field type and value
                int fieldType = isClassField ? 1 : 0;
                string fieldLabel = isClassField ? "Class" : "Name";

                // Read via formula (current approach)
                string formulaName = GetCharacterSlotNameOnly(listController, characterIndex);
                string formulaJob = isClassField ? GetJobName(listController, characterIndex) : null;

                // Read via direct cursor index (proposed fix)
                string directName = GetCharacterSlotNameOnly(listController, cursorIndex);
                string directJob = isClassField ? GetJobName(listController, cursorIndex) : null;

                MelonLogger.Msg($"[New Game] cursor={cursorIndex}, char={characterIndex}: " +
                    $"formulaName='{formulaName}' directName='{directName}' " +
                    $"formulaJob='{formulaJob}' directJob='{directJob}'");

                // USE whichever produces correct data (start with formula, compare in log)
                string fieldValue;
                if (isClassField)
                {
                    fieldValue = formulaJob ?? directJob ?? "unknown";
                }
                else
                {
                    fieldValue = formulaName ?? directName ?? "unnamed";
                }

                // Build announcement based on what changed
                bool characterChanged = (characterIndex != lastCharacterIndex);
                bool fieldChanged = (fieldType != lastFieldType);

                string announcement;

                if (characterChanged)
                {
                    // Full announcement with character context
                    announcement = $"Light Warrior {characterIndex + 1}: {fieldLabel}: {fieldValue}";
                }
                else if (fieldChanged)
                {
                    // Same character, different field
                    announcement = $"{fieldLabel}: {fieldValue}";
                }
                else
                {
                    // Nothing changed
                    MelonLogger.Msg("[New Game] No change, skipping announcement");
                    yield break;
                }

                // Update tracking
                lastCharacterIndex = characterIndex;
                lastFieldType = fieldType;

                // String-only dedup
                if (AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_FIELD, announcement))
                {
                    MelonLogger.Msg($"[New Game] {announcement}");
                    FFI_ScreenReaderMod.SpeakText(announcement);
                }
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

        /// <summary>
        /// Clears the handling flag. Called by CursorNavigation_Postfix after skipping a read.
        /// </summary>
        public static void ClearHandlingFlag()
        {
            IsHandlingCursor = false;
        }

        #endregion

        #region Existing Patches

        /// <summary>
        /// Postfix for InitSelect - announces entering character selection mode.
        /// Stores controller reference for other patches.
        /// </summary>
        public static void InitSelect_Postfix(object __instance)
        {
            try
            {
                currentController = __instance;

                // Reset all tracking state
                lastTargetIndex = -1;
                lastCharacterIndex = -1;
                lastFieldType = -1;
                decodedCharacterSlot = -1;
                for (int i = 0; i < lastSlotNames.Length; i++)
                    lastSlotNames[i] = null;

                AnnouncementDeduplicator.Reset(CONTEXT_SLOT, CONTEXT_NAME, CONTEXT_AUTO_INDEX, CONTEXT_FIELD);

                MelonLogger.Msg("[New Game] Character selection");
                FFI_ScreenReaderMod.SpeakText("Character selection", interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitSelect_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// EVENT-DRIVEN: Postfix for CharacterContentListController.SetTargetSelectContent(int).
        /// Tracks index for UpdateView. No announcements (SelectContent handles those).
        /// </summary>
        public static void SetTargetSelectContent_Postfix(object __instance, int index)
        {
            try
            {
                DecodeGridIndex(index, out int charIdx, out bool isClass, out bool isDone);
                if (!isDone && charIdx >= 0 && charIdx < 4)
                    lastTargetIndex = charIdx;
                MelonLogger.Msg($"[New Game] SetTargetSelectContent grid={index} -> char={charIdx}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetTargetSelectContent_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for InitNameSelect - announces entering name selection mode.
        /// Reads the current character name from the controller instance.
        /// Also stores controller reference for event-driven hooks.
        /// </summary>
        public static void InitNameSelect_Postfix(object __instance)
        {
            try
            {
                // Store controller reference for event-driven hooks
                currentController = __instance;

                // Reset tracking
                AnnouncementDeduplicator.Reset(CONTEXT_NAME, CONTEXT_AUTO_INDEX);

                // Try to get CurrentData property which has CharacterName
                string characterName = GetCurrentCharacterName(__instance);

                // Get the current suggested name
                string suggestedName = GetCurrentSuggestedName(__instance);

                string announcement;
                if (!string.IsNullOrEmpty(characterName))
                {
                    announcement = $"Select name for {characterName}";
                }
                else
                {
                    announcement = "Select name";
                }

                if (!string.IsNullOrEmpty(suggestedName))
                {
                    announcement += $". Current: {suggestedName}";
                    AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_NAME, suggestedName);
                }

                MelonLogger.Msg($"[New Game] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitNameSelect_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// EVENT-DRIVEN: Postfix for KeyInput.NameContentListController.SetFocus
        /// Fires once when name focus changes during name cycling.
        /// </summary>
        public static void SetFocus_Postfix(int index)
        {
            try
            {
                // Check if index changed
                if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_AUTO_INDEX, index))
                {
                    return;
                }

                // Get the name at this index from stored NewGameWindowController
                string currentName = null;
                if (currentController != null)
                {
                    currentName = GetAutoNameByIndex(currentController, index);
                }

                if (!string.IsNullOrEmpty(currentName) &&
                    AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_NAME, currentName))
                {
                    MelonLogger.Msg($"[New Game] Name: {currentName}");
                    FFI_ScreenReaderMod.SpeakText(currentName);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetFocus_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// EVENT-DRIVEN: Postfix for CharacterContentListController.UpdateView
        /// Fires when the character list view is refreshed, including after name assignment.
        /// Detects name changes and announces the new name.
        /// </summary>
        public static void UpdateView_Postfix(object __instance)
        {
            try
            {
                // Use decodedCharacterSlot from SelectContent if available,
                // otherwise fall back to lastTargetIndex
                int currentSlot = decodedCharacterSlot >= 0 ? decodedCharacterSlot : lastTargetIndex;

                MelonLogger.Msg($"[New Game] UpdateView called: currentSlot={currentSlot}");

                // Skip if no slot selected yet
                if (currentSlot < 0 || currentSlot >= 4)
                {
                    return;
                }

                // Get the current name for this slot
                string currentName = GetCharacterSlotNameOnly(__instance, currentSlot);
                string lastName = lastSlotNames[currentSlot];

                MelonLogger.Msg($"[New Game] UpdateView: slot={currentSlot}, currentName='{currentName}', lastName='{lastName}'");

                // Announce if name exists and is different from last known name for this slot
                if (!string.IsNullOrEmpty(currentName) && currentName != lastName)
                {
                    lastSlotNames[currentSlot] = currentName;
                    MelonLogger.Msg($"[New Game] Name changed: {currentName}");
                    FFI_ScreenReaderMod.SpeakText(currentName);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in UpdateView_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for InitNameInput - announces entering keyboard input mode.
        /// </summary>
        public static void InitNameInput_Postfix(object __instance)
        {
            try
            {
                string characterName = GetCurrentCharacterName(__instance);

                string announcement;
                if (!string.IsNullOrEmpty(characterName))
                {
                    announcement = $"Enter name for {characterName}. Type using keyboard.";
                }
                else
                {
                    announcement = "Enter name using keyboard";
                }

                MelonLogger.Msg($"[New Game] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitNameInput_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for InitStartPopup - reads the "Start game with these settings?" confirmation.
        /// </summary>
        public static void InitStartPopup_Postfix(object __instance)
        {
            try
            {
                // Try to get newGamePopup field and read its Message property
                var popupField = __instance.GetType().GetField("newGamePopup",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (popupField != null)
                {
                    var popup = popupField.GetValue(__instance);
                    if (popup != null)
                    {
                        // Get Message property from NewGamePopup
                        var msgProp = popup.GetType().GetProperty("Message",
                            BindingFlags.Public | BindingFlags.Instance);

                        if (msgProp != null)
                        {
                            string message = msgProp.GetValue(popup) as string;
                            if (!string.IsNullOrEmpty(message))
                            {
                                message = TextUtils.StripIconMarkup(message.Trim());
                                MelonLogger.Msg($"[New Game] Start popup: {message}");
                                FFI_ScreenReaderMod.SpeakText(message);
                                return;
                            }
                        }
                    }
                }

                // Fallback
                MelonLogger.Msg("[New Game] Start game confirmation");
                FFI_ScreenReaderMod.SpeakText("Start game?");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitStartPopup_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Name Helpers

        /// <summary>
        /// Gets just the character name for a slot (without "Light Warrior X:" prefix).
        /// </summary>
        private static string GetCharacterSlotNameOnly(object controller, int index)
        {
            try
            {
                // Get ContentList property which has CharacterContentController items
                var listProp = AccessTools.Property(controller.GetType(), "ContentList");
                if (listProp == null)
                {
                    return null;
                }

                var list = listProp.GetValue(controller);
                if (list == null)
                {
                    return null;
                }

                // Get count
                var countProp = list.GetType().GetProperty("Count");
                if (countProp == null || index < 0 || index >= (int)countProp.GetValue(list))
                {
                    return null;
                }

                // Get item at index
                var indexer = list.GetType().GetProperty("Item");
                if (indexer == null)
                {
                    return null;
                }

                var charController = indexer.GetValue(list, new object[] { index });
                if (charController == null)
                {
                    return null;
                }

                // Get CharacterName property from CharacterContentController
                var nameProp = AccessTools.Property(charController.GetType(), "CharacterName");
                if (nameProp != null)
                {
                    string name = nameProp.GetValue(charController) as string;
                    return string.IsNullOrEmpty(name) ? null : name;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the current character name from CurrentData property.
        /// </summary>
        private static string GetCurrentCharacterName(object controller)
        {
            try
            {
                // Get CurrentData property (NewGameSelectData)
                var currentDataProp = controller.GetType().GetProperty("CurrentData",
                    BindingFlags.Public | BindingFlags.Instance);

                if (currentDataProp != null)
                {
                    var currentData = currentDataProp.GetValue(controller);
                    if (currentData != null)
                    {
                        // Get CharacterName from NewGameSelectData
                        var charNameProp = currentData.GetType().GetProperty("CharacterName",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (charNameProp != null)
                        {
                            return charNameProp.GetValue(currentData) as string;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting character name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Gets the current suggested name using GetAutoName method or field access.
        /// </summary>
        private static string GetCurrentSuggestedName(object controller)
        {
            try
            {
                // Try to get autoNameIndex field
                var autoNameIndexField = controller.GetType().GetField("autoNameIndex",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (autoNameIndexField != null)
                {
                    int index = (int)autoNameIndexField.GetValue(controller);
                    AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_AUTO_INDEX, index);
                    return GetAutoNameByIndex(controller, index);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting suggested name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Gets an auto-generated name by index using the GetAutoName method.
        /// </summary>
        private static string GetAutoNameByIndex(object controller, int index)
        {
            try
            {
                // Try calling GetAutoName(int index) method
                var getAutoNameMethod = controller.GetType().GetMethod("GetAutoName",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (getAutoNameMethod != null)
                {
                    var result = getAutoNameMethod.Invoke(controller, new object[] { index });
                    return result as string;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error calling GetAutoName: {ex.Message}");
            }
            return null;
        }

        #endregion

        #region Job/Class Reading (ported from CharacterCreationPatches)

        /// <summary>
        /// Gets the job/class name for the given character index (0-3).
        /// Uses multiple strategies: Characterstatuses -> ContentList.Data -> MasterManager
        /// </summary>
        private static string GetJobName(object listController, int characterIndex)
        {
            try
            {
                // Strategy 1: Get from Characterstatuses list
                var statusesProp = AccessTools.Property(listController.GetType(), "Characterstatuses");
                if (statusesProp != null)
                {
                    var statusesList = statusesProp.GetValue(listController);
                    if (statusesList != null)
                    {
                        var countProp = statusesList.GetType().GetProperty("Count");
                        if (countProp != null)
                        {
                            int count = (int)countProp.GetValue(statusesList);
                            if (characterIndex >= 0 && characterIndex < count)
                            {
                                var indexer = statusesList.GetType().GetProperty("Item");
                                if (indexer != null)
                                {
                                    var characterStatus = indexer.GetValue(statusesList, new object[] { characterIndex });
                                    if (characterStatus != null)
                                    {
                                        var jobIdProp = AccessTools.Property(characterStatus.GetType(), "JobId");
                                        if (jobIdProp != null)
                                        {
                                            int jobId = (int)jobIdProp.GetValue(characterStatus);
                                            MelonLogger.Msg($"[New Game] GetJobName strategy 1: char={characterIndex}, jobId={jobId}");
                                            return GetJobNameById(jobId);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Strategy 2: Get from ContentList -> CharacterContentController.Data
                var contentListProp = AccessTools.Property(listController.GetType(), "ContentList");
                if (contentListProp != null)
                {
                    var contentList = contentListProp.GetValue(listController);
                    if (contentList != null)
                    {
                        var countProp = contentList.GetType().GetProperty("Count");
                        if (countProp != null)
                        {
                            int count = (int)countProp.GetValue(contentList);
                            if (characterIndex >= 0 && characterIndex < count)
                            {
                                var indexer = contentList.GetType().GetProperty("Item");
                                if (indexer != null)
                                {
                                    var charController = indexer.GetValue(contentList, new object[] { characterIndex });
                                    if (charController != null)
                                    {
                                        // Try Data.CharacterStatusId -> master data lookup
                                        var dataProp = AccessTools.Property(charController.GetType(), "Data");
                                        if (dataProp != null)
                                        {
                                            var data = dataProp.GetValue(charController);
                                            if (data != null)
                                            {
                                                var charStatusIdProp = AccessTools.Property(data.GetType(), "CharacterStatusId");
                                                if (charStatusIdProp != null)
                                                {
                                                    int charStatusId = (int)charStatusIdProp.GetValue(data);
                                                    if (charStatusId > 0)
                                                    {
                                                        int jobId = GetJobIdFromMasterData(charStatusId);
                                                        if (jobId > 0)
                                                        {
                                                            MelonLogger.Msg($"[New Game] GetJobName strategy 2: char={characterIndex}, statusId={charStatusId}, jobId={jobId}");
                                                            return GetJobNameById(jobId);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                MelonLogger.Msg($"[New Game] GetJobName: no job found for char={characterIndex}");
                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting job name: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Maps FF1 job ID to job name.
        /// </summary>
        private static string GetJobNameById(int jobId)
        {
            switch (jobId)
            {
                case 1: return "Warrior";
                case 2: return "Thief";
                case 3: return "Monk";
                case 4: return "Red Mage";
                case 5: return "White Mage";
                case 6: return "Black Mage";
                case 7: return "Knight";
                case 8: return "Ninja";
                case 9: return "Master";
                case 10: return "Red Wizard";
                case 11: return "White Wizard";
                case 12: return "Black Wizard";
                default: return $"Job {jobId}";
            }
        }

        /// <summary>
        /// Gets JobId from CharacterStatus master data using MasterManager.
        /// </summary>
        private static int GetJobIdFromMasterData(int characterStatusId)
        {
            try
            {
                // Find MasterManager type
                Type masterManagerType = FindType("Il2CppLast.Data.Master.MasterManager");
                if (masterManagerType == null) return 0;

                // Get MasterManager.Instance
                var instanceProp = AccessTools.Property(masterManagerType, "Instance");
                if (instanceProp == null) return 0;

                var masterManager = instanceProp.GetValue(null);
                if (masterManager == null) return 0;

                // Find CharacterStatus master type
                Type characterStatusType = FindType("Il2CppLast.Data.Master.CharacterStatus");
                if (characterStatusType == null) return 0;

                // Call MasterManager.GetData<CharacterStatus>(characterStatusId)
                var getDataMethod = masterManagerType.GetMethod("GetData");
                if (getDataMethod == null) return 0;

                var genericGetData = getDataMethod.MakeGenericMethod(characterStatusType);
                var characterStatus = genericGetData.Invoke(masterManager, new object[] { characterStatusId });
                if (characterStatus == null) return 0;

                // Get JobId
                var jobIdProp = AccessTools.Property(characterStatus.GetType(), "JobId");
                if (jobIdProp != null)
                {
                    return (int)jobIdProp.GetValue(characterStatus);
                }

                // Try job_id field
                var jobIdField = AccessTools.Field(characterStatus.GetType(), "job_id");
                if (jobIdField != null)
                {
                    return (int)jobIdField.GetValue(characterStatus);
                }

                return 0;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting JobId from master data: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Finds a type by name across all loaded assemblies.
        /// </summary>
        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.FullName == fullName)
                        {
                            return type;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        #endregion
    }
}
