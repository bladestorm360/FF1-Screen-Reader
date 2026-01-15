using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using Il2CppLast.Management;

// Type aliases for IL2CPP types
using KeyInputItemListController = Il2CppLast.UI.KeyInput.ItemListController;
using KeyInputItemUseController = Il2CppLast.UI.KeyInput.ItemUseController;
using ItemListContentData = Il2CppLast.UI.ItemListContentData;
using ItemTargetSelectContentController = Il2CppLast.UI.KeyInput.ItemTargetSelectContentController;
using GameCursor = Il2CppLast.UI.Cursor;
using CustomScrollViewWithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using Condition = Il2CppLast.Data.Master.Condition;
using CorpsId = Il2CppLast.Defaine.User.CorpsId;
using Corps = Il2CppLast.Data.User.Corps;
using UserDataManager = Il2CppLast.Management.UserDataManager;
using BattleItemInfomationController = Il2CppLast.UI.KeyInput.BattleItemInfomationController;
using KeyInputItemCommandController = Il2CppLast.UI.KeyInput.ItemCommandController;
using KeyInputItemWindowController = Il2CppLast.UI.KeyInput.ItemWindowController;
using ItemCommandId = Il2CppLast.Defaine.UI.ItemCommandId;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Helper for item menu announcements.
    /// </summary>
    public static class ItemMenuState
    {
        private static string lastAnnouncement = "";
        private static float lastAnnouncementTime = 0f;

        /// <summary>
        /// True when item list or item target selection is active.
        /// Used to suppress generic cursor navigation announcements.
        /// </summary>
        public static bool IsItemMenuActive { get; set; } = false;

        /// <summary>
        /// Stores the currently selected item data for 'I' key lookup.
        /// </summary>
        public static ItemListContentData LastSelectedItem { get; set; } = null;

        // State machine offsets (from dump.cs)
        // KeyInput.ItemWindowController has stateMachine at offset 0x70
        private const int OFFSET_STATE_MACHINE = 0x70;
        private const int OFFSET_STATE_MACHINE_CURRENT = 0x10;
        private const int OFFSET_STATE_TAG = 0x10;

        // ItemWindowController.State values
        private const int STATE_NONE = 0;
        private const int STATE_COMMAND_SELECT = 1;  // Command bar (Use/Key Items/Sort)
        private const int STATE_USE_SELECT = 2;       // Item list for Use
        private const int STATE_IMPORTANT_SELECT = 3; // Key Items list
        private const int STATE_ORGANIZE_SELECT = 4;  // Sort list
        private const int STATE_TARGET_SELECT = 5;    // Target selection

        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// Uses state machine to determine if we're in item list vs command bar.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsItemMenuActive)
                return false;

            try
            {
                // Check the ItemWindowController's state machine
                var windowController = UnityEngine.Object.FindObjectOfType<KeyInputItemWindowController>();
                if (windowController != null)
                {
                    int currentState = GetCurrentState(windowController);

                    // If we're in CommandSelect state, don't suppress - let MenuTextDiscovery handle it
                    if (currentState == STATE_COMMAND_SELECT || currentState == STATE_NONE)
                    {
                        ClearState();
                        return false;
                    }

                    // We're in an item list state - suppress
                    if (currentState == STATE_USE_SELECT ||
                        currentState == STATE_IMPORTANT_SELECT ||
                        currentState == STATE_ORGANIZE_SELECT ||
                        currentState == STATE_TARGET_SELECT)
                    {
                        return true;
                    }
                }
            }
            catch { }

            // Fallback: clear state if we can't determine
            ClearState();
            return false;
        }

        /// <summary>
        /// Reads the current state from ItemWindowController's state machine.
        /// Returns -1 if unable to read.
        /// </summary>
        private static int GetCurrentState(KeyInputItemWindowController controller)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return -1;

                unsafe
                {
                    // Read stateMachine pointer at offset 0x70
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
        /// Clears item menu state when menu is closed.
        /// </summary>
        public static void ClearState()
        {
            IsItemMenuActive = false;
            LastSelectedItem = null;
            lastAnnouncement = "";
            lastAnnouncementTime = 0f;
        }

        /// <summary>
        /// Alias for ClearState() for consistency with other state classes.
        /// </summary>
        public static void ResetState() => ClearState();

        public static bool ShouldAnnounce(string announcement)
        {
            float currentTime = UnityEngine.Time.time;
            if (announcement == lastAnnouncement && (currentTime - lastAnnouncementTime) < 0.1f)
                return false;

            lastAnnouncement = announcement;
            lastAnnouncementTime = currentTime;
            return true;
        }

        /// <summary>
        /// Gets the row (Front/Back) for a character.
        /// </summary>
        public static string GetCharacterRow(OwnedCharacterData characterData)
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                {
                    MelonLogger.Msg($"[ItemMenu] UserDataManager is null");
                    return null;
                }

                var corpsList = userDataManager.GetCorpsListClone();
                if (corpsList == null)
                {
                    MelonLogger.Msg($"[ItemMenu] CorpsList is null");
                    return null;
                }

                int characterId = characterData.Id;
                MelonLogger.Msg($"[ItemMenu] Looking for character {characterData.Name} (ID={characterId}) in {corpsList.Count} corps entries");

                foreach (var corps in corpsList)
                {
                    if (corps != null)
                    {
                        MelonLogger.Msg($"[ItemMenu] Corps entry: CharId={corps.CharacterId}, CorpsId={corps.Id}");
                        if (corps.CharacterId == characterId)
                        {
                            string row = corps.Id == CorpsId.Front ? "Front Row" : "Back Row";
                            MelonLogger.Msg($"[ItemMenu] Found match! {characterData.Name} is in {row}");
                            return row;
                        }
                    }
                }
                MelonLogger.Msg($"[ItemMenu] No matching Corps found for {characterData.Name}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemMenu] Error getting character row: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Gets the localized name for an ItemCommandId.
        /// </summary>
        public static string GetItemCommandName(ItemCommandId commandId)
        {
            switch (commandId)
            {
                case ItemCommandId.Use:
                    return GetLocalizedCommand("$menu_item_use") ?? "Use";
                case ItemCommandId.Organize:
                    return GetLocalizedCommand("$menu_item_organize") ?? "Sort";
                case ItemCommandId.Important:
                    return GetLocalizedCommand("$menu_item_important") ?? "Key Items";
                default:
                    return null;
            }
        }

        private static string GetLocalizedCommand(string mesId)
        {
            try
            {
                var messageManager = MessageManager.Instance;
                if (messageManager != null)
                {
                    string text = messageManager.GetMessage(mesId, false);
                    if (!string.IsNullOrWhiteSpace(text))
                        return TextUtils.StripIconMarkup(text);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Gets a localized condition/status effect name from a Condition object.
        /// </summary>
        public static string GetConditionName(Condition condition)
        {
            if (condition == null)
                return null;

            try
            {
                string mesId = condition.MesIdName;
                if (!string.IsNullOrEmpty(mesId))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedName = messageManager.GetMessage(mesId, false);
                        if (!string.IsNullOrWhiteSpace(localizedName))
                            return localizedName;
                    }
                }
            }
            catch
            {
                // Fall through
            }

            return null;
        }
    }

    /// <summary>
    /// Patch for ItemWindowController.SetActive to clear state when menu closes.
    /// This ensures the active state flag is properly reset when backing out to main menu.
    /// </summary>
    [HarmonyPatch(typeof(KeyInputItemWindowController), "SetActive", new Type[] { typeof(bool) })]
    public static class ItemWindowController_SetActive_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isActive)
        {
            if (!isActive)
            {
                MelonLogger.Msg("[Item Menu] SetActive(false) - clearing state");
                ItemMenuState.ResetState();
            }
        }
    }

    /// <summary>
    /// Patch for item list selection.
    /// Announces item name: description when navigating items in the menu.
    /// </summary>
    [HarmonyPatch(typeof(KeyInputItemListController), "SelectContent",
        new Type[] {
            typeof(Il2CppSystem.Collections.Generic.IEnumerable<ItemListContentData>),
            typeof(int),
            typeof(GameCursor),
            typeof(CustomScrollViewWithinRangeType)
        })]
    public static class ItemListController_SelectContent_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            KeyInputItemListController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<ItemListContentData> targets,
            int index,
            GameCursor targetCursor)
        {
            try
            {
                if (targets == null)
                    return;

                // NOTE: Don't set IsItemMenuActive here - wait until after validation
                // Setting it early causes suppression during menu transitions

                // Convert IEnumerable to List for indexed access
                var targetList = new Il2CppSystem.Collections.Generic.List<ItemListContentData>(targets);
                if (targetList == null || targetList.Count == 0)
                    return;

                if (index < 0 || index >= targetList.Count)
                    return;

                var itemData = targetList[index];
                if (itemData == null)
                    return;

                // Store selected item for 'I' key lookup
                ItemMenuState.LastSelectedItem = itemData;

                string itemName = itemData.Name;
                if (string.IsNullOrEmpty(itemName))
                    return;

                // Strip icon markup from name
                itemName = TextUtils.StripIconMarkup(itemName);

                if (string.IsNullOrEmpty(itemName))
                    return;

                // Build announcement: "Item Name: Description"
                string announcement = itemName;

                // Add description if available
                string description = itemData.Description;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    description = TextUtils.StripIconMarkup(description);

                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        announcement += ": " + description;
                    }
                }

                // Skip duplicates
                if (!ItemMenuState.ShouldAnnounce(announcement))
                    return;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                // Also clear other menu states to prevent conflicts
                FFI_ScreenReader.Core.FFI_ScreenReaderMod.ClearOtherMenuStates("Item");
                ItemMenuState.IsItemMenuActive = true;

                MelonLogger.Msg($"[Item Menu] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ItemListController.SelectContent patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for character target selection when using an item.
    /// Announces character name, HP, and status effects.
    /// </summary>
    [HarmonyPatch(typeof(KeyInputItemUseController), "SelectContent",
        new Type[] {
            typeof(Il2CppSystem.Collections.Generic.IEnumerable<ItemTargetSelectContentController>),
            typeof(GameCursor)
        })]
    public static class ItemUseController_SelectContent_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            KeyInputItemUseController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<ItemTargetSelectContentController> targetContents,
            GameCursor targetCursor)
        {
            try
            {
                if (targetCursor == null || targetContents == null)
                    return;

                // NOTE: Don't set IsItemMenuActive here - wait until after validation
                // Setting it early causes suppression during menu transitions

                int index = targetCursor.Index;

                // Convert to list for indexed access
                var contentList = new Il2CppSystem.Collections.Generic.List<ItemTargetSelectContentController>(targetContents);
                if (contentList == null || contentList.Count == 0)
                    return;

                if (index < 0 || index >= contentList.Count)
                    return;

                var content = contentList[index];
                if (content == null)
                    return;

                // Get character data from the content controller
                var characterData = content.CurrentData;
                if (characterData == null)
                    return;

                // Build announcement: "Character Name, Level X, HP current/max, Status effects"
                // Note: Row is intentionally NOT included for item targeting - only for status/equip menus
                // Note: FF1 uses spell charges per level, not MP - so MP is not announced
                string charName = characterData.Name;
                if (string.IsNullOrWhiteSpace(charName))
                    return;

                string announcement = charName;

                // Add level, HP, and status information
                try
                {
                    var parameter = characterData.Parameter;
                    if (parameter != null)
                    {
                        // Add level
                        int level = parameter.BaseLevel;
                        if (level > 0)
                        {
                            announcement += $", Level {level}";
                        }

                        // Add HP
                        int currentHp = parameter.currentHP;
                        int maxHp = parameter.ConfirmedMaxHp();
                        announcement += $", HP {currentHp}/{maxHp}";

                        // Add status conditions
                        var conditionList = parameter.CurrentConditionList;
                        if (conditionList != null && conditionList.Count > 0)
                        {
                            var statusNames = new List<string>();
                            foreach (var condition in conditionList)
                            {
                                string conditionName = ItemMenuState.GetConditionName(condition);
                                if (!string.IsNullOrWhiteSpace(conditionName))
                                {
                                    statusNames.Add(conditionName);
                                }
                            }

                            if (statusNames.Count > 0)
                            {
                                announcement += ", " + string.Join(", ", statusNames);
                            }
                        }
                    }
                }
                catch (Exception paramEx)
                {
                    MelonLogger.Warning($"[Item Target] Error getting character parameters: {paramEx.Message}");
                }

                // Skip duplicates
                if (!ItemMenuState.ShouldAnnounce(announcement))
                    return;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                // Also clear other menu states to prevent conflicts
                FFI_ScreenReader.Core.FFI_ScreenReaderMod.ClearOtherMenuStates("Item");
                ItemMenuState.IsItemMenuActive = true;

                MelonLogger.Msg($"[Item Target] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ItemUseController.SelectContent patch: {ex.Message}");
            }
        }
    }

}
