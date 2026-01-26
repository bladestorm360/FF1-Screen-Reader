using System;
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
    /// Patches for new game character creation (Light Warrior naming).
    /// Port of FF3's NewGameNamingPatches with FF1-specific changes.
    ///
    /// Patches:
    /// - InitSelect: Entering character slot selection mode
    /// - SetTargetSelectContent: Character slot navigation (event-driven)
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

        // Track controller for cross-method access
        private static object currentController = null;

        // Track last target index for UpdateView (more reliable than reading from instance)
        private static int lastTargetIndex = -1;

        // Track last slot names for change detection
        private static string[] lastSlotNames = new string[4];

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[New Game] Applying new game naming patches...");

                Type controllerType = typeof(NewGameWindowController);
                Type charListType = typeof(CharacterContentListController);
                Type nameListType = typeof(NameContentListController);

                // InitSelect - entering character selection mode
                PatchMethod(harmony, controllerType, "InitSelect", nameof(InitSelect_Postfix));

                // SetTargetSelectContent - character slot navigation (event-driven)
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

        /// <summary>
        /// Postfix for InitSelect - announces entering character selection mode.
        /// Stores controller reference for other patches.
        /// </summary>
        public static void InitSelect_Postfix(object __instance)
        {
            try
            {
                currentController = __instance;

                // Reset tracking state
                lastTargetIndex = -1;
                for (int i = 0; i < lastSlotNames.Length; i++)
                    lastSlotNames[i] = null;

                AnnouncementDeduplicator.Reset(CONTEXT_SLOT, CONTEXT_NAME, CONTEXT_AUTO_INDEX);

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
        /// Fires once when cursor moves to a new character slot.
        /// Announces slot info: "Light Warrior {n}: {name or unnamed}" or "Done"
        /// </summary>
        public static void SetTargetSelectContent_Postfix(int index)
        {
            try
            {
                // Track for UpdateView
                lastTargetIndex = index;

                // Check if index changed (deduplication)
                if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_SLOT, index))
                {
                    return;
                }

                // Get slot info
                string slotInfo = GetCharacterSlotInfo(currentController, index);

                if (!string.IsNullOrEmpty(slotInfo) &&
                    AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_NAME, slotInfo))
                {
                    MelonLogger.Msg($"[New Game] Slot: {slotInfo}");
                    FFI_ScreenReaderMod.SpeakText(slotInfo, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetTargetSelectContent_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets character slot info from the controller.
        /// Returns "Light Warrior {n}: {name}" or "Light Warrior {n}: unnamed" or "Done"
        /// </summary>
        private static string GetCharacterSlotInfo(object controller, int index)
        {
            if (controller == null)
            {
                return index >= 4 ? "Done" : $"Light Warrior {index + 1}: unnamed";
            }

            int displayIndex = index + 1;

            try
            {
                // Get SelectedDataList property
                var listProp = AccessTools.Property(controller.GetType(), "SelectedDataList");
                if (listProp == null)
                {
                    return $"Light Warrior {displayIndex}: unnamed";
                }

                var list = listProp.GetValue(controller);
                if (list == null)
                {
                    return $"Light Warrior {displayIndex}: unnamed";
                }

                // Get count and item at index
                var countProp = list.GetType().GetProperty("Count");
                if (countProp == null)
                {
                    return $"Light Warrior {displayIndex}: unnamed";
                }

                int count = (int)countProp.GetValue(list);
                if (index < 0 || index >= count)
                {
                    // Index beyond character list is the Done button
                    return index >= count ? "Done" : null;
                }

                // Get item at index using indexer
                var indexer = list.GetType().GetProperty("Item");
                if (indexer == null)
                {
                    return $"Light Warrior {displayIndex}: unnamed";
                }

                var item = indexer.GetValue(list, new object[] { index });
                if (item == null)
                {
                    return $"Light Warrior {displayIndex}: unnamed";
                }

                // Get CharacterName from NewGameSelectData
                var nameProp = AccessTools.Property(item.GetType(), "CharacterName");
                if (nameProp != null)
                {
                    string name = nameProp.GetValue(item) as string;
                    if (string.IsNullOrEmpty(name))
                    {
                        return $"Light Warrior {displayIndex}: unnamed";
                    }
                    return $"Light Warrior {displayIndex}: {name}";
                }

                return $"Light Warrior {displayIndex}: unnamed";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting character slot info: {ex.Message}");
                return $"Light Warrior {displayIndex}: unnamed";
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
                // Use lastTargetIndex set by SetTargetSelectContent_Postfix
                // (reading from instance offset was unreliable)
                int currentSlot = lastTargetIndex;

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
    }
}
