using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using Il2CppInterop.Runtime;

// FF1 Battle types - KeyInput namespace
using BattleItemInfomationController = Il2CppLast.UI.KeyInput.BattleItemInfomationController;
using BattleItemInfomationContentController = Il2CppLast.UI.KeyInput.BattleItemInfomationContentController;
using ItemListContentData = Il2CppLast.UI.ItemListContentData;
using OwnedItemData = Il2CppLast.Data.User.OwnedItemData;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Manual patch application for battle item menu.
    /// </summary>
    public static class BattleItemPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[Battle Item] Applying battle item menu patches...");

                var controllerType = typeof(BattleItemInfomationController);

                // Find SelectContent(Cursor, WithinRangeType) - called when navigating items
                // Get index from cursor.Index property
                MethodInfo selectContentMethod = null;
                var methods = controllerType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                foreach (var m in methods)
                {
                    if (m.Name == "SelectContent")
                    {
                        var parameters = m.GetParameters();
                        var paramTypes = string.Join(", ", Array.ConvertAll(parameters, p => p.ParameterType.Name));
                        MelonLogger.Msg($"[Battle Item] Found SelectContent({paramTypes})");

                        // Look for (Cursor, WithinRangeType) signature - first param is Cursor
                        if (parameters.Length >= 1 &&
                            parameters[0].ParameterType.Name == "Cursor")
                        {
                            selectContentMethod = m;
                            MelonLogger.Msg($"[Battle Item] Selected this overload for patching");
                            break;
                        }
                    }
                }

                if (selectContentMethod != null)
                {
                    var postfix = typeof(BattleItemPatches)
                        .GetMethod(nameof(SelectContent_Postfix), BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Battle Item] Patched SelectContent successfully");
                }
                else
                {
                    MelonLogger.Warning("[Battle Item] SelectContent method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SelectContent - announces item name and description.
        /// Method signature: SelectContent(Cursor targetCursor, WithinRangeType type)
        /// Get index from cursor.Index property
        /// </summary>
        public static void SelectContent_Postfix(object __instance, object __0)
        {
            try
            {
                if (__instance == null || __0 == null)
                    return;

                // Get cursor and its index
                var cursor = __0 as GameCursor;
                if (cursor == null)
                    return;

                int index = cursor.Index;
                MelonLogger.Msg($"[Battle Item] SelectContent called, cursor index: {index}");

                var controller = __instance as BattleItemInfomationController;
                if (controller == null)
                    return;

                // If command menu is active but user didn't select "Items" (index 2),
                // this is a stale callback from exiting the item menu - ignore it
                if (BattleCommandState.IsActive && BattleCommandState.LastSelectedCommandIndex != 2)
                    return;

                // NOTE: Do NOT set IsActive here - wait until AFTER validation succeeds
                // Setting it early causes suppression during menu transitions

                // Try to get item data - use the index parameter directly
                string announcement = TryGetItemAnnouncement(controller, index);

                if (string.IsNullOrEmpty(announcement))
                {
                    MelonLogger.Msg("[Battle Item] Could not get item data");
                    return;
                }

                // Use central deduplicator - skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce("BattleItem", announcement))
                    return;

                // Set state AFTER successful validation - this is the key fix
                FFI_ScreenReaderMod.ClearOtherMenuStates("BattleItem");
                BattleItemMenuState.IsActive = true;
                BattleTargetState.SetTargetSelectionActive(false);

                MelonLogger.Msg($"[Battle Item] Announcing: {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error in SelectContent patch: {ex.Message}");
            }
        }

        // IL2CPP field offsets - use centralized offsets
        private const int OFFSET_DISPLAY_DATA_LIST = IL2CppOffsets.BattleItem.DisplayDataList;
        private const int OFFSET_DATA_LIST = IL2CppOffsets.BattleItem.DataList;

        /// <summary>
        /// Try to get item announcement from controller.
        /// Uses displayDataList (offset 0xC8) which has ItemListContentData with Description.
        /// Uses IL2CPP pointer-based access since reflection doesn't work for IL2CPP fields.
        /// </summary>
        private static string TryGetItemAnnouncement(BattleItemInfomationController controller, int index)
        {
            try
            {
                // Method 1 (BEST): Try displayDataList via IL2CPP pointer access - offset 0xC8
                // This contains ItemListContentData which has both Name and Description
                Il2CppSystem.Collections.Generic.List<ItemListContentData> displayDataList = null;
                try
                {
                    IntPtr controllerPtr = controller.Pointer;
                    if (controllerPtr != IntPtr.Zero)
                    {
                        unsafe
                        {
                            IntPtr displayListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_DISPLAY_DATA_LIST);
                            if (displayListPtr != IntPtr.Zero)
                            {
                                displayDataList = new Il2CppSystem.Collections.Generic.List<ItemListContentData>(displayListPtr);
                                MelonLogger.Msg($"[Battle Item] displayDataList via pointer, count: {displayDataList?.Count ?? -1}");
                            }
                            else
                            {
                                MelonLogger.Msg($"[Battle Item] displayDataList pointer is null");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"[Battle Item] Error reading displayDataList via pointer: {ex.Message}");
                }

                if (displayDataList != null && index >= 0 && index < displayDataList.Count)
                {
                    var data = displayDataList[index];
                    if (data != null)
                    {
                        return FormatItemAnnouncement(data);
                    }
                }

                // Method 2: Try dataList via IL2CPP pointer access - offset 0x60
                Il2CppSystem.Collections.Generic.List<OwnedItemData> dataList = null;
                try
                {
                    IntPtr controllerPtr = controller.Pointer;
                    if (controllerPtr != IntPtr.Zero)
                    {
                        unsafe
                        {
                            IntPtr dataListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_DATA_LIST);
                            if (dataListPtr != IntPtr.Zero)
                            {
                                dataList = new Il2CppSystem.Collections.Generic.List<OwnedItemData>(dataListPtr);
                                MelonLogger.Msg($"[Battle Item] dataList via pointer, count: {dataList?.Count ?? -1}");
                            }
                            else
                            {
                                MelonLogger.Msg($"[Battle Item] dataList pointer is null");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"[Battle Item] Error reading dataList via pointer: {ex.Message}");
                }

                if (dataList != null && index >= 0 && index < dataList.Count)
                {
                    var itemData = dataList[index];
                    if (itemData != null)
                    {
                        string name = itemData.Name;
                        if (!string.IsNullOrEmpty(name))
                        {
                            // OwnedItemData only has Name - no description available
                            return TextUtils.StripIconMarkup(name);
                        }
                    }
                }

                // Method 3: Try to find focused item in all content controllers
                var allContentControllers = UnityEngine.Object.FindObjectsOfType<BattleItemInfomationContentController>();
                if (allContentControllers != null && allContentControllers.Length > 0)
                {
                    foreach (var cc in allContentControllers)
                    {
                        if (cc == null || !cc.gameObject.activeInHierarchy)
                            continue;

                        try
                        {
                            var data = cc.Data;
                            if (data != null && data.IsFocus)
                            {
                                return FormatItemAnnouncement(data);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error getting item announcement: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Format item data into announcement string.
        /// </summary>
        private static string FormatItemAnnouncement(ItemListContentData data)
        {
            try
            {
                string name = data.Name;
                if (string.IsNullOrEmpty(name))
                    return null;

                name = TextUtils.StripIconMarkup(name);
                if (string.IsNullOrEmpty(name))
                    return null;

                string announcement = name;

                // Try to get description
                try
                {
                    string description = data.Description;
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        description = TextUtils.StripIconMarkup(description);
                        if (!string.IsNullOrWhiteSpace(description))
                        {
                            announcement += ": " + description;
                        }
                    }
                }
                catch
                {
                    // Description not available, just use name
                }

                return announcement;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error formatting announcement: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reset state (call at battle end).
        /// </summary>
        public static void ResetState()
        {
            AnnouncementDeduplicator.Reset("BattleItem");
        }
    }
}
