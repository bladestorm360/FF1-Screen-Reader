using System;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Patches;
using static FFI_ScreenReader.Utils.ModTextTranslator;

using MasterManager = Il2CppLast.Data.Master.MasterManager;
using JobGroup = Il2CppLast.Data.Master.JobGroup;

namespace FFI_ScreenReader.Menus
{
    /// <summary>
    /// Announces which character classes can equip the currently-focused weapon/armor.
    /// Triggered by the U key (or right-stick-left on gamepad).
    /// Works in shops (reads ShopMenuTracker.LastItemContent*) and inventory (delegates to ItemDetailsAnnouncer).
    /// </summary>
    public static class UsableByAnnouncer
    {
        private const int CONTENT_TYPE_WEAPON = 2;
        private const int CONTENT_TYPE_ARMOR = 3;

        public static void AnnounceForCurrentContext()
        {
            try
            {
                if (ShopMenuTracker.ValidateState())
                {
                    AnnounceShopItem();
                    return;
                }

                if (ItemMenuState.IsItemMenuActive)
                {
                    // Same logic/output as before — just driven by U instead of I
                    ItemDetailsAnnouncer.AnnounceEquipRequirements();
                    return;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[UsableBy] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the "Can equip: ..." string for the focused shop item.
        /// Returns null if the item isn't equipment or data can't be resolved.
        /// </summary>
        internal static string BuildShopItemAnnouncement()
        {
            int itemType = ShopMenuTracker.LastItemContentType;
            int itemId = ShopMenuTracker.LastItemContentId;

            if (itemType != CONTENT_TYPE_WEAPON && itemType != CONTENT_TYPE_ARMOR)
                return null;
            if (itemId <= 0)
                return null;

            var masterManager = MasterManager.Instance;
            if (masterManager == null)
                return null;

            int equipJobGroupId = ItemDetailsAnnouncer.GetEquipJobGroupId(masterManager, itemType, itemId);
            if (equipJobGroupId <= 0)
                return null;

            var jobGroup = masterManager.GetData<JobGroup>(equipJobGroupId);
            if (jobGroup == null)
                return null;

            var unlockedJobIds = ItemDetailsAnnouncer.GetUnlockedJobIds();
            var canEquipJobs = ItemDetailsAnnouncer.GetEquippableJobs(masterManager, jobGroup, unlockedJobIds);
            return ItemDetailsAnnouncer.BuildAnnouncement(canEquipJobs);
        }

        private static void AnnounceShopItem()
        {
            string announcement = BuildShopItemAnnouncement();
            if (string.IsNullOrEmpty(announcement))
                return;
            FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
        }
    }
}
