using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using UnityEngine;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Patches for the FF1 character creation screen (New Game).
    /// Handles navigation between Light Warriors and their Name/Class fields.
    /// </summary>
    public static class CharacterCreationPatches
    {
        // Track last announced state to avoid duplicates
        private static int lastCharacterIndex = -1;
        private static int lastCommandId = -1;  // 1=Job/Class, 2=Name
        private static string lastAnnouncement = "";

        // Cached type references
        private static Type newGameControllerType;
        private static Type listControllerType;

        /// <summary>
        /// Flag indicating character creation is handling cursor events.
        /// Used to prevent MenuTextDiscovery from double-reading.
        /// </summary>
        public static bool IsHandlingCursor { get; private set; } = false;

        /// <summary>
        /// Applies character creation patches using manual Harmony patching.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("Applying character creation patches...");

                // Find CharacterContentListController type (KeyInput version)
                listControllerType = FindType("Il2CppLast.UI.KeyInput.CharacterContentListController");
                if (listControllerType == null)
                {
                    MelonLogger.Warning("CharacterContentListController (KeyInput) not found");
                    return;
                }
                MelonLogger.Msg($"Found CharacterContentListController: {listControllerType.FullName}");

                // Patch SelectContent - called when cursor moves to any field
                var selectContentMethod = AccessTools.Method(listControllerType, "SelectContent");
                if (selectContentMethod != null)
                {
                    var postfix = typeof(CharacterCreationPatches).GetMethod("SelectContent_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched CharacterContentListController.SelectContent");
                }
                else
                {
                    MelonLogger.Warning("SelectContent method not found on CharacterContentListController");
                }

                // Find NewGameWindowController for InitNameInput patch
                newGameControllerType = FindType("Il2CppSerial.FF1.UI.KeyInput.NewGameWindowController");
                if (newGameControllerType == null)
                {
                    newGameControllerType = FindType("Il2CppLast.UI.KeyInput.NewGameWindowController");
                }

                if (newGameControllerType != null)
                {
                    MelonLogger.Msg($"Found NewGameWindowController: {newGameControllerType.FullName}");

                    // Patch InitNameInput - keyboard input mode
                    var initNameInputMethod = AccessTools.Method(newGameControllerType, "InitNameInput");
                    if (initNameInputMethod != null)
                    {
                        var postfix = typeof(CharacterCreationPatches).GetMethod("InitNameInput_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(initNameInputMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("Patched NewGameWindowController.InitNameInput");
                    }
                }
                else
                {
                    MelonLogger.Warning("NewGameWindowController not found - keyboard input announcement disabled");
                }

                MelonLogger.Msg("Character creation patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying character creation patches: {ex.Message}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
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

        /// <summary>
        /// Postfix for CharacterContentListController.SelectContent.
        /// Called when cursor moves to any Name or Class field in the character grid.
        /// </summary>
        public static void SelectContent_Postfix(object __instance, object targetCursor)
        {
            IsHandlingCursor = true;

            try
            {
                if (targetCursor == null)
                {
                    MelonLogger.Msg("[CharCreation] targetCursor is null");
                    return;
                }

                // Get cursor index
                var indexProp = AccessTools.Property(targetCursor.GetType(), "Index");
                if (indexProp == null)
                {
                    MelonLogger.Msg("[CharCreation] Cursor.Index property not found");
                    return;
                }

                int cursorIndex = (int)indexProp.GetValue(targetCursor);
                MelonLogger.Msg($"[CharCreation] SelectContent cursor index: {cursorIndex}");

                // Use coroutine to read after UI updates
                CoroutineManager.StartManaged(AnnounceAfterDelay(__instance, cursorIndex));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SelectContent_Postfix: {ex.Message}");
                IsHandlingCursor = false;
            }
        }

        /// <summary>
        /// Coroutine to announce selection after one frame delay for UI to update.
        /// </summary>
        private static IEnumerator AnnounceAfterDelay(object listController, int cursorIndex)
        {
            yield return null; // Wait one frame

            try
            {
                // The grid layout is:
                // Index 0: LW1 Name, Index 1: LW1 Class
                // Index 2: LW2 Name, Index 3: LW2 Class
                // Index 4: LW3 Name, Index 5: LW3 Class
                // Index 6: LW4 Name, Index 7: LW4 Class
                //
                // So: characterIndex = cursorIndex / 2
                //     commandId = (cursorIndex % 2 == 0) ? 2 (Name) : 1 (Class)

                int characterIndex = cursorIndex / 2;
                int commandId = (cursorIndex % 2 == 0) ? 2 : 1;  // 2=Name, 1=Class

                MelonLogger.Msg($"[CharCreation] Calculated: char={characterIndex}, cmd={commandId} (cursorIdx={cursorIndex})");

                // Get the data from the controller
                string characterName = GetCharacterName(listController, characterIndex);
                string jobName = GetJobName(listController, characterIndex);

                MelonLogger.Msg($"[CharCreation] Data: name='{characterName}', job='{jobName}'");

                // Build announcement
                string fieldLabel = commandId == 2 ? "Name" : "Class";
                string fieldValue = commandId == 2 ? (characterName ?? "unnamed") : (jobName ?? "unknown");

                string announcement;

                // Determine if we should include "Light Warrior N:" prefix
                bool characterChanged = (characterIndex != lastCharacterIndex);
                bool fieldChanged = (commandId != lastCommandId);

                if (characterChanged)
                {
                    // Character changed - full announcement
                    announcement = $"Light Warrior {characterIndex + 1}: {fieldLabel}: {fieldValue}";
                }
                else if (fieldChanged)
                {
                    // Only field changed within same character - short announcement
                    announcement = $"{fieldLabel}: {fieldValue}";
                }
                else
                {
                    // Nothing changed - skip
                    MelonLogger.Msg("[CharCreation] No change, skipping announcement");
                    yield break;
                }

                // Skip duplicate announcements
                if (announcement == lastAnnouncement)
                {
                    MelonLogger.Msg($"[CharCreation] Skipping duplicate: {announcement}");
                    yield break;
                }

                // Update tracking state
                lastCharacterIndex = characterIndex;
                lastCommandId = commandId;
                lastAnnouncement = announcement;

                MelonLogger.Msg($"[CharCreation] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in AnnounceAfterDelay: {ex.Message}");
            }
            finally
            {
                IsHandlingCursor = false;
            }
        }

        /// <summary>
        /// Gets the character name for the given character index (0-3).
        /// </summary>
        private static string GetCharacterName(object listController, int characterIndex)
        {
            try
            {
                // Get ContentList property
                var contentListProp = AccessTools.Property(listController.GetType(), "ContentList");
                if (contentListProp == null)
                {
                    MelonLogger.Msg("[CharCreation] ContentList property not found");
                    return null;
                }

                var contentList = contentListProp.GetValue(listController);
                if (contentList == null)
                {
                    MelonLogger.Msg("[CharCreation] ContentList is null");
                    return null;
                }

                // Get count
                var countProp = contentList.GetType().GetProperty("Count");
                if (countProp == null) return null;
                int count = (int)countProp.GetValue(contentList);

                if (characterIndex < 0 || characterIndex >= count)
                {
                    MelonLogger.Msg($"[CharCreation] Character index {characterIndex} out of range (count={count})");
                    return null;
                }

                // Get CharacterContentController at index
                var indexer = contentList.GetType().GetProperty("Item");
                if (indexer == null) return null;

                var charController = indexer.GetValue(contentList, new object[] { characterIndex });
                if (charController == null) return null;

                // Get CharacterName property
                var charNameProp = AccessTools.Property(charController.GetType(), "CharacterName");
                if (charNameProp != null)
                {
                    string name = charNameProp.GetValue(charController) as string;
                    if (!string.IsNullOrEmpty(name))
                    {
                        return name;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting character name: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the job/class name for the given character index (0-3).
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

                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting job name: {ex.Message}");
                return null;
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
        /// Postfix for NewGameWindowController.InitNameInput - keyboard input mode.
        /// </summary>
        public static void InitNameInput_Postfix(object __instance)
        {
            try
            {
                // Get current character name
                string characterName = GetCurrentCharacterNameFromController(__instance);

                string announcement;
                if (!string.IsNullOrEmpty(characterName))
                {
                    announcement = $"Enter name for {characterName}. Type using keyboard.";
                }
                else
                {
                    int displayIndex = lastCharacterIndex >= 0 ? lastCharacterIndex + 1 : 1;
                    announcement = $"Enter name for Light Warrior {displayIndex}. Type using keyboard.";
                }

                MelonLogger.Msg($"[CharCreation] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitNameInput_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current character name from NewGameWindowController.CurrentData.
        /// </summary>
        private static string GetCurrentCharacterNameFromController(object controller)
        {
            try
            {
                // Get CurrentData property
                var currentDataProp = AccessTools.Property(controller.GetType(), "CurrentData");
                if (currentDataProp != null)
                {
                    var currentData = currentDataProp.GetValue(controller);
                    if (currentData != null)
                    {
                        var charNameProp = AccessTools.Property(currentData.GetType(), "CharacterName");
                        if (charNameProp != null)
                        {
                            return charNameProp.GetValue(currentData) as string;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting current character name: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Resets state when leaving character creation.
        /// </summary>
        public static void ResetState()
        {
            lastCharacterIndex = -1;
            lastCommandId = -1;
            lastAnnouncement = "";
            IsHandlingCursor = false;
        }

        /// <summary>
        /// Clears the handling flag. Called by MenuTextDiscovery after skipping a read.
        /// </summary>
        public static void ClearHandlingFlag()
        {
            IsHandlingCursor = false;
        }
    }
}
