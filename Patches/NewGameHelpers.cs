using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Helper methods for new game character creation.
    /// Grid decoding, name/job reading via reflection.
    /// </summary>
    internal static class NewGameHelpers
    {
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
        /// </summary>
        internal static void DecodeGridIndex(int cursorIndex, out int characterIndex, out bool isClassField, out bool isDone)
        {
            if (cursorIndex >= 8)
            {
                characterIndex = -1;
                isClassField = false;
                isDone = true;
                return;
            }

            characterIndex = cursorIndex / 2;
            isClassField = cursorIndex % 2 == 1;
            isDone = false;
        }

        #endregion

        #region Name Helpers

        /// <summary>
        /// Gets just the character name for a slot (without "Light Warrior X:" prefix).
        /// </summary>
        internal static string GetCharacterSlotNameOnly(object controller, int index)
        {
            try
            {
                var listProp = AccessTools.Property(controller.GetType(), "ContentList");
                if (listProp == null)
                    return null;

                var list = listProp.GetValue(controller);
                if (list == null)
                    return null;

                var countProp = list.GetType().GetProperty("Count");
                if (countProp == null || index < 0 || index >= (int)countProp.GetValue(list))
                    return null;

                var indexer = list.GetType().GetProperty("Item");
                if (indexer == null)
                    return null;

                var charController = indexer.GetValue(list, new object[] { index });
                if (charController == null)
                    return null;

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
                return null; // Reflection on IL2CPP types may fail
            }
        }

        /// <summary>
        /// Gets the current character name from CurrentData property.
        /// </summary>
        internal static string GetCurrentCharacterName(object controller)
        {
            try
            {
                var currentDataProp = controller.GetType().GetProperty("CurrentData",
                    BindingFlags.Public | BindingFlags.Instance);

                if (currentDataProp != null)
                {
                    var currentData = currentDataProp.GetValue(controller);
                    if (currentData != null)
                    {
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
        internal static string GetCurrentSuggestedName(object controller)
        {
            try
            {
                var autoNameIndexField = controller.GetType().GetField("autoNameIndex",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (autoNameIndexField != null)
                {
                    int index = (int)autoNameIndexField.GetValue(controller);
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
        internal static string GetAutoNameByIndex(object controller, int index)
        {
            try
            {
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

        #region Job/Class Reading

        /// <summary>
        /// Gets the job/class name for the given character index (0-3).
        /// Uses multiple strategies: Characterstatuses -> ContentList.Data -> MasterManager
        /// </summary>
        internal static string GetJobName(object listController, int characterIndex)
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
        /// Maps FF1 job ID to job name.
        /// </summary>
        internal static string GetJobNameById(int jobId)
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

        private static int GetJobIdFromMasterData(int characterStatusId)
        {
            try
            {
                Type masterManagerType = FindType("Il2CppLast.Data.Master.MasterManager");
                if (masterManagerType == null) return 0;

                var instanceProp = AccessTools.Property(masterManagerType, "Instance");
                if (instanceProp == null) return 0;

                var masterManager = instanceProp.GetValue(null);
                if (masterManager == null) return 0;

                Type characterStatusType = FindType("Il2CppLast.Data.Master.CharacterStatus");
                if (characterStatusType == null) return 0;

                var getDataMethod = masterManagerType.GetMethod("GetData");
                if (getDataMethod == null) return 0;

                var genericGetData = getDataMethod.MakeGenericMethod(characterStatusType);
                var characterStatus = genericGetData.Invoke(masterManager, new object[] { characterStatusId });
                if (characterStatus == null) return 0;

                var jobIdProp = AccessTools.Property(characterStatus.GetType(), "JobId");
                if (jobIdProp != null)
                {
                    return (int)jobIdProp.GetValue(characterStatus);
                }

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
                catch { } // Assembly may throw on GetTypes
            }
            return null;
        }

        #endregion
    }
}
