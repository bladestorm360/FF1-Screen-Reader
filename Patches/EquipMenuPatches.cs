using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using Il2CppLast.Management;

// Type aliases for IL2CPP types
using KeyInputEquipmentInfoWindowController = Il2CppLast.UI.KeyInput.EquipmentInfoWindowController;
using KeyInputEquipmentSelectWindowController = Il2CppLast.UI.KeyInput.EquipmentSelectWindowController;
using KeyInputEquipmentWindowController = Il2CppLast.UI.KeyInput.EquipmentWindowController;
using EquipmentInfoContentView = Il2CppLast.UI.KeyInput.EquipmentInfoContentView;
using EquipSlotType = Il2CppLast.Defaine.EquipSlotType;
using EquipUtility = Il2CppLast.Systems.EquipUtility;
using GameCursor = Il2CppLast.UI.Cursor;
using CustomScrollViewWithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;
using EquipmentCommandController = Il2CppLast.UI.KeyInput.EquipmentCommandController;
using EquipmentCommandId = Il2CppLast.UI.EquipmentCommandId;
using EquipmentDescriptionWindowController = Il2CppLast.UI.KeyInput.EquipmentDescriptionWindowController;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Helper for equipment menu announcements.
    /// </summary>
    public static class EquipMenuState
    {
        /// <summary>
        /// True when equipment menu is active and handling announcements.
        /// </summary>
        public static bool IsActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.EQUIP_MENU);
            set => MenuStateRegistry.SetActive(MenuStateRegistry.EQUIP_MENU, value);
        }

        /// <summary>
        /// True when equipment menu was entered from shop menu.
        /// Used to restore shop state when equipment menu closes.
        /// </summary>
        public static bool EnteredFromShop { get; set; } = false;

        /// <summary>
        /// Last target index (party member). Used to detect tab switches in SelectContent
        /// (event-driven: only fires when the game sends a cursor event).
        /// Changes only on RB/LB, not on slot navigation.
        /// </summary>
        public static int LastTargetIndex { get; set; } = -1;

        static EquipMenuState()
        {
            MenuStateRegistry.RegisterResetHandler(MenuStateRegistry.EQUIP_MENU, () =>
            {
                EnteredFromShop = false;
                LastTargetIndex = -1;
                AnnouncementDeduplicator.Reset(AnnouncementContexts.EQUIP_SELECT);
            });
        }

        // State machine offset - use centralized offsets
        private const int OFFSET_STATE_MACHINE = IL2CppOffsets.MenuStateMachine.Equip;

        // EquipmentWindowController.State values (from dump.cs)
        private const int STATE_NONE = 0;
        private const int STATE_COMMAND = 1;   // Command bar (Equip/Optimal/Remove All)
        private const int STATE_INFO = 2;      // Equipment slot selection
        private const int STATE_SELECT = 3;    // Equipment item selection

        /// <summary>
        /// Validates that equipment menu is active and should suppress generic cursor.
        /// Uses dual validation like MagicMenuPatches:
        /// 1. Check state machine - if COMMAND, don't suppress
        /// 2. Check if actual list controllers are active
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive) return false;

            try
            {
                // Check state machine first - if in COMMAND state, don't suppress
                var equipmentController = UnityEngine.Object.FindObjectOfType<KeyInputEquipmentWindowController>();
                if (equipmentController != null)
                {
                    int currentState = StateMachineHelper.ReadState(equipmentController.Pointer, OFFSET_STATE_MACHINE);

                    // STATE_COMMAND means we're in command bar - let MenuTextDiscovery handle
                    if (currentState == STATE_COMMAND)
                    {
                        ClearState();
                        return false;
                    }

                    // STATE_NONE means menu closing
                    if (currentState == STATE_NONE)
                    {
                        ClearState();
                        return false;
                    }
                }

                // Validate the actual list controllers are active (not just parent)
                var infoController = UnityEngine.Object.FindObjectOfType<KeyInputEquipmentInfoWindowController>();
                var selectController = UnityEngine.Object.FindObjectOfType<KeyInputEquipmentSelectWindowController>();

                bool infoActive = infoController != null && infoController.gameObject.activeInHierarchy;
                bool selectActive = selectController != null && selectController.gameObject.activeInHierarchy;

                if (!infoActive && !selectActive)
                {
                    // Neither list controller is active - we've left the equipment submenu
                    ClearState();
                    return false;
                }

                // At least one list is active - suppress
                return true;
            }
            catch
            { // State check failed; assume menu closed
                ClearState();
                return false;
            }
        }

        /// <summary>
        /// Clears equipment menu state.
        /// </summary>
        public static void ClearState()
        {
            IsActive = false;
        }

        /// <summary>
        /// Alias for ClearState() for consistency with other state classes.
        /// </summary>
        public static void ResetState() => ClearState();

        /// <summary>
        /// Check if announcement should be made (string-only deduplication).
        /// </summary>
        public static bool ShouldAnnounce(string announcement) => AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.EQUIP_SELECT, announcement);

        public static string GetSlotName(EquipSlotType slot)
        {
            try
            {
                string messageId = EquipUtility.GetSlotMessageId(slot);
                if (!string.IsNullOrEmpty(messageId))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedName = messageManager.GetMessage(messageId);
                        if (!string.IsNullOrWhiteSpace(localizedName))
                            return localizedName;
                    }
                }
            }
            catch
            {
                // Fall through to defaults
            }

            // Fallback to English slot names
            return slot switch
            {
                EquipSlotType.Slot1 => "Right Hand",
                EquipSlotType.Slot2 => "Left Hand",
                EquipSlotType.Slot3 => "Head",
                EquipSlotType.Slot4 => "Body",
                EquipSlotType.Slot5 => "Accessory",
                EquipSlotType.Slot6 => "Accessory 2",
                _ => $"Slot {(int)slot}"
            };
        }
    }

    /// <summary>
    /// Patch for equipment slot selection (Menu 2).
    /// EquipmentInfoWindowController.SelectContent is called when navigating between equipment slots.
    /// Uses same approach as FF5 - direct contentList access.
    /// </summary>
    [HarmonyPatch(typeof(KeyInputEquipmentInfoWindowController), "SelectContent", new Type[] { typeof(GameCursor) })]
    public static class EquipmentInfoWindowController_SelectContent_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(KeyInputEquipmentInfoWindowController __instance, GameCursor targetCursor)
        {
            try
            {
                // NOTE: Don't set IsActive here - wait until after validation
                // Setting it early causes suppression during menu transitions

                if (targetCursor == null) return;

                int index = targetCursor.Index;

                // Access contentList directly (IL2CppInterop exposes private fields)
                var contentList = __instance.contentList;
                if (contentList == null || contentList.Count == 0)
                {
                    MelonLogger.Warning("[Equipment Slot] contentList is null or empty");
                    return;
                }

                if (index < 0 || index >= contentList.Count)
                {
                    MelonLogger.Warning($"[Equipment Slot] Index {index} out of range (count={contentList.Count})");
                    return;
                }

                var contentView = contentList[index];
                if (contentView == null)
                {
                    MelonLogger.Warning("[Equipment Slot] Content view is null");
                    return;
                }

                // Get slot name from partText (like FF5)
                string slotName = null;
                if (contentView.partText != null)
                {
                    slotName = contentView.partText.text;
                }

                // Fallback to localized slot name if partText is empty
                if (string.IsNullOrWhiteSpace(slotName))
                {
                    EquipSlotType slotType = contentView.Slot;
                    slotName = EquipMenuState.GetSlotName(slotType);
                }

                // Get equipped item name from Data property (stats gated behind I key)
                string equippedItem = null;
                var itemData = contentView.Data;
                if (itemData != null)
                {
                    try
                    {
                        equippedItem = itemData.Name;
                    }
                    catch { } // IL2CPP item data may not resolve
                }

                // Build announcement
                string announcement = "";
                if (!string.IsNullOrWhiteSpace(slotName))
                {
                    announcement = slotName;
                }

                if (!string.IsNullOrWhiteSpace(equippedItem))
                {
                    if (!string.IsNullOrWhiteSpace(announcement))
                    {
                        announcement += ": " + equippedItem;
                    }
                    else
                    {
                        announcement = equippedItem;
                    }
                }
                else
                {
                    announcement += ": Empty";
                }

                if (string.IsNullOrWhiteSpace(announcement))
                {
                    return;
                }

                // Strip icon markup (e.g., <ic_knife>)
                announcement = TextUtils.StripIconMarkup(announcement);

                // AutoDetail: append stats/description for the equipped item
                if (FFI_ScreenReaderMod.AutoDetailEnabled)
                {
                    string detail = null;
                    try { detail = EquipDetailsAnnouncer.GetDescriptionFromUI(); } catch { }
                    if (!string.IsNullOrWhiteSpace(detail))
                        announcement = $"{announcement}: {detail}";
                }

                // Skip duplicates
                if (!EquipMenuState.ShouldAnnounce(announcement))
                    return;

                // Capture shop context BEFORE clearing states (for restoration when equipment menu closes)
                if (!EquipMenuState.IsActive)
                {
                    EquipMenuState.EnteredFromShop = ShopMenuTracker.IsShopMenuActive;
                }

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                // Also clear other menu states to prevent conflicts
                FFI_ScreenReaderMod.ClearOtherMenuStates("Equip");
                EquipMenuState.IsActive = true;

                // Detect character switch via targetIndex (int, changes only on RB/LB)
                bool characterSwitched = false;
                try
                {
                    IntPtr ctrlPtr = __instance.Pointer;
                    if (ctrlPtr != IntPtr.Zero)
                    {
                        int targetIndex = IL2CppFieldReader.ReadInt32(ctrlPtr, IL2CppOffsets.Equipment.TargetIndex);
                        if (targetIndex != EquipMenuState.LastTargetIndex)
                        {
                            characterSwitched = EquipMenuState.LastTargetIndex >= 0;
                            EquipMenuState.LastTargetIndex = targetIndex;

                            if (characterSwitched)
                            {
                                // Reset dedup so slot re-announces for new character
                                AnnouncementDeduplicator.Reset(AnnouncementContexts.EQUIP_SELECT);

                                // Read character name/job from view
                                IntPtr viewPtr = IL2CppFieldReader.ReadPointerSafe(ctrlPtr, IL2CppOffsets.Equipment.InfoView);
                                if (viewPtr != IntPtr.Zero)
                                {
                                    string charName = null;
                                    string jobName = null;

                                    IntPtr nameTextPtr = IL2CppFieldReader.ReadPointerSafe(viewPtr, IL2CppOffsets.Equipment.NameText);
                                    if (nameTextPtr != IntPtr.Zero)
                                        charName = new UnityEngine.UI.Text(nameTextPtr)?.text;

                                    IntPtr jobTextPtr = IL2CppFieldReader.ReadPointerSafe(viewPtr, IL2CppOffsets.Equipment.JobNameText);
                                    if (jobTextPtr != IntPtr.Zero)
                                        jobName = new UnityEngine.UI.Text(jobTextPtr)?.text;

                                    if (!string.IsNullOrWhiteSpace(charName))
                                    {
                                        string charAnnouncement = charName;
                                        if (!string.IsNullOrWhiteSpace(jobName))
                                            charAnnouncement += $", {jobName}";
                                        FFI_ScreenReaderMod.SpeakText(charAnnouncement, interrupt: true);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { } // Non-critical — fall through to slot announcement

                // After character switch, queue slot behind character name; otherwise interrupt
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: !characterSwitched);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in EquipmentInfoWindowController.SelectContent patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for EquipmentWindowController.SetActive to clear state when menu closes.
    /// This ensures the active state flag is properly reset when backing out to main menu.
    /// Also restores shop state if equipment was entered from shop menu.
    /// </summary>
    [HarmonyPatch(typeof(KeyInputEquipmentWindowController), "SetActive", new Type[] { typeof(bool) })]
    public static class EquipmentWindowController_SetActive_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isActive)
        {
            if (!isActive)
            {
                // Capture shop restoration flag before clearing state
                bool shouldRestoreShop = EquipMenuState.EnteredFromShop;

                EquipMenuState.ClearState();

                // Restore shop state if we entered from shop menu
                if (shouldRestoreShop)
                {
                    ShopMenuTracker.IsShopMenuActive = true;
                }
            }
        }
    }

    /// <summary>
    /// Patch for equipment item selection (Menu 3).
    /// EquipmentSelectWindowController.SelectContent is called when navigating the item list.
    /// </summary>
    [HarmonyPatch(typeof(KeyInputEquipmentSelectWindowController), "SelectContent",
        new Type[] { typeof(GameCursor), typeof(CustomScrollViewWithinRangeType) })]
    public static class EquipmentSelectWindowController_SelectContent_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(KeyInputEquipmentSelectWindowController __instance, GameCursor targetCursor)
        {
            try
            {
                // NOTE: Don't set IsActive here - wait until after validation
                // Setting it early causes suppression during menu transitions

                if (targetCursor == null) return;

                int index = targetCursor.Index;

                // Access ContentDataList (public property)
                var contentDataList = __instance.ContentDataList;
                if (contentDataList == null || contentDataList.Count == 0)
                {
                    return; // Empty list is normal for empty slots
                }

                if (index < 0 || index >= contentDataList.Count)
                {
                    return;
                }

                var itemData = contentDataList[index];
                if (itemData == null)
                {
                    return;
                }

                // Get item name - handle empty/remove entries
                string itemName = itemData.Name;
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    // This might be a "Remove" or empty entry
                    itemName = "Remove";
                }

                // Strip icon markup from name
                itemName = TextUtils.StripIconMarkup(itemName);

                // Base announcement is name only (stats/description gated behind I key).
                // AutoDetail appends the same detail the I key reads.
                string baseAnnouncement = itemName;
                string announcement = baseAnnouncement;
                if (FFI_ScreenReaderMod.AutoDetailEnabled)
                {
                    string detail = null;
                    try { detail = EquipDetailsAnnouncer.GetDescriptionFromUI(); } catch { }
                    if (!string.IsNullOrWhiteSpace(detail))
                        announcement = $"{baseAnnouncement}: {detail}";
                }

                // Skip duplicates
                if (!EquipMenuState.ShouldAnnounce(announcement))
                    return;

                // Capture shop context BEFORE clearing states (for restoration when equipment menu closes)
                if (!EquipMenuState.IsActive)
                {
                    EquipMenuState.EnteredFromShop = ShopMenuTracker.IsShopMenuActive;
                }

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                // Also clear other menu states to prevent conflicts
                FFI_ScreenReaderMod.ClearOtherMenuStates("Equip");
                EquipMenuState.IsActive = true;

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in EquipmentSelectWindowController.SelectContent patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Manual patches for equipment menu (command bar + character switch announcements).
    /// </summary>
    public static class EquipMenuPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(EquipmentCommandController);

                // Patch EquipmentCommandController.SetFocus(EquipmentCommandId, bool) - clears shop state for command bar
                var setFocusMethod = controllerType.GetMethod("SetFocus",
                    BindingFlags.Instance | BindingFlags.Public,
                    null, new Type[] { typeof(EquipmentCommandId), typeof(bool) }, null);

                if (setFocusMethod != null)
                {
                    harmony.Patch(setFocusMethod,
                        postfix: new HarmonyMethod(typeof(EquipMenuPatches), nameof(CommandSetFocus_Postfix)));
                }
                else
                {
                    MelonLogger.Warning("[Equipment] Could not find EquipmentCommandController.SetFocus");
                }

            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Equipment] Failed to apply patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears shop state when equipment command bar gains focus.
        /// This allows MenuTextDiscovery/generic cursor to announce command bar items.
        /// FF1 signature: SetFocus(EquipmentCommandId id, bool isFocus = true)
        /// </summary>
        public static void CommandSetFocus_Postfix(EquipmentCommandController __instance, EquipmentCommandId id, bool isFocus)
        {
            try
            {
                if (isFocus && ShopMenuTracker.IsShopMenuActive)
                {
                    ShopMenuTracker.ClearForEquipmentSubmenu();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Equipment] Error in CommandSetFocus_Postfix: {ex.Message}");
            }
        }

    }

    /// <summary>
    /// Announces equipment item details when 'I' key or right stick up is pressed.
    /// Reads the description text directly from the game's EquipmentDescriptionWindowController UI panel.
    /// Mirrors ShopDetailsAnnouncer pattern exactly.
    /// </summary>
    public static class EquipDetailsAnnouncer
    {
        public static void AnnounceCurrentItemDetails()
        {
            try
            {
                if (!EquipMenuState.IsActive)
                    return;

                string announcement = GetDescriptionFromUI();
                if (string.IsNullOrEmpty(announcement))
                    announcement = "No description available";

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Equipment] Error announcing details: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the description text from the equipment UI panel.
        /// Pointer chain: EquipmentDescriptionWindowController → view (0x20) → descriptionText (0x18) → text
        /// </summary>
        internal static string GetDescriptionFromUI()
        {
            try
            {
                var descController = UnityEngine.Object.FindObjectOfType<EquipmentDescriptionWindowController>();
                if (descController == null)
                    return null;

                IntPtr controllerPtr = descController.Pointer;
                if (controllerPtr == IntPtr.Zero) return null;

                IntPtr viewPtr = IL2CppFieldReader.ReadPointerSafe(controllerPtr, IL2CppOffsets.Equipment.DescriptionView);
                if (viewPtr == IntPtr.Zero) return null;

                IntPtr textPtr = IL2CppFieldReader.ReadPointerSafe(viewPtr, IL2CppOffsets.Equipment.DescriptionText);
                if (textPtr == IntPtr.Zero) return null;

                var textComponent = new UnityEngine.UI.Text(textPtr);
                return textComponent?.text;
            }
            catch
            {
                return null;
            }
        }
    }
}
