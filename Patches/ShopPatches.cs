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
using ShopListItemContentController = Il2CppLast.UI.KeyInput.ShopListItemContentController;
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
                PatchItemSetFocus(harmony);
                PatchCommandSetFocus(harmony);
                PatchTradeWindow(harmony);
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

        private static void PatchItemSetFocus(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(ShopListItemContentController);
                var setFocusMethod = controllerType.GetMethod("SetFocus", new Type[] { typeof(bool) });

                if (setFocusMethod == null)
                {
                    MelonLogger.Warning("[Shop] Could not find ShopListItemContentController.SetFocus(bool)");
                    return;
                }

                harmony.Patch(setFocusMethod,
                    postfix: new HarmonyMethod(typeof(ShopPatches), nameof(ItemSetFocus_Postfix)));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch item SetFocus: {ex.Message}");
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

        private static void PatchTradeWindow(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type tradeType = typeof(ShopTradeWindowController);

                var updateMethod = tradeType.GetMethod("UpdateCotroller", new Type[] { typeof(bool) });
                if (updateMethod == null)
                {
                    updateMethod = tradeType.GetMethod("UpdateCotroller", Type.EmptyTypes);
                }

                if (updateMethod != null)
                {
                    harmony.Patch(updateMethod,
                        postfix: new HarmonyMethod(typeof(ShopPatches), nameof(UpdateCotroller_Postfix)));
                }
                else
                {
                    MelonLogger.Warning("[Shop] Could not find ShopTradeWindowController.UpdateCotroller");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch trade window: {ex.Message}");
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
        /// Called when an item in the shop list gains/loses focus.
        /// Announces item name, price, and caches description and stats for 'I' key.
        /// </summary>
        public static void ItemSetFocus_Postfix(ShopListItemContentController __instance, bool isFocus)
        {
            try
            {
                if (!isFocus || __instance == null)
                    return;

                // If spell list receives focus while IsInMagicSlotSelection is set,
                // user has backed out of the spell slot grid
                if (ShopMenuTracker.IsInMagicSlotSelection)
                {
                    ShopMenuTracker.IsInMagicSlotSelection = false;
                    ShopMagicPatches.ResetState();
                }

                FFI_ScreenReaderMod.ClearOtherMenuStates("Shop");
                ShopMenuTracker.IsShopMenuActive = true;

                string itemName = null;
                try
                {
                    itemName = __instance.iconTextView?.nameText?.text;
                }
                catch { } // IL2CPP field may not resolve

                if (string.IsNullOrEmpty(itemName))
                    return;

                itemName = TextUtils.StripIconMarkup(itemName);

                // Get price from shopListItemContentView
                string price = null;
                try
                {
                    var viewProp = __instance.GetType().GetProperty("shopListItemContentView",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (viewProp != null)
                    {
                        var view = viewProp.GetValue(__instance);
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
                catch { } // IL2CPP property access may fail

                // Fallback 1: Try field access
                if (string.IsNullOrEmpty(price))
                {
                    try
                    {
                        var viewField = __instance.GetType().GetField("shopListItemContentView",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (viewField != null)
                        {
                            var view = viewField.GetValue(__instance);
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
                    catch { } // IL2CPP field access may fail
                }

                // Fallback 2: Try pointer-based access
                if (string.IsNullOrEmpty(price))
                {
                    try
                    {
                        IntPtr controllerPtr = __instance.Pointer;
                        if (controllerPtr != IntPtr.Zero)
                        {
                            IntPtr viewPtr = IL2CppFieldReader.ReadPointer(controllerPtr, 0x30);
                            if (viewPtr != IntPtr.Zero)
                            {
                                IntPtr priceTextPtr = IL2CppFieldReader.ReadPointer(viewPtr, 0x18);
                                if (priceTextPtr != IntPtr.Zero)
                                {
                                    var priceTextComponent = new UnityEngine.UI.Text(priceTextPtr);
                                    price = priceTextComponent?.text;
                                }
                            }
                        }
                    }
                    catch { } // IL2CPP pointer offset may be invalid
                }

                ShopMenuTracker.LastItemName = itemName;
                ShopMenuTracker.LastItemPrice = price;

                string announcement = string.IsNullOrEmpty(price) ? itemName : $"{itemName}, {price}";

                AnnouncementHelper.AnnounceIfNew(AnnouncementContexts.SHOP_ITEM, announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in ItemSetFocus_Postfix: {ex.Message}");
            }
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

                AnnouncementHelper.AnnounceIfNew(AnnouncementContexts.SHOP_COMMAND, commandName);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in CommandSetCursor_Postfix: {ex.Message}");
            }
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
        /// Called when the trade window updates (after quantity changes).
        /// Announces the current quantity and total price.
        /// </summary>
        public static void UpdateCotroller_Postfix(ShopTradeWindowController __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                if (ShopMenuTracker.IsInMagicSlotSelection)
                    return;

                int selectedCount = GetSelectedCount(__instance);

                if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.SHOP_QUANTITY, selectedCount))
                    return;

                string totalPrice = GetTotalPriceText(__instance);

                string announcement = string.IsNullOrEmpty(totalPrice)
                    ? string.Format(T("Quantity: {0}"), selectedCount)
                    : string.Format(T("Quantity: {0}, Total: {1}"), selectedCount, totalPrice);

                FFI_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in UpdateCotroller_Postfix: {ex.Message}");
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
            ShopMenuTracker.ClearState();
        }

        #endregion

        /// <summary>
        /// Resets shop tracking state when leaving.
        /// Called from ShopMenuTracker.ClearState().
        /// </summary>
        public static void ResetQuantityTracking()
        {
            AnnouncementDeduplicator.Reset(AnnouncementContexts.SHOP_ITEM, AnnouncementContexts.SHOP_COMMAND, AnnouncementContexts.SHOP_QUANTITY, AnnouncementContexts.SHOP_SLOT);
            ShopMagicPatches.ResetState();
        }
    }
}
