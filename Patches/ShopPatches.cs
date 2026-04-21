using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

// FF1 Shop UI types (root namespace in IL2CPP)
using ShopController = Il2CppLast.UI.KeyInput.ShopController;
using ShopInfoController = Il2CppLast.UI.KeyInput.ShopInfoController;
using ShopListItemContentController = Il2CppLast.UI.KeyInput.ShopListItemContentController;
using ShopListMainContentController = Il2CppLast.UI.KeyInput.ShopListMainContentController;
using ShopTradeWindowController = Il2CppLast.UI.KeyInput.ShopTradeWindowController;
using ShopCommandMenuContentController = Il2CppLast.UI.KeyInput.ShopCommandMenuContentController;
using ShopCommandMenuController = Il2CppLast.UI.KeyInput.ShopCommandMenuController;
using ShopCommandId = Il2CppLast.Defaine.ShopCommandId;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Shop menu patches using manual Harmony patching.
    /// Patches item list focus, command menu focus, and quantity changes.
    /// Magic shop patches are in ShopMagicPatches.
    /// </summary>
    public static class ShopPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                PatchSetDescription(harmony);
                PatchCommandSetFocus(harmony);
                PatchTradeWindowShow(harmony);
                PatchTradeWindowCounts(harmony);
                PatchShopClose(harmony);

                // Magic shop patches (separate class)
                ShopMagicPatches.ApplyPatches(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to apply shop patches: {ex.Message}");
            }
        }

        #region Patch Registration

        private static void PatchSetDescription(HarmonyLib.Harmony harmony)
        {
            try
            {
                // ShopInfoController.SetDescription(string) is invoked on every cursor
                // move in the shop item list, regardless of affordability/canSelect. We use
                // it as a cursor-moved signal and read the in-focus item from
                // ShopListMainContentController (selectCursor.Index into productContentList).
                Type controllerType = typeof(ShopInfoController);
                var setDescriptionMethod = controllerType.GetMethod(
                    "SetDescription",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new Type[] { typeof(string) },
                    null);

                if (setDescriptionMethod == null)
                {
                    MelonLogger.Warning("[Shop] Could not find ShopInfoController.SetDescription(string)");
                    return;
                }

                harmony.Patch(setDescriptionMethod,
                    postfix: new HarmonyMethod(typeof(ShopPatches), nameof(SetDescription_Postfix)));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch ShopInfoController.SetDescription: {ex.Message}");
            }
        }

        private static void PatchCommandSetFocus(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(ShopCommandMenuController);
                var setCursorMethod = controllerType.GetMethod("SetCursor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new Type[] { typeof(int) }, null);

                if (setCursorMethod == null)
                {
                    MelonLogger.Warning("[Shop] Could not find ShopCommandMenuController.SetCursor(int)");
                    return;
                }

                harmony.Patch(setCursorMethod,
                    postfix: new HarmonyMethod(typeof(ShopPatches), nameof(CommandSetCursor_Postfix)));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch command SetCursor: {ex.Message}");
            }
        }

        private static void PatchTradeWindowShow(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type tradeType = typeof(ShopTradeWindowController);
                var showMethod = tradeType.GetMethod(
                    "Show",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (showMethod != null)
                {
                    harmony.Patch(showMethod,
                        postfix: new HarmonyMethod(typeof(ShopPatches), nameof(TradeWindowShow_Postfix)));
                }
                else
                {
                    MelonLogger.Warning("[Shop] Could not find ShopTradeWindowController.Show");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch trade window Show: {ex.Message}");
            }
        }

        private static void PatchTradeWindowCounts(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type tradeType = typeof(ShopTradeWindowController);
                var addMethod = tradeType.GetMethod("AddCount",
                    BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                var takeMethod = tradeType.GetMethod("TakeCount",
                    BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

                var postfix = new HarmonyMethod(typeof(ShopPatches), nameof(TradeWindowCount_Postfix));

                if (addMethod != null)
                    harmony.Patch(addMethod, postfix: postfix);
                else
                    MelonLogger.Warning("[Shop] Could not find ShopTradeWindowController.AddCount");

                if (takeMethod != null)
                    harmony.Patch(takeMethod, postfix: postfix);
                else
                    MelonLogger.Warning("[Shop] Could not find ShopTradeWindowController.TakeCount");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch trade window count methods: {ex.Message}");
            }
        }

        private static void PatchShopClose(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(ShopController);
                var closeMethod = controllerType.GetMethod("Close", Type.EmptyTypes);

                if (closeMethod != null)
                {
                    harmony.Patch(closeMethod,
                        postfix: new HarmonyMethod(typeof(ShopPatches), nameof(ShopClose_Postfix)));
                }
                else
                {
                    MelonLogger.Warning("[Shop] Could not find ShopController.Close");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch shop Close: {ex.Message}");
            }
        }

        #endregion

        #region Item/Command Postfixes

        /// <summary>
        /// Fires whenever the shop description panel updates, which happens on every
        /// cursor move in the item list — affordable or not. We use this as a signal
        /// that the cursor moved, then read the in-focus item's name/price directly from
        /// ShopListMainContentController (selectCursor.Index into productContentList).
        /// The description parameter is ignored here; data comes from the item itself.
        /// </summary>
        public static void SetDescription_Postfix(ShopInfoController __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                // Gate on the ShopController state machine — the game's own
                // authoritative "which panel has focus" signal. States 2 and 3
                // are SelectProduct (buy list) and SelectSellItem (sell list);
                // any other value (command bar, confirm, equipment, magic) means
                // this fire is a transition flicker or a preview refresh and
                // should not announce an item.
                var shopController = ShopMenuTracker.GetCachedOrFindShopController();
                if (shopController != null)
                {
                    int state = ShopMenuTracker.GetCurrentState(shopController);
                    if (state != 2 && state != 3) // SelectProduct, SelectSellItem
                        return;
                }

                var mainList = FindActiveMainContentController();
                if (mainList == null)
                    return; // Not in the item list (command menu, magic slot grid, etc.)

                // Reaching the item list means the magic spell slot grid (if previously active)
                // has been backed out of.
                if (ShopMenuTracker.IsInMagicSlotSelection)
                {
                    ShopMenuTracker.IsInMagicSlotSelection = false;
                    ShopMagicPatches.ResetState();
                }

                FFI_ScreenReaderMod.ClearOtherMenuStates("Shop");
                ShopMenuTracker.IsShopMenuActive = true;

                IntPtr instancePtr = mainList.Pointer;
                if (instancePtr == IntPtr.Zero)
                    return;

                IntPtr cursorPtr = IL2CppFieldReader.ReadPointer(instancePtr, IL2CppOffsets.Shop.ListMainSelectCursor);
                if (cursorPtr == IntPtr.Zero)
                    return;

                var cursor = new GameCursor(cursorPtr);
                int index = cursor.Index;
                if (index < 0)
                    return;

                IntPtr listPtr = IL2CppFieldReader.ReadPointer(instancePtr, IL2CppOffsets.Shop.ListMainProductContentList);
                if (listPtr == IntPtr.Zero)
                    return;

                var list = new Il2CppSystem.Collections.Generic.List<ShopListItemContentController>(listPtr);
                if (list == null || index >= list.Count)
                    return;

                var content = list[index];
                AnnounceFocusedItem(content);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in SetDescription_Postfix: {ex.Message}");
            }
        }

        private static ShopListMainContentController _cachedMainList;

        private static ShopListMainContentController FindActiveMainContentController()
        {
            try
            {
                if (_cachedMainList != null)
                {
                    try
                    {
                        if (_cachedMainList.Pointer != IntPtr.Zero &&
                            _cachedMainList.gameObject != null &&
                            _cachedMainList.gameObject.activeInHierarchy)
                        {
                            return _cachedMainList;
                        }
                    }
                    catch { _cachedMainList = null; }
                }

                var all = UnityEngine.Object.FindObjectsOfType<ShopListMainContentController>();
                if (all != null)
                {
                    foreach (var candidate in all)
                    {
                        if (candidate == null) continue;
                        try
                        {
                            if (candidate.gameObject != null && candidate.gameObject.activeInHierarchy)
                            {
                                _cachedMainList = candidate;
                                return candidate;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Announces a focused shop item (name + price) and caches it for the I/U keys.
        /// Empty slots announce "Empty" but do NOT overwrite the cached I/U target, so
        /// those keys continue to describe the last real item while the cursor passes
        /// over gaps in the sell inventory.
        /// </summary>
        private static void AnnounceFocusedItem(ShopListItemContentController content)
        {
            string itemName = null;
            if (content != null)
            {
                try { itemName = content.iconTextView?.nameText?.text; } catch { }
            }
            itemName = TextUtils.StripIconMarkup(itemName);

            if (string.IsNullOrEmpty(itemName))
            {
                FFI_ScreenReaderMod.SpeakText(T("Empty"), interrupt: true);
                return;
            }

            string price = ExtractPrice(content);

            ShopMenuTracker.LastItemName = itemName;
            ShopMenuTracker.LastItemPrice = price;

            try
            {
                int contentId = content.ContentId;
                ShopMenuTracker.LastItemContentId = contentId;
                ShopMenuTracker.LastItemContentType = ResolveShopItemType(contentId);
            }
            catch
            {
                ShopMenuTracker.LastItemContentId = 0;
                ShopMenuTracker.LastItemContentType = -1;
            }

            string baseAnnouncement = string.IsNullOrEmpty(price) ? itemName : $"{itemName}, {price}";

            string announcement = baseAnnouncement;
            if (FFI_ScreenReaderMod.AutoDetailEnabled)
            {
                string detail = null;
                try { detail = ShopMenuTracker.GetDescriptionFromUI(); } catch { }
                if (!string.IsNullOrWhiteSpace(detail))
                    announcement = $"{baseAnnouncement}: {detail}";
            }

            FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
        }

        private static string ExtractPrice(ShopListItemContentController content)
        {
            if (content == null) return null;

            string price = null;

            try
            {
                var viewProp = content.GetType().GetProperty("shopListItemContentView",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (viewProp != null)
                {
                    var view = viewProp.GetValue(content);
                    if (view != null)
                    {
                        var priceTextProp = view.GetType().GetProperty("priceText",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (priceTextProp != null)
                        {
                            var priceText = priceTextProp.GetValue(view) as UnityEngine.UI.Text;
                            price = priceText?.text;
                        }
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(price))
            {
                try
                {
                    var viewField = content.GetType().GetField("shopListItemContentView",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (viewField != null)
                    {
                        var view = viewField.GetValue(content);
                        if (view != null)
                        {
                            var priceTextField = view.GetType().GetField("priceText",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (priceTextField != null)
                            {
                                var priceText = priceTextField.GetValue(view) as UnityEngine.UI.Text;
                                price = priceText?.text;
                            }
                        }
                    }
                }
                catch { }
            }

            if (string.IsNullOrEmpty(price))
            {
                try
                {
                    IntPtr controllerPtr = content.Pointer;
                    if (controllerPtr != IntPtr.Zero)
                    {
                        IntPtr viewPtr = IL2CppFieldReader.ReadPointer(controllerPtr, IL2CppOffsets.Shop.ShopListItemContentView);
                        if (viewPtr != IntPtr.Zero)
                        {
                            IntPtr priceTextPtr = IL2CppFieldReader.ReadPointer(viewPtr, IL2CppOffsets.Shop.PriceText);
                            if (priceTextPtr != IntPtr.Zero)
                            {
                                var priceTextComponent = new UnityEngine.UI.Text(priceTextPtr);
                                price = priceTextComponent?.text;
                            }
                        }
                    }
                }
                catch { }
            }

            return price;
        }

        /// <summary>
        /// Called when cursor moves on the shop command menu.
        /// Announces command name (Buy, Sell, Equipment, Back).
        /// </summary>
        public static void CommandSetCursor_Postfix(ShopCommandMenuController __instance, int index)
        {
            try
            {
                if (__instance == null)
                    return;

                if (ShopMenuTracker.EnteredEquipmentSubmenu)
                {
                    ShopMenuTracker.EnteredEquipmentSubmenu = false;
                    ShopMenuTracker.IsShopMenuActive = true;
                    return;
                }

                FFI_ScreenReaderMod.ClearOtherMenuStates("Shop");
                if (!ShopMenuTracker.IsShopMenuActive)
                {
                    var shopController = UnityEngine.Object.FindObjectOfType<ShopController>();
                    if (shopController != null)
                    {
                        ShopMenuTracker.SetCachedController(shopController);
                    }
                }
                ShopMenuTracker.IsShopMenuActive = true;

                var contentList = __instance.contentList;
                if (contentList == null || index < 0 || index >= contentList.Count)
                {
                    return;
                }

                var commandContent = contentList[index];
                if (commandContent == null)
                    return;

                string commandName = GetCommandName(commandContent.CommandId);
                if (string.IsNullOrEmpty(commandName))
                    return;

                FFI_ScreenReaderMod.SpeakText(commandName, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in CommandSetCursor_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Resolves whether a shop item ContentId is a weapon (2), armor (3), or unknown (-1).
        /// Probes MasterManager for Weapon first, then Armor.
        /// </summary>
        private static int ResolveShopItemType(int contentId)
        {
            if (contentId <= 0) return -1;
            try
            {
                var mm = Il2CppLast.Data.Master.MasterManager.Instance;
                if (mm == null) return -1;

                var weapon = mm.GetData<Il2CppLast.Data.Master.Weapon>(contentId);
                if (weapon != null) return 2; // CONTENT_TYPE_WEAPON

                var armor = mm.GetData<Il2CppLast.Data.Master.Armor>(contentId);
                if (armor != null) return 3; // CONTENT_TYPE_ARMOR
            }
            catch { }
            return -1;
        }

        private static string GetCommandName(ShopCommandId commandId)
        {
            return commandId switch
            {
                ShopCommandId.Buy => T("Buy"),
                ShopCommandId.Sell => T("Sell"),
                ShopCommandId.Equipment => T("Equipment"),
                ShopCommandId.Back => T("Back"),
                _ => null
            };
        }

        #endregion

        #region Trade Window

        private const int OFFSET_SELECTED_COUNT = IL2CppOffsets.Shop.SelectedCount;
        private const int OFFSET_TRADE_VIEW = IL2CppOffsets.Shop.TradeView;
        private const int OFFSET_TOTAL_PRICE_TEXT = IL2CppOffsets.Shop.TotalPriceText;

        /// <summary>
        /// Fires once when the trade window opens (buy or sell confirm). Announces
        /// the starting quantity and total.
        /// </summary>
        public static void TradeWindowShow_Postfix(ShopTradeWindowController __instance)
        {
            AnnounceQuantity(__instance);
        }

        /// <summary>
        /// Fires when the user increments (AddCount) or decrements (TakeCount) the
        /// quantity. Each call is a discrete user event, so we announce unconditionally —
        /// a no-op press against max/min replays the same value, which matches buy-menu
        /// feedback behavior.
        /// </summary>
        public static void TradeWindowCount_Postfix(ShopTradeWindowController __instance)
        {
            AnnounceQuantity(__instance);
        }

        private static void AnnounceQuantity(ShopTradeWindowController controller)
        {
            try
            {
                if (controller == null) return;
                if (ShopMenuTracker.IsInMagicSlotSelection) return;

                int selectedCount = GetSelectedCount(controller);
                string totalPrice = GetTotalPriceText(controller);

                string announcement = string.IsNullOrEmpty(totalPrice)
                    ? string.Format(T("Quantity: {0}"), selectedCount)
                    : string.Format(T("Quantity: {0}, Total: {1}"), selectedCount, totalPrice);

                FFI_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error announcing quantity: {ex.Message}");
            }
        }

        private static int GetSelectedCount(ShopTradeWindowController controller)
        {
            try
            {
                IntPtr ptr = controller.Pointer;
                if (ptr != IntPtr.Zero)
                {
                    return IL2CppFieldReader.ReadInt32(ptr, OFFSET_SELECTED_COUNT);
                }
            }
            catch { } // IL2CPP pointer read may fail during transitions
            return 0;
        }

        private static string GetTotalPriceText(ShopTradeWindowController controller)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return null;

                IntPtr viewPtr = IL2CppFieldReader.ReadPointer(controllerPtr, OFFSET_TRADE_VIEW);
                if (viewPtr == IntPtr.Zero)
                    return null;

                IntPtr textPtr = IL2CppFieldReader.ReadPointer(viewPtr, OFFSET_TOTAL_PRICE_TEXT);
                if (textPtr == IntPtr.Zero)
                    return null;

                var textComponent = new UnityEngine.UI.Text(textPtr);
                return textComponent?.text;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Shop] GetTotalPriceText pointer access failed: {ex.Message}");
            }

            try
            {
                var viewField = controller.GetType().GetField("view",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                if (viewField != null)
                {
                    var view = viewField.GetValue(controller);
                    if (view != null)
                    {
                        var priceTextField = view.GetType().GetField("totarlPriceText",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        if (priceTextField != null)
                        {
                            var priceText = priceTextField.GetValue(view) as UnityEngine.UI.Text;
                            if (priceText != null)
                            {
                                return priceText.text;
                            }
                        }
                    }
                }
            }
            catch { } // Reflection fallback; pointer method preferred
            return null;
        }

        #endregion

        #region Shop Close

        public static void ShopClose_Postfix()
        {
            _cachedMainList = null;
            ShopMenuTracker.ClearState();
        }

        #endregion

        /// <summary>
        /// Resets shop tracking state when leaving.
        /// Called from ShopMenuTracker.ClearState().
        /// </summary>
        public static void ResetQuantityTracking()
        {
            ShopMagicPatches.ResetState();
        }
    }
}
