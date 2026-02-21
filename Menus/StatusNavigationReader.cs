using System;
using System.Collections.Generic;
using MelonLoader;
using Il2CppLast.Data.User;
using FFI_ScreenReader.Core;

namespace FFI_ScreenReader.Menus
{
    /// <summary>
    /// Handles navigation through status screen stats using arrow keys.
    /// </summary>
    public static class StatusNavigationReader
    {
        private static List<StatusStatDefinition> statList = null;
        private static readonly int[] GroupStartIndices = new int[] { 0, 5, 14, 19 };

        public static void InitializeStatList()
        {
            if (statList != null) return;

            statList = new List<StatusStatDefinition>();

            // Character Info Group (indices 0-4)
            statList.Add(new StatusStatDefinition("Name", StatGroup.CharacterInfo, ReadName));
            statList.Add(new StatusStatDefinition("Job", StatGroup.CharacterInfo, ReadJobName));
            statList.Add(new StatusStatDefinition("Level", StatGroup.CharacterInfo, ReadCharacterLevel));
            statList.Add(new StatusStatDefinition("Experience", StatGroup.CharacterInfo, ReadExperience));
            statList.Add(new StatusStatDefinition("Next Level", StatGroup.CharacterInfo, ReadNextLevel));

            // Vitals Group (indices 5-13): HP, 8 spell charge levels
            statList.Add(new StatusStatDefinition("HP", StatGroup.Vitals, ReadHP));
            statList.Add(new StatusStatDefinition("LV1", StatGroup.Vitals, ReadMPLevel1));
            statList.Add(new StatusStatDefinition("LV2", StatGroup.Vitals, ReadMPLevel2));
            statList.Add(new StatusStatDefinition("LV3", StatGroup.Vitals, ReadMPLevel3));
            statList.Add(new StatusStatDefinition("LV4", StatGroup.Vitals, ReadMPLevel4));
            statList.Add(new StatusStatDefinition("LV5", StatGroup.Vitals, ReadMPLevel5));
            statList.Add(new StatusStatDefinition("LV6", StatGroup.Vitals, ReadMPLevel6));
            statList.Add(new StatusStatDefinition("LV7", StatGroup.Vitals, ReadMPLevel7));
            statList.Add(new StatusStatDefinition("LV8", StatGroup.Vitals, ReadMPLevel8));

            // Attributes Group (indices 14-18)
            statList.Add(new StatusStatDefinition("Strength", StatGroup.Attributes, ReadStrength));
            statList.Add(new StatusStatDefinition("Agility", StatGroup.Attributes, ReadAgility));
            statList.Add(new StatusStatDefinition("Stamina", StatGroup.Attributes, ReadStamina));
            statList.Add(new StatusStatDefinition("Intellect", StatGroup.Attributes, ReadIntellect));
            statList.Add(new StatusStatDefinition("Luck", StatGroup.Attributes, ReadLuck));

            // Combat Stats Group (indices 19-22)
            statList.Add(new StatusStatDefinition("Attack", StatGroup.CombatStats, ReadAttack));
            statList.Add(new StatusStatDefinition("Accuracy", StatGroup.CombatStats, ReadAccuracy));
            statList.Add(new StatusStatDefinition("Defense", StatGroup.CombatStats, ReadDefense));
            statList.Add(new StatusStatDefinition("Evasion", StatGroup.CombatStats, ReadEvasion));
        }

        public static void NavigateNext()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex = (tracker.CurrentStatIndex + 1) % statList.Count;
            ReadCurrentStat();
        }

        public static void NavigatePrevious()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex--;
            if (tracker.CurrentStatIndex < 0)
                tracker.CurrentStatIndex = statList.Count - 1;
            ReadCurrentStat();
        }

        public static void JumpToNextGroup()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            int currentIndex = tracker.CurrentStatIndex;
            int nextGroupIndex = -1;

            for (int i = 0; i < GroupStartIndices.Length; i++)
            {
                if (GroupStartIndices[i] > currentIndex)
                {
                    nextGroupIndex = GroupStartIndices[i];
                    break;
                }
            }

            if (nextGroupIndex == -1)
                nextGroupIndex = GroupStartIndices[0];

            tracker.CurrentStatIndex = nextGroupIndex;
            ReadCurrentStat();
        }

        public static void JumpToPreviousGroup()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            int currentIndex = tracker.CurrentStatIndex;
            int prevGroupIndex = -1;

            for (int i = GroupStartIndices.Length - 1; i >= 0; i--)
            {
                if (GroupStartIndices[i] < currentIndex)
                {
                    prevGroupIndex = GroupStartIndices[i];
                    break;
                }
            }

            if (prevGroupIndex == -1)
                prevGroupIndex = GroupStartIndices[GroupStartIndices.Length - 1];

            tracker.CurrentStatIndex = prevGroupIndex;
            ReadCurrentStat();
        }

        public static void JumpToTop()
        {
            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex = 0;
            ReadCurrentStat();
        }

        public static void JumpToBottom()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex = statList.Count - 1;
            ReadCurrentStat();
        }

        public static void ReadCurrentStat()
        {
            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.ValidateState())
            {
                FFI_ScreenReaderMod.SpeakText("Navigation not available");
                return;
            }

            ReadStatAtIndex(tracker.CurrentStatIndex);
        }

        private static void ReadStatAtIndex(int index)
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;

            if (index < 0 || index >= statList.Count)
            {
                MelonLogger.Warning($"Invalid stat index: {index}");
                return;
            }

            if (tracker.CurrentCharacterData == null)
            {
                FFI_ScreenReaderMod.SpeakText("No character data");
                return;
            }

            try
            {
                var stat = statList[index];
                string value = stat.Reader(tracker.CurrentCharacterData);
                FFI_ScreenReaderMod.SpeakText(value, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading stat at index {index}: {ex.Message}");
                FFI_ScreenReaderMod.SpeakText("Error reading stat");
            }
        }

        #region Character Info Readers

        private static string ReadName(OwnedCharacterData data)
        {
            try
            {
                if (data == null) return "N/A";
                string name = data.Name;
                return !string.IsNullOrWhiteSpace(name) ? $"Name: {name}" : "N/A";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading name: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadJobName(OwnedCharacterData data)
        {
            try
            {
                if (data == null) return "N/A";

                string jobName = Patches.StatusMenuHelpers.GetCurrentJobName(data);
                if (!string.IsNullOrWhiteSpace(jobName))
                    return $"Job: {jobName}";

                return $"Job: ID {data.JobId}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading job name: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadCharacterLevel(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Level: {data.Parameter.ConfirmedLevel()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading character level: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadExperience(OwnedCharacterData data)
        {
            try
            {
                if (data == null) return "N/A";
                return $"Experience: {data.CurrentExp}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Experience: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadNextLevel(OwnedCharacterData data)
        {
            try
            {
                if (data == null) return "N/A";
                return $"Next Level: {data.GetNextExp()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Next Level: {ex.Message}");
                return "N/A";
            }
        }

        #endregion

        #region Vitals Readers

        private static string ReadHP(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"HP: {data.Parameter.currentHP} / {data.Parameter.ConfirmedMaxHp()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading HP: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadMPLevelN(OwnedCharacterData data, int level)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";

                var currentCharges = data.Parameter.CurrentMpCountList;
                int current = 0;
                if (currentCharges != null && currentCharges.ContainsKey(level))
                    current = currentCharges[level];
                return $"LV{level}: {current}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading MP level {level}: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadMPLevel1(OwnedCharacterData data) => ReadMPLevelN(data, 1);
        private static string ReadMPLevel2(OwnedCharacterData data) => ReadMPLevelN(data, 2);
        private static string ReadMPLevel3(OwnedCharacterData data) => ReadMPLevelN(data, 3);
        private static string ReadMPLevel4(OwnedCharacterData data) => ReadMPLevelN(data, 4);
        private static string ReadMPLevel5(OwnedCharacterData data) => ReadMPLevelN(data, 5);
        private static string ReadMPLevel6(OwnedCharacterData data) => ReadMPLevelN(data, 6);
        private static string ReadMPLevel7(OwnedCharacterData data) => ReadMPLevelN(data, 7);
        private static string ReadMPLevel8(OwnedCharacterData data) => ReadMPLevelN(data, 8);

        #endregion

        #region Attribute Readers

        private static string ReadStrength(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Strength: {data.Parameter.ConfirmedPower()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Strength: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadAgility(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Agility: {data.Parameter.ConfirmedAgility()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Agility: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadStamina(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Stamina: {data.Parameter.ConfirmedVitality()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Stamina: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadIntellect(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Intellect: {data.Parameter.ConfirmedIntelligence()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Intellect: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadLuck(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Luck: {data.Parameter.ConfirmedLuck()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Luck: {ex.Message}");
                return "N/A";
            }
        }

        #endregion

        #region Combat Stat Readers

        private static string ReadAttack(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Attack: {data.Parameter.ConfirmedAttack()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Attack: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadAccuracy(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Accuracy: {data.Parameter.ConfirmedAccuracyRate(false)}%";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Accuracy: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadDefense(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Defense: {data.Parameter.ConfirmedDefense()}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Defense: {ex.Message}");
                return "N/A";
            }
        }

        private static string ReadEvasion(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return "N/A";
                return $"Evasion: {data.Parameter.ConfirmedEvasionRate(false)}%";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Evasion: {ex.Message}");
                return "N/A";
            }
        }

        #endregion
    }
}
