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
        /// <summary>
        /// True when item list or item target selection is active.
        /// Used to suppress generic cursor navigation announcements.
        /// </summary>
        public static bool IsItemMenuActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.ITEM_MENU);
            set => MenuStateRegistry.SetActive(MenuStateRegistry.ITEM_MENU, value);
        }

        /// <summary>
        /// Stores the currently selected item data for 'I' key lookup.
        /// </summary>
        public static ItemListContentData LastSelectedItem { get; set; } = null;

        static ItemMenuState()
        {
            MenuStateRegistry.RegisterResetHandler(MenuStateRegistry.ITEM_MENU, () =>
            {
                LastSelectedItem = null;
            });
        }

        // State machine offset - use centralized offsets
        private const int OFFSET_STATE_MACHINE = IL2CppOffsets.MenuStateMachine.Item;

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
                    int currentState = StateMachineHelper.ReadState(windowController.Pointer, OFFSET_STATE_MACHINE);

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
            catch { } // State machine read may fail

            // Fallback: clear state if we can't determine
            ClearState();
            return false;
        }

        /// <summary>
        /// Clears item menu state when menu is closed.
        /// </summary>
        public static void ClearState()
        {
            IsItemMenuActive = false;
        }

        /// <summary>
        /// Alias for ClearState() for consistency with other state classes.
        /// </summary>
        public static void ResetState() => ClearState();

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
                    return null;
                }

                var corpsList = userDataManager.GetCorpsListClone();
                if (corpsList == null)
                {
                    return null;
                }

                int characterId = characterData.Id;

                foreach (var corps in corpsList)
                {
                    if (corps != null)
                    {
                        if (corps.CharacterId == characterId)
                        {
                            string row = corps.Id == CorpsId.Front ? "Front Row" : "Back Row";
                            return row;
                        }
                    }
                }
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
            catch { } // Localization lookup may fail
            return null;
        }

        /// <summary>
        /// Gets a localized condition/status effect name. Delegates to <see cref="MagicMenuState.GetConditionName"/>
        /// so condition resolution has a single source of truth.
        /// </summary>
        public static string GetConditionName(Condition condition) => MagicMenuState.GetConditionName(condition);

        /// <summary>
        /// Announces an item-list row (name + count + AutoDetail description + "(X of Y)"). Shared by the
        /// SelectContent nav patch and the on-(re)entry reader. Returns true iff it spoke.
        /// </summary>
        public static bool AnnounceItemListData(ItemListContentData itemData, int index, int count)
        {
            try
            {
                LastSelectedItem = itemData;

                string itemName = itemData.Name;
                if (string.IsNullOrEmpty(itemName)) return false;
                itemName = TextUtils.StripIconMarkup(itemName);
                if (string.IsNullOrEmpty(itemName)) return false;

                string baseAnnouncement = itemName;
                int c = itemData.Count;
                if (c > 1) baseAnnouncement += $", {c}";

                string announcement = baseAnnouncement;
                if (FFI_ScreenReaderMod.AutoDetailEnabled)
                {
                    string description = itemData.Description;
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        description = TextUtils.StripIconMarkup(description);
                        if (!string.IsNullOrWhiteSpace(description))
                            announcement = baseAnnouncement + ": " + description;
                    }
                }

                FFI_ScreenReader.Core.FFI_ScreenReaderMod.ClearOtherMenuStates("Item");
                IsItemMenuActive = true;

                announcement = MenuPosition.Format(announcement, index, count);
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemMenu] AnnounceItemListData error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Announces an item-use target character (name + HP + status + "(X of Y)"). Shared by the
        /// SelectContent nav patch and the on-(re)entry reader. Returns true iff it spoke.
        /// </summary>
        public static bool AnnounceItemUseTarget(ItemTargetSelectContentController content, int index, int count)
        {
            try
            {
                var characterData = content.CurrentData;
                if (characterData == null) return false;

                string charName = characterData.Name;
                if (string.IsNullOrWhiteSpace(charName)) return false;

                string announcement = charName;
                try
                {
                    var parameter = characterData.Parameter;
                    if (parameter != null)
                    {
                        int currentHp = parameter.currentHP;
                        int maxHp = parameter.ConfirmedMaxHp();
                        announcement += $", HP {currentHp}/{maxHp}";

                        var conditionList = parameter.CurrentConditionList;
                        if (conditionList != null && conditionList.Count > 0)
                        {
                            var statusNames = new List<string>();
                            foreach (var condition in conditionList)
                            {
                                string conditionName = GetConditionName(condition);
                                if (!string.IsNullOrWhiteSpace(conditionName))
                                    statusNames.Add(conditionName);
                            }
                            if (statusNames.Count > 0)
                                announcement += ", " + string.Join(", ", statusNames);
                        }
                    }
                }
                catch (Exception paramEx)
                {
                    MelonLogger.Warning($"[Item Target] Error getting character parameters: {paramEx.Message}");
                }

                FFI_ScreenReader.Core.FFI_ScreenReaderMod.ClearOtherMenuStates("Item");
                IsItemMenuActive = true;

                announcement = MenuPosition.Format(announcement, index, count);
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Item Target] AnnounceItemUseTarget error: {ex.Message}");
                return false;
            }
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

                // Convert IEnumerable to List for indexed access
                var targetList = new Il2CppSystem.Collections.Generic.List<ItemListContentData>(targets);
                if (targetList == null || targetList.Count == 0)
                    return;

                if (index < 0 || index >= targetList.Count)
                    return;

                var itemData = targetList[index];
                if (itemData == null)
                    return;

                ItemMenuState.AnnounceItemListData(itemData, index, targetList.Count);
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

                ItemMenuState.AnnounceItemUseTarget(content, index, contentList.Count);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ItemUseController.SelectContent patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Re-announce the focused row when the item LIST or the item-use TARGET (re)gains focus — on initial
    /// entry AND on back-out from a deeper screen. The nav SelectContent patches only fire on cursor
    /// movement, so they're silent on (re)entry. The list controllers' state-entry <c>*Init()</c> methods
    /// fire on both, so they arm a one-shot that the per-frame <c>UpdateController</c> consumes once the
    /// list is genuinely built (self-retry), reusing the same announce helpers as navigation.
    /// </summary>
    public static class FieldItemReannouncePatches
    {
        // ItemListController (KeyInput) offsets
        private const int ITEM_LIST_SELECT_CURSOR = 0x60;
        private const int ITEM_LIST_DATA_LIST = 0x78;     // IEnumerable<ItemListContentData>
        // ItemUseController (KeyInput) offsets
        private const int ITEM_USE_CONTENT_LIST = 0x40;   // List<ItemTargetSelectContentController>
        private const int ITEM_USE_SELECT_CURSOR = 0x50;

        private static bool _pendingItemListAnnounce;
        private static int _itemListFrames;
        private static bool _pendingItemTargetAnnounce;
        private static int _itemTargetFrames;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            // Item list: arm on each list state-entry, consume in the per-frame UpdateController.
            Patch(harmony, typeof(KeyInputItemListController), "UseSelectInit", nameof(ItemList_Init_Postfix));
            Patch(harmony, typeof(KeyInputItemListController), "ImportantSelectInit", nameof(ItemList_Init_Postfix));
            Patch(harmony, typeof(KeyInputItemListController), "OrganizeSelectInit", nameof(ItemList_Init_Postfix));
            Patch(harmony, typeof(KeyInputItemListController), "UpdateController", nameof(ItemList_Update_Postfix));

            // Item-use target: arm on Single/All state-entry, consume in the per-frame UpdateController.
            Patch(harmony, typeof(KeyInputItemUseController), "SingleInit", nameof(ItemTarget_Init_Postfix));
            Patch(harmony, typeof(KeyInputItemUseController), "AllInit", nameof(ItemTarget_Init_Postfix));
            Patch(harmony, typeof(KeyInputItemUseController), "UpdateController", nameof(ItemTarget_Update_Postfix));
        }

        private static void Patch(HarmonyLib.Harmony harmony, Type type, string method, string postfixName)
        {
            try
            {
                var target = AccessTools.Method(type, method);
                if (target != null)
                    harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(FieldItemReannouncePatches), postfixName)));
                else
                    MelonLogger.Warning($"[ItemMenu] {type.Name}.{method} not found");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemMenu] Error patching {type.Name}.{method}: {ex.Message}");
            }
        }

        public static void ItemList_Init_Postfix()
        {
            _pendingItemListAnnounce = true;
            _itemListFrames = 0;
        }

        public static void ItemTarget_Init_Postfix()
        {
            _pendingItemTargetAnnounce = true;
            _itemTargetFrames = 0;
        }

        public static void ItemList_Update_Postfix(object __instance)
        {
            try
            {
                if (!_pendingItemListAnnounce) return;
                var controller = __instance as KeyInputItemListController;
                if (controller == null || !controller.gameObject.activeInHierarchy) return;

                _itemListFrames++;
                bool spoke = TryAnnounceItemListInitial(controller);
                if (spoke || _itemListFrames > 30)
                    _pendingItemListAnnounce = false;
            }
            catch { }
        }

        public static void ItemTarget_Update_Postfix(object __instance)
        {
            try
            {
                if (!_pendingItemTargetAnnounce) return;
                var controller = __instance as KeyInputItemUseController;
                if (controller == null || !controller.gameObject.activeInHierarchy) return;

                _itemTargetFrames++;
                bool spoke = TryAnnounceItemTargetInitial(controller);
                if (spoke || _itemTargetFrames > 30)
                    _pendingItemTargetAnnounce = false;
            }
            catch { }
        }

        private static bool TryAnnounceItemListInitial(KeyInputItemListController controller)
        {
            try
            {
                IntPtr ptr = controller.Pointer;
                if (ptr == IntPtr.Zero) return false;
                var mm = Il2CppLast.UI.MenuManager.Instance;
                if (mm == null || !mm.IsOpen) return false;

                IntPtr dlPtr = IL2CppFieldReader.ReadPointer(ptr, ITEM_LIST_DATA_LIST);
                if (dlPtr == IntPtr.Zero) return false;
                var enumerable = new Il2CppSystem.Object(dlPtr)
                    .TryCast<Il2CppSystem.Collections.Generic.IEnumerable<ItemListContentData>>();
                if (enumerable == null) return false;
                var list = new Il2CppSystem.Collections.Generic.List<ItemListContentData>(enumerable);
                if (list.Count == 0) return false;

                IntPtr curPtr = IL2CppFieldReader.ReadPointer(ptr, ITEM_LIST_SELECT_CURSOR);
                if (curPtr == IntPtr.Zero) return false;
                int index = new GameCursor(curPtr).Index;
                if (index < 0 || index >= list.Count) return false;

                var itemData = list[index];
                if (itemData == null) return false;
                return ItemMenuState.AnnounceItemListData(itemData, index, list.Count);
            }
            catch { return false; }
        }

        private static bool TryAnnounceItemTargetInitial(KeyInputItemUseController controller)
        {
            try
            {
                IntPtr ptr = controller.Pointer;
                if (ptr == IntPtr.Zero) return false;
                var mm = Il2CppLast.UI.MenuManager.Instance;
                if (mm == null || !mm.IsOpen) return false;

                IntPtr clPtr = IL2CppFieldReader.ReadPointer(ptr, ITEM_USE_CONTENT_LIST);
                if (clPtr == IntPtr.Zero) return false;
                var list = new Il2CppSystem.Collections.Generic.List<ItemTargetSelectContentController>(clPtr);
                if (list.Count == 0) return false;

                IntPtr curPtr = IL2CppFieldReader.ReadPointer(ptr, ITEM_USE_SELECT_CURSOR);
                if (curPtr == IntPtr.Zero) return false;
                int index = new GameCursor(curPtr).Index;
                if (index < 0 || index >= list.Count) return false;

                var content = list[index];
                if (content == null) return false;
                return ItemMenuState.AnnounceItemUseTarget(content, index, list.Count);
            }
            catch { return false; }
        }
    }

}
