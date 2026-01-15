using System;
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
        public static bool IsActive { get; set; } = false;

        // State machine offsets (from dump.cs)
        // KeyInput.EquipmentWindowController has stateMachine at offset 0x60
        private const int OFFSET_STATE_MACHINE = 0x60;
        private const int OFFSET_STATE_MACHINE_CURRENT = 0x10;
        private const int OFFSET_STATE_TAG = 0x10;

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
                    int currentState = GetCurrentState(equipmentController);
                    MelonLogger.Msg($"[DEBUG EquipMenu] State={currentState}");

                    // STATE_COMMAND means we're in command bar - let MenuTextDiscovery handle
                    if (currentState == STATE_COMMAND)
                    {
                        MelonLogger.Msg("[DEBUG EquipMenu] STATE_COMMAND, clearing and not suppressing");
                        ClearState();
                        return false;
                    }

                    // STATE_NONE means menu closing
                    if (currentState == STATE_NONE)
                    {
                        MelonLogger.Msg("[DEBUG EquipMenu] STATE_NONE, clearing");
                        ClearState();
                        return false;
                    }
                }

                // Validate the actual list controllers are active (not just parent)
                var infoController = UnityEngine.Object.FindObjectOfType<KeyInputEquipmentInfoWindowController>();
                var selectController = UnityEngine.Object.FindObjectOfType<KeyInputEquipmentSelectWindowController>();

                bool infoActive = infoController != null && infoController.gameObject.activeInHierarchy;
                bool selectActive = selectController != null && selectController.gameObject.activeInHierarchy;

                MelonLogger.Msg($"[DEBUG EquipMenu] InfoActive={infoActive}, SelectActive={selectActive}");

                if (!infoActive && !selectActive)
                {
                    // Neither list controller is active - we've left the equipment submenu
                    MelonLogger.Msg("[DEBUG EquipMenu] No list controllers active, clearing");
                    ClearState();
                    return false;
                }

                // At least one list is active - suppress
                MelonLogger.Msg("[DEBUG EquipMenu] List controller active, suppressing");
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"[DEBUG EquipMenu] Exception: {ex.Message}, clearing");
                ClearState();
                return false;
            }
        }

        /// <summary>
        /// Reads the current state from EquipmentWindowController's state machine.
        /// Returns -1 if unable to read.
        /// </summary>
        private static int GetCurrentState(KeyInputEquipmentWindowController controller)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return -1;

                unsafe
                {
                    // Read stateMachine pointer at offset 0x60
                    IntPtr stateMachinePtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_STATE_MACHINE);
                    if (stateMachinePtr == IntPtr.Zero)
                        return -1;

                    // Read current State<T> pointer at offset 0x10
                    IntPtr currentStatePtr = *(IntPtr*)((byte*)stateMachinePtr.ToPointer() + OFFSET_STATE_MACHINE_CURRENT);
                    if (currentStatePtr == IntPtr.Zero)
                        return -1;

                    // Read Tag (int) at offset 0x10
                    int stateValue = *(int*)((byte*)currentStatePtr.ToPointer() + OFFSET_STATE_TAG);
                    return stateValue;
                }
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Clears equipment menu state.
        /// </summary>
        public static void ClearState()
        {
            IsActive = false;
            lastAnnouncement = "";
            lastAnnouncementTime = 0f;
        }

        /// <summary>
        /// Alias for ClearState() for consistency with other state classes.
        /// </summary>
        public static void ResetState() => ClearState();

        private static string lastAnnouncement = "";
        private static float lastAnnouncementTime = 0f;

        public static bool ShouldAnnounce(string announcement)
        {
            float currentTime = UnityEngine.Time.time;
            if (announcement == lastAnnouncement && (currentTime - lastAnnouncementTime) < 0.1f)
                return false;

            lastAnnouncement = announcement;
            lastAnnouncementTime = currentTime;
            return true;
        }

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

                // Get equipped item from Data property
                string equippedItem = null;
                var itemData = contentView.Data;
                if (itemData != null)
                {
                    try
                    {
                        equippedItem = itemData.Name;

                        // Add parameter message (ATK +12, DEF +5, etc.)
                        string paramMsg = itemData.ParameterMessage;
                        if (!string.IsNullOrWhiteSpace(paramMsg))
                        {
                            equippedItem += ", " + paramMsg;
                        }
                    }
                    catch { }
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

                // Skip duplicates
                if (!EquipMenuState.ShouldAnnounce(announcement))
                    return;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                // Also clear other menu states to prevent conflicts
                FFI_ScreenReaderMod.ClearOtherMenuStates("Equip");
                EquipMenuState.IsActive = true;

                MelonLogger.Msg($"[Equipment Slot] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
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
    /// </summary>
    [HarmonyPatch(typeof(KeyInputEquipmentWindowController), "SetActive", new Type[] { typeof(bool) })]
    public static class EquipmentWindowController_SetActive_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isActive)
        {
            if (!isActive)
            {
                MelonLogger.Msg("[Equipment Menu] SetActive(false) - clearing state");
                EquipMenuState.ClearState();
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

                // Build announcement with item details
                string announcement = itemName;

                // Add parameter info (ATK +15, DEF +8, etc.)
                try
                {
                    string paramMessage = itemData.ParameterMessage;
                    if (!string.IsNullOrWhiteSpace(paramMessage))
                    {
                        paramMessage = TextUtils.StripIconMarkup(paramMessage);
                        announcement += $", {paramMessage}";
                    }
                }
                catch { }

                // Add description
                try
                {
                    string description = itemData.Description;
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        description = TextUtils.StripIconMarkup(description);
                        announcement += $", {description}";
                    }
                }
                catch { }

                // Skip duplicates
                if (!EquipMenuState.ShouldAnnounce(announcement))
                    return;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                // Also clear other menu states to prevent conflicts
                FFI_ScreenReaderMod.ClearOtherMenuStates("Equip");
                EquipMenuState.IsActive = true;

                MelonLogger.Msg($"[Equipment Item] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in EquipmentSelectWindowController.SelectContent patch: {ex.Message}");
            }
        }
    }
}
