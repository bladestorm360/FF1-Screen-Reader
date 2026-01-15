using System;
using System.Collections.Generic;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Patches;

// Type aliases for IL2CPP types
using MasterManager = Il2CppLast.Data.Master.MasterManager;
using MessageManager = Il2CppLast.Management.MessageManager;
using UserDataManager = Il2CppLast.Management.UserDataManager;
using Weapon = Il2CppLast.Data.Master.Weapon;
using Armor = Il2CppLast.Data.Master.Armor;
using Job = Il2CppLast.Data.Master.Job;
using JobGroup = Il2CppLast.Data.Master.JobGroup;
using ContentType = Il2CppLast.Defaine.Content.ContentType;

namespace FFI_ScreenReader.Menus
{
    /// <summary>
    /// Announces equipment job requirements when 'I' key is pressed in Items menu.
    /// Only works for equipment (weapons/armor), silent for consumables/key items.
    /// </summary>
    public static class ItemDetailsAnnouncer
    {
        // ContentType values from dump.cs
        private const int CONTENT_TYPE_WEAPON = 2;
        private const int CONTENT_TYPE_ARMOR = 3;

        /// <summary>
        /// Announces which jobs can equip the currently selected item.
        /// Only announces for weapons and armor, silent for other items.
        /// </summary>
        public static void AnnounceEquipRequirements()
        {
            try
            {
                var itemData = ItemMenuState.LastSelectedItem;
                if (itemData == null)
                {
                    MelonLogger.Msg("[ItemDetails] No item selected");
                    return;
                }

                int itemType = itemData.ItemType;
                int itemId = itemData.ItemId;

                MelonLogger.Msg($"[ItemDetails] Checking item: Type={itemType}, Id={itemId}");

                // Only process equipment (weapons and armor)
                if (itemType != CONTENT_TYPE_WEAPON && itemType != CONTENT_TYPE_ARMOR)
                {
                    MelonLogger.Msg("[ItemDetails] Not equipment, ignoring");
                    return;
                }

                var masterManager = MasterManager.Instance;
                if (masterManager == null)
                {
                    MelonLogger.Warning("[ItemDetails] MasterManager not available");
                    return;
                }

                // Get EquipJobGroupId based on item type
                int equipJobGroupId = GetEquipJobGroupId(masterManager, itemType, itemId);
                if (equipJobGroupId <= 0)
                {
                    MelonLogger.Msg($"[ItemDetails] No EquipJobGroupId found");
                    return;
                }

                MelonLogger.Msg($"[ItemDetails] EquipJobGroupId = {equipJobGroupId}");

                // Get the JobGroup data
                var jobGroup = masterManager.GetData<JobGroup>(equipJobGroupId);
                if (jobGroup == null)
                {
                    MelonLogger.Msg($"[ItemDetails] JobGroup {equipJobGroupId} not found");
                    return;
                }

                // Get unlocked jobs from UserDataManager
                var unlockedJobIds = GetUnlockedJobIds();
                MelonLogger.Msg($"[ItemDetails] Unlocked jobs count: {unlockedJobIds.Count}");

                // Build list of jobs that can equip (filtered by unlocked)
                var canEquipJobs = GetEquippableJobs(masterManager, jobGroup, unlockedJobIds);

                // Build and announce the result
                string announcement = BuildAnnouncement(canEquipJobs);
                if (!string.IsNullOrEmpty(announcement))
                {
                    MelonLogger.Msg($"[ItemDetails] {announcement}");
                    FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemDetails] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the EquipJobGroupId from weapon or armor master data.
        /// </summary>
        private static int GetEquipJobGroupId(MasterManager masterManager, int itemType, int itemId)
        {
            try
            {
                if (itemType == CONTENT_TYPE_WEAPON)
                {
                    var weapon = masterManager.GetData<Weapon>(itemId);
                    if (weapon != null)
                    {
                        return weapon.EquipJobGroupId;
                    }
                }
                else if (itemType == CONTENT_TYPE_ARMOR)
                {
                    var armor = masterManager.GetData<Armor>(itemId);
                    if (armor != null)
                    {
                        return armor.EquipJobGroupId;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemDetails] Error getting EquipJobGroupId: {ex.Message}");
            }

            return 0;
        }

        /// <summary>
        /// Gets the set of job IDs that have been unlocked (released) by the player.
        /// FF1 starts with 6 base jobs, class change unlocks upgraded versions.
        /// </summary>
        private static HashSet<int> GetUnlockedJobIds()
        {
            var unlockedIds = new HashSet<int>();

            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                {
                    MelonLogger.Warning("[ItemDetails] UserDataManager not available");
                    return unlockedIds;
                }

                var releasedJobs = userDataManager.ReleasedJobs;
                if (releasedJobs == null)
                {
                    MelonLogger.Warning("[ItemDetails] ReleasedJobs is null");
                    return unlockedIds;
                }

                foreach (var job in releasedJobs)
                {
                    if (job != null)
                    {
                        unlockedIds.Add(job.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemDetails] Error getting unlocked jobs: {ex.Message}");
            }

            return unlockedIds;
        }

        /// <summary>
        /// Gets list of job names that can equip based on JobGroup accept flags.
        /// Only includes jobs that are unlocked (released) to avoid spoilers.
        /// FF1 has 12 jobs (6 base + 6 upgraded after class change).
        /// </summary>
        private static List<string> GetEquippableJobs(MasterManager masterManager, JobGroup jobGroup, HashSet<int> unlockedJobIds)
        {
            var jobNames = new List<string>();
            var messageManager = MessageManager.Instance;

            // Check each job using the JobNAccept properties
            // JobGroup has Job1Accept through Job22Accept properties (though FF1 only uses 1-12)
            int[] acceptFlags = new int[]
            {
                jobGroup.Job1Accept,
                jobGroup.Job2Accept,
                jobGroup.Job3Accept,
                jobGroup.Job4Accept,
                jobGroup.Job5Accept,
                jobGroup.Job6Accept,
                jobGroup.Job7Accept,
                jobGroup.Job8Accept,
                jobGroup.Job9Accept,
                jobGroup.Job10Accept,
                jobGroup.Job11Accept,
                jobGroup.Job12Accept,
                jobGroup.Job13Accept,
                jobGroup.Job14Accept,
                jobGroup.Job15Accept,
                jobGroup.Job16Accept,
                jobGroup.Job17Accept,
                jobGroup.Job18Accept,
                jobGroup.Job19Accept,
                jobGroup.Job20Accept,
                jobGroup.Job21Accept,
                jobGroup.Job22Accept
            };

            for (int i = 0; i < acceptFlags.Length; i++)
            {
                int jobId = i + 1;  // Job IDs are 1-indexed

                // Only include if: job can equip this item AND job is unlocked
                if (acceptFlags[i] != 0 && unlockedJobIds.Contains(jobId))
                {
                    string jobName = GetJobName(masterManager, messageManager, jobId);
                    if (!string.IsNullOrEmpty(jobName))
                    {
                        jobNames.Add(jobName);
                    }
                }
            }

            return jobNames;
        }

        /// <summary>
        /// Gets the localized name for a job.
        /// </summary>
        private static string GetJobName(MasterManager masterManager, MessageManager messageManager, int jobId)
        {
            try
            {
                var job = masterManager.GetData<Job>(jobId);
                if (job == null)
                    return null;

                string mesIdName = job.MesIdName;
                if (string.IsNullOrEmpty(mesIdName))
                    return null;

                if (messageManager != null)
                {
                    string localizedName = messageManager.GetMessage(mesIdName, false);
                    if (!string.IsNullOrWhiteSpace(localizedName))
                        return localizedName;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Builds the announcement string from the list of equippable jobs.
        /// </summary>
        private static string BuildAnnouncement(List<string> jobNames)
        {
            if (jobNames == null || jobNames.Count == 0)
            {
                return "No unlocked jobs can equip";
            }

            return "Can equip: " + string.Join(", ", jobNames);
        }
    }
}
