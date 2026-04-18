using System;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Patches;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

using MasterManager = Il2CppLast.Data.Master.MasterManager;
using MessageManager = Il2CppLast.Management.MessageManager;
using JobGroup = Il2CppLast.Data.Master.JobGroup;
using Content = Il2CppLast.Data.Master.Content;

namespace FFI_ScreenReader.Menus
{
    /// <summary>
    /// Announces which character classes can equip the currently-focused weapon/armor.
    /// Triggered by the U key (or right-stick-left on gamepad).
    /// Works in shops (looks up master data by item name) and inventory (delegates to ItemDetailsAnnouncer).
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
        /// Returns null if the item isn't equipment or can't be resolved by name.
        /// </summary>
        internal static string BuildShopItemAnnouncement()
        {
            string itemName = ShopMenuTracker.LastItemName;
            if (string.IsNullOrEmpty(itemName))
                return null;

            var masterManager = MasterManager.Instance;
            if (masterManager == null)
                return null;

            if (!TryResolveContent(masterManager, itemName, out int itemType, out int itemId))
                return null;
            if (itemType != CONTENT_TYPE_WEAPON && itemType != CONTENT_TYPE_ARMOR)
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

        /// <summary>
        /// Iterates the Content master list to resolve a display name into (TypeId, TypeValue).
        /// Content is the unified item table: TypeId is the ContentType (2=weapon, 3=armor, ...),
        /// TypeValue is the ID within the corresponding Weapon/Armor/Item master list.
        /// Matching by display name bypasses the unreliable ShopListItemContentController.ContentId.
        /// </summary>
        private static bool TryResolveContent(MasterManager masterManager, string itemName, out int itemType, out int itemId)
        {
            itemType = -1;
            itemId = 0;

            var messageManager = MessageManager.Instance;
            if (messageManager == null)
                return false;

            string target = TextUtils.StripIconMarkup(itemName)?.Trim();
            if (string.IsNullOrEmpty(target))
                return false;

            try
            {
                var contents = masterManager.GetList<Content>();
                if (contents == null)
                    return false;

                foreach (var kvp in contents)
                {
                    var content = kvp.Value;
                    if (content == null) continue;
                    string mes = content.MesIdName;
                    if (string.IsNullOrEmpty(mes)) continue;
                    string localized = messageManager.GetMessage(mes, false);
                    if (string.IsNullOrEmpty(localized)) continue;
                    if (string.Equals(TextUtils.StripIconMarkup(localized).Trim(), target, StringComparison.Ordinal))
                    {
                        itemType = content.TypeId;
                        itemId = content.TypeValue;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[UsableBy] Content scan failed: {ex.Message}");
            }

            return false;
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
