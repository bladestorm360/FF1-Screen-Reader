using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

// FF1 Shop UI types (root namespace in IL2CPP)
using ShopController = Il2CppLast.UI.KeyInput.ShopController;
using ShopListItemContentController = Il2CppLast.UI.KeyInput.ShopListItemContentController;
using ShopTradeWindowController = Il2CppLast.UI.KeyInput.ShopTradeWindowController;
using ShopCommandMenuContentController = Il2CppLast.UI.KeyInput.ShopCommandMenuContentController;
using ShopCommandMenuController = Il2CppLast.UI.KeyInput.ShopCommandMenuController;
using ShopMagicTargetSelectController = Il2CppLast.UI.KeyInput.ShopMagicTargetSelectController;
using ShopMagicTargetSelectContentController = Il2CppLast.UI.KeyInput.ShopMagicTargetSelectContentController;
using ShopCommandId = Il2CppLast.Defaine.ShopCommandId;
using GameCursor = Il2CppLast.UI.Cursor;

// Master data types for item stats
using MasterManager = Il2CppLast.Data.Master.MasterManager;
using Weapon = Il2CppLast.Data.Master.Weapon;
using Armor = Il2CppLast.Data.Master.Armor;
using Content = Il2CppLast.Data.Master.Content;
using ContentType = Il2CppLast.Defaine.Content.ContentType;
using MessageManager = Il2CppLast.Management.MessageManager;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Tracks shop menu state for 'I' key description access and suppression.
    /// Uses state machine validation to only suppress during item list navigation,
    /// not during command menu (Buy/Sell/Exit) navigation.
    /// </summary>
    public static class ShopMenuTracker
    {
        public static bool IsShopMenuActive { get; set; }
        public static string LastItemName { get; set; }
        public static string LastItemDescription { get; set; }
        public static string LastItemPrice { get; set; }
        public static string LastItemStats { get; set; }

        /// <summary>
        /// Tracks when we're actively in the magic spell slot selection grid.
        /// Set by MagicTargetShow_Postfix, cleared by MagicTargetSetFocus_Postfix(false) or shop close.
        /// Used to suppress item list announcements during spell slot navigation.
        /// </summary>
        public static bool IsInMagicSlotSelection { get; set; }

        // Cached controller reference to avoid expensive FindObjectOfType calls
        private static ShopController cachedShopController = null;

        // State machine offset - use centralized offsets
        private const int OFFSET_STATE_MACHINE = IL2CppOffsets.MenuStateMachine.Shop;

        // ShopController.State values (from dump.cs line 423102-423116)
        private const int STATE_NONE = 0;
        private const int STATE_SELECT_COMMAND = 1;           // Command bar (Buy/Sell/Equipment/Back)
        private const int STATE_SELECT_PRODUCT = 2;           // Buy item list
        private const int STATE_SELECT_SELL_ITEM = 3;         // Sell item list
        private const int STATE_SELECT_ABILITY_TARGET = 4;    // Magic shop character select
        private const int STATE_SELECT_EQUIPMENT = 5;         // Equipment sub-menu
        private const int STATE_CONFIRMATION_BUY_ITEM = 6;    // Buy quantity confirmation
        private const int STATE_CONFIRMATION_SELL_ITEM = 7;   // Sell quantity confirmation
        private const int STATE_CONFIRMATION_FORGET_MAGIC = 8; // Magic forget confirmation
        private const int STATE_CONFIRMATION_BUY_MAGIC = 9;   // Magic buy confirmation

        /// <summary>
        /// Sets the cached shop controller reference.
        /// Called by shop patches when they detect the controller.
        /// </summary>
        public static void SetCachedController(ShopController controller)
        {
            cachedShopController = controller;
        }

        /// <summary>
        /// Validates that shop menu is active and should suppress generic cursor.
        /// ShopPatches handles ALL shop states including command menu for consistency.
        /// - Command menu (STATE_SELECT_COMMAND): Suppressed - CommandSetFocus_Postfix handles it
        /// - Item lists: Suppressed - ItemSetFocus_Postfix handles with price info
        /// - Quantity: Suppressed - UpdateCotroller_Postfix handles
        /// - Magic slot selection: Suppressed - MagicSelectContent_Postfix handles
        /// Uses cached controller reference to avoid expensive FindObjectOfType calls.
        /// </summary>
        public static bool ValidateState()
        {
            // Magic slot selection always suppresses - this flag is set by MagicSelectContent_Postfix
            // and persists during slot navigation even if SetFocus(false) is called
            if (IsInMagicSlotSelection)
                return true;

            if (!IsShopMenuActive)
                return false;

            try
            {
                // Use cached controller - much faster than FindObjectOfType
                // Validate it's still alive (IL2CPP objects can be destroyed)
                if (cachedShopController != null)
                {
                    try
                    {
                        // Check if the IL2CPP object is still valid
                        if (cachedShopController.Pointer == IntPtr.Zero)
                        {
                            cachedShopController = null;
                        }
                    }
                    catch
                    {
                        cachedShopController = null;
                    }
                }

                if (cachedShopController != null)
                {
                    int currentState = GetCurrentState(cachedShopController);

                    // STATE_NONE means shop is closing/closed
                    if (currentState == STATE_NONE)
                    {
                        ClearState();
                        return false;
                    }

                    // All shop states are suppressed - ShopPatches handles everything
                    // Command menu: CommandSetFocus_Postfix announces Buy/Sell/Equipment/Back
                    // Item lists: ItemSetFocus_Postfix announces with price
                    // Confirmations: UpdateCotroller_Postfix announces quantity/total
                    if (currentState == STATE_SELECT_COMMAND ||
                        currentState == STATE_SELECT_PRODUCT ||
                        currentState == STATE_SELECT_SELL_ITEM ||
                        currentState == STATE_SELECT_ABILITY_TARGET ||
                        currentState == STATE_SELECT_EQUIPMENT ||
                        currentState == STATE_CONFIRMATION_BUY_ITEM ||
                        currentState == STATE_CONFIRMATION_SELL_ITEM ||
                        currentState == STATE_CONFIRMATION_FORGET_MAGIC ||
                        currentState == STATE_CONFIRMATION_BUY_MAGIC)
                    {
                        return true;
                    }
                }

                // No cached controller and IsShopMenuActive is true -
                // trust the flag (set by shop patches) but don't call FindObjectOfType
                // This avoids the expensive search and is safe because the flag is only
                // set when our patches actually fire on a valid controller
                return IsShopMenuActive;
            }
            catch
            {
                ClearState();
                return false;
            }
        }

        /// <summary>
        /// Reads the current state from ShopController's state machine.
        /// Returns -1 if unable to read.
        /// </summary>
        public static int GetCurrentState(ShopController controller)
        {
            if (controller == null)
                return -1;
            return StateMachineHelper.ReadState(controller.Pointer, OFFSET_STATE_MACHINE);
        }

        public static void ClearState()
        {
            IsShopMenuActive = false;
            IsInMagicSlotSelection = false;
            cachedShopController = null;
            LastItemName = null;
            LastItemDescription = null;
            LastItemPrice = null;
            LastItemStats = null;
            ShopPatches.ResetQuantityTracking();
        }
    }

    /// <summary>
    /// Announces shop item details when 'I' key is pressed.
    /// Announces stats first, then description.
    /// Format: "Defense 3, Magic Defense 1. Armor made of leather."
    /// </summary>
    public static class ShopDetailsAnnouncer
    {
        public static void AnnounceCurrentItemDetails()
        {
            try
            {
                if (!ShopMenuTracker.ValidateState())
                {
                    return;
                }

                // Build announcement: Stats first, then description
                string stats = ShopMenuTracker.LastItemStats;
                string description = ShopMenuTracker.LastItemDescription;

                string announcement = "";

                // Add stats if available
                if (!string.IsNullOrEmpty(stats))
                {
                    announcement = stats;
                }

                // Add description
                if (!string.IsNullOrEmpty(description))
                {
                    if (!string.IsNullOrEmpty(announcement))
                    {
                        announcement += ". " + description;
                    }
                    else
                    {
                        announcement = description;
                    }
                }

                if (string.IsNullOrEmpty(announcement))
                {
                    announcement = "No item details available";
                }

                MelonLogger.Msg($"[Shop Details] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing shop details: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Shop menu patches using manual Harmony patching.
    /// Patches item list focus, command menu focus, and quantity changes.
    /// </summary>
    public static class ShopPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch ShopListItemContentController.SetFocus(bool) - item selection (buy/sell lists)
                PatchItemSetFocus(harmony);

                // Patch ShopCommandMenuContentController.SetFocus(bool) - command bar (Buy/Sell/Equipment/Back)
                PatchCommandSetFocus(harmony);

                // Patch ShopTradeWindowController.UpdateCotroller(bool) - quantity changes
                // Note: FF1 KeyInput has UpdateCotroller(bool isCount = True)
                PatchTradeWindow(harmony);

                // Patch ShopController.Close() - clears state when shop closes
                PatchShopClose(harmony);

                // Patch ShopMagicTargetSelectController.SetCursor - magic shop character selection
                PatchMagicTargetSetCursor(harmony);

                MelonLogger.Msg("[Shop] All shop patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to apply shop patches: {ex.Message}");
            }
        }

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

                MelonLogger.Msg("[Shop] Patched ShopListItemContentController.SetFocus");
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
                // Patch ShopCommandMenuController.SetCursor(int index) - the parent controller that manages command menu
                // ShopCommandMenuContentController doesn't have SetFocus, it's the individual item
                Type controllerType = typeof(ShopCommandMenuController);
                var setCursorMethod = controllerType.GetMethod("SetCursor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, new Type[] { typeof(int) }, null);

                if (setCursorMethod == null)
                {
                    MelonLogger.Warning("[Shop] Could not find ShopCommandMenuController.SetCursor(int)");
                    return;
                }

                harmony.Patch(setCursorMethod,
                    postfix: new HarmonyMethod(typeof(ShopPatches), nameof(CommandSetCursor_Postfix)));

                MelonLogger.Msg("[Shop] Patched ShopCommandMenuController.SetCursor");
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

                // FF1 KeyInput: UpdateCotroller(bool isCount = True)
                var updateMethod = tradeType.GetMethod("UpdateCotroller", new Type[] { typeof(bool) });
                if (updateMethod == null)
                {
                    // Fallback: try parameterless version
                    updateMethod = tradeType.GetMethod("UpdateCotroller", Type.EmptyTypes);
                }

                if (updateMethod != null)
                {
                    harmony.Patch(updateMethod,
                        postfix: new HarmonyMethod(typeof(ShopPatches), nameof(UpdateCotroller_Postfix)));
                    MelonLogger.Msg("[Shop] Patched ShopTradeWindowController.UpdateCotroller");
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
                    MelonLogger.Msg("[Shop] Patched ShopController.Close");
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

        /// <summary>
        /// Patches ShopMagicTargetSelectController.SelectContent for magic shop spell slot selection.
        /// Announces character name, slot number, and spell name when navigating spell slots.
        /// </summary>
        private static void PatchMagicTargetSetCursor(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(ShopMagicTargetSelectController);

                // Patch SelectContent(Cursor, List<ShopGetMagicContentController>) - called during navigation
                MethodInfo selectContentMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "SelectContent")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 2 && parameters[0].ParameterType.Name == "Cursor")
                        {
                            selectContentMethod = method;
                            break;
                        }
                    }
                }

                if (selectContentMethod != null)
                {
                    harmony.Patch(selectContentMethod,
                        postfix: new HarmonyMethod(typeof(ShopPatches), nameof(MagicSelectContent_Postfix)));
                    MelonLogger.Msg("[Shop] Patched ShopMagicTargetSelectController.SelectContent");
                }
                else
                {
                    MelonLogger.Warning("[Shop] Could not find ShopMagicTargetSelectController.SelectContent");
                }

                // Also patch SetFocus to announce initial slot when menu opens
                var setFocusMethod = controllerType.GetMethod("SetFocus", new Type[] { typeof(bool) });
                if (setFocusMethod != null)
                {
                    harmony.Patch(setFocusMethod,
                        postfix: new HarmonyMethod(typeof(ShopPatches), nameof(MagicTargetSetFocus_Postfix)));
                    MelonLogger.Msg("[Shop] Patched ShopMagicTargetSelectController.SetFocus");
                }

                // Patch Show() to detect when no characters can learn the spell
                // Show(ShopProductData, List<OwnedCharacterData>, Action, Action<string>, Action)
                MethodInfo showMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "Show")
                    {
                        var parameters = method.GetParameters();
                        // KeyInput version has 5 params including Action<string> onSelected
                        if (parameters.Length == 5)
                        {
                            showMethod = method;
                            break;
                        }
                    }
                }

                if (showMethod != null)
                {
                    harmony.Patch(showMethod,
                        postfix: new HarmonyMethod(typeof(ShopPatches), nameof(MagicTargetShow_Postfix)));
                    MelonLogger.Msg("[Shop] Patched ShopMagicTargetSelectController.Show");
                }
                else
                {
                    MelonLogger.Warning("[Shop] Could not find ShopMagicTargetSelectController.Show with 5 params");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch magic target: {ex.Message}");
            }
        }

        // ============ Postfix Methods ============

        // Track last announcement to prevent duplicates (string-only deduplication)
        private static string lastAnnouncedItemText = "";

        // Track last command to prevent duplicates
        private static string lastAnnouncedCommand = "";

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
                // user has backed out of the spell slot grid - clear the flag
                if (ShopMenuTracker.IsInMagicSlotSelection)
                {
                    MelonLogger.Msg("[Shop] Spell list focus received - clearing magic slot selection flag");
                    ShopMenuTracker.IsInMagicSlotSelection = false;
                    // Also reset character tracking for next spell slot entry
                    lastAnnouncedCharacterId = -1;
                }

                // Mark shop as active and clear other menu states
                FFI_ScreenReaderMod.ClearOtherMenuStates("Shop");
                ShopMenuTracker.IsShopMenuActive = true;

                // Get item name from iconTextView
                string itemName = null;
                try
                {
                    itemName = __instance.iconTextView?.nameText?.text;
                }
                catch { }

                if (string.IsNullOrEmpty(itemName))
                    return;

                // Strip any icon markup
                itemName = TextUtils.StripIconMarkup(itemName);

                // Get price from shopListItemContentView (FF1 field name)
                // FF1 KeyInput: shopListItemContentView at offset 0x30, priceText at offset 0x18
                string price = null;
                try
                {
                    // Try direct IL2CPP property access first
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
                catch (Exception ex)
                {
                    MelonLogger.Msg($"[Shop] Price property access failed: {ex.Message}");
                }

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
                    catch { }
                }

                // Fallback 2: Try pointer-based access (offsets from dump.cs)
                if (string.IsNullOrEmpty(price))
                {
                    try
                    {
                        IntPtr controllerPtr = __instance.Pointer;
                        if (controllerPtr != IntPtr.Zero)
                        {
                            unsafe
                            {
                                // shopListItemContentView at offset 0x30
                                IntPtr viewPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + 0x30);
                                if (viewPtr != IntPtr.Zero)
                                {
                                    // priceText at offset 0x18 in ShopListItemContentView
                                    IntPtr priceTextPtr = *(IntPtr*)((byte*)viewPtr.ToPointer() + 0x18);
                                    if (priceTextPtr != IntPtr.Zero)
                                    {
                                        var priceTextComponent = new UnityEngine.UI.Text(priceTextPtr);
                                        price = priceTextComponent?.text;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg($"[Shop] Price pointer access failed: {ex.Message}");
                    }
                }

                // Log price reading result for debugging
                if (!string.IsNullOrEmpty(price))
                {
                    MelonLogger.Msg($"[Shop] Price found: {price}");
                }
                else
                {
                    MelonLogger.Msg($"[Shop] Price not found for item: {itemName}");
                }

                // Get description from Message property (cached for 'I' key)
                string description = null;
                try
                {
                    description = __instance.Message;
                }
                catch { }

                // Get item stats from master data (cached for 'I' key)
                string stats = null;
                try
                {
                    int contentId = __instance.ContentId;
                    if (contentId > 0)
                    {
                        stats = GetItemStats(contentId);
                    }
                }
                catch { }

                // Cache for 'I' key
                ShopMenuTracker.LastItemName = itemName;
                ShopMenuTracker.LastItemPrice = price;
                ShopMenuTracker.LastItemDescription = description;
                ShopMenuTracker.LastItemStats = stats;

                // Build announcement: "Item Name, Price"
                string announcement = string.IsNullOrEmpty(price) ? itemName : $"{itemName}, {price}";

                // String-only deduplication - skip if same announcement
                if (announcement == lastAnnouncedItemText)
                {
                    return;
                }

                lastAnnouncedItemText = announcement;

                MelonLogger.Msg($"[Shop Item] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement);
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
                MelonLogger.Msg($"[Shop Command] SetCursor called, index={index}");

                if (__instance == null)
                    return;

                // Mark shop as active and cache controller for fast validation
                FFI_ScreenReaderMod.ClearOtherMenuStates("Shop");
                if (!ShopMenuTracker.IsShopMenuActive)
                {
                    // First entry to shop - cache the controller for fast ValidateState checks
                    var shopController = UnityEngine.Object.FindObjectOfType<ShopController>();
                    if (shopController != null)
                    {
                        ShopMenuTracker.SetCachedController(shopController);
                    }
                }
                ShopMenuTracker.IsShopMenuActive = true;

                // Get command from contentList at index
                var contentList = __instance.contentList;
                if (contentList == null || index < 0 || index >= contentList.Count)
                {
                    MelonLogger.Msg($"[Shop Command] Invalid index {index} or null contentList");
                    return;
                }

                var commandContent = contentList[index];
                if (commandContent == null)
                    return;

                // Get command name from CommandId
                string commandName = GetCommandName(commandContent.CommandId);
                MelonLogger.Msg($"[Shop Command] CommandId={commandContent.CommandId}, Name={commandName}");

                if (string.IsNullOrEmpty(commandName))
                    return;

                // String-only deduplication
                if (commandName == lastAnnouncedCommand)
                {
                    MelonLogger.Msg($"[Shop Command] Skipping duplicate: {commandName}");
                    return;
                }

                lastAnnouncedCommand = commandName;

                MelonLogger.Msg($"[Shop Command] Announcing: {commandName}");
                FFI_ScreenReaderMod.SpeakText(commandName);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in CommandSetCursor_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Maps ShopCommandId enum to readable string.
        /// </summary>
        private static string GetCommandName(ShopCommandId commandId)
        {
            return commandId switch
            {
                ShopCommandId.Buy => "Buy",
                ShopCommandId.Sell => "Sell",
                ShopCommandId.Equipment => "Equipment",
                ShopCommandId.Back => "Back",
                _ => null
            };
        }

        /// <summary>
        /// Gets item stats by looking up master data.
        /// ContentId is the Content system ID - we need to look up the Content
        /// to get TypeValue (the actual weapon/armor ID for master data).
        /// </summary>
        private static string GetItemStats(int contentId)
        {
            try
            {
                var masterManager = MasterManager.Instance;
                if (masterManager == null)
                    return null;

                // Look up the Content to get the actual item ID (TypeValue) and type (TypeId)
                var content = masterManager.GetData<Content>(contentId);
                if (content == null)
                    return null;

                int typeId = content.TypeId;           // ContentType: 1=Item, 2=Weapon, 3=Armor
                int actualItemId = content.TypeValue;  // The actual weapon/armor/item ID

                // Look up stats based on content type
                switch ((ContentType)typeId)
                {
                    case ContentType.Weapon:
                        return GetWeaponStats(masterManager, actualItemId);

                    case ContentType.Armor:
                        return GetArmorStats(masterManager, actualItemId);

                    default:
                        // Regular items and other types don't have equipment stats
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets stats string for a weapon.
        /// Format: "Attack X" (with optional additional stats)
        /// </summary>
        private static string GetWeaponStats(MasterManager masterManager, int weaponId)
        {
            try
            {
                var weapon = masterManager.GetData<Weapon>(weaponId);
                if (weapon == null)
                    return null;

                var stats = new List<string>();

                int attack = weapon.Attack;
                if (attack > 0)
                    stats.Add($"Attack {attack}");

                int accuracy = weapon.AccuracyRate;
                if (accuracy > 0)
                    stats.Add($"Accuracy {accuracy}");

                int evasion = weapon.EvasionRate;
                if (evasion > 0)
                    stats.Add($"Evasion {evasion}");

                // Stat bonuses
                if (weapon.Strength > 0) stats.Add($"Strength +{weapon.Strength}");
                if (weapon.Vitality > 0) stats.Add($"Vitality +{weapon.Vitality}");
                if (weapon.Agility > 0) stats.Add($"Agility +{weapon.Agility}");
                if (weapon.Intelligence > 0) stats.Add($"Intelligence +{weapon.Intelligence}");
                if (weapon.Spirit > 0) stats.Add($"Spirit +{weapon.Spirit}");
                if (weapon.Magic > 0) stats.Add($"Magic +{weapon.Magic}");

                return stats.Count > 0 ? string.Join(", ", stats) : null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Shop] Error getting weapon stats: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets stats string for armor.
        /// Format: "Defense X, Magic Defense Y" (with optional additional stats)
        /// </summary>
        private static string GetArmorStats(MasterManager masterManager, int armorId)
        {
            try
            {
                var armor = masterManager.GetData<Armor>(armorId);
                if (armor == null)
                    return null;

                var stats = new List<string>();

                int defense = armor.Defense;
                if (defense > 0)
                    stats.Add($"Defense {defense}");

                int magicDefense = armor.AbilityDefense;
                if (magicDefense > 0)
                    stats.Add($"Magic Defense {magicDefense}");

                int evasion = armor.EvasionRate;
                if (evasion > 0)
                    stats.Add($"Evasion {evasion}");

                int magicEvasion = armor.AbilityEvasionRate;
                if (magicEvasion > 0)
                    stats.Add($"Magic Evasion {magicEvasion}");

                // Stat bonuses
                if (armor.Strength > 0) stats.Add($"Strength +{armor.Strength}");
                if (armor.Vitality > 0) stats.Add($"Vitality +{armor.Vitality}");
                if (armor.Agility > 0) stats.Add($"Agility +{armor.Agility}");
                if (armor.Intelligence > 0) stats.Add($"Intelligence +{armor.Intelligence}");
                if (armor.Spirit > 0) stats.Add($"Spirit +{armor.Spirit}");
                if (armor.Magic > 0) stats.Add($"Magic +{armor.Magic}");

                return stats.Count > 0 ? string.Join(", ", stats) : null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Shop] Error getting armor stats: {ex.Message}");
                return null;
            }
        }

        // Memory offsets - use centralized offsets
        private const int OFFSET_SELECTED_COUNT = IL2CppOffsets.Shop.SelectedCount;
        private const int OFFSET_TRADE_VIEW = IL2CppOffsets.Shop.TradeView;
        private const int OFFSET_TOTAL_PRICE_TEXT = IL2CppOffsets.Shop.TotalPriceText;

        // Track last quantity to avoid duplicate announcements
        private static int lastAnnouncedQuantity = -1;

        /// <summary>
        /// Called when the trade window updates (after quantity changes).
        /// Announces the current quantity and total price.
        /// FF1 KeyInput: UpdateCotroller(bool isCount = True)
        /// </summary>
        public static void UpdateCotroller_Postfix(ShopTradeWindowController __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                // Check if we're in a magic shop state - don't announce quantity for magic purchases
                // Magic shops select spell slots, not quantities
                if (IsInMagicShopState())
                {
                    return;
                }

                // Read selectedCount via pointer (offset 0x3C in FF1 KeyInput)
                int selectedCount = GetSelectedCount(__instance);

                // Skip if quantity hasn't changed (UpdateCotroller is called frequently)
                if (selectedCount == lastAnnouncedQuantity)
                    return;

                lastAnnouncedQuantity = selectedCount;

                // Read total price text via pointer chain
                string totalPrice = GetTotalPriceText(__instance);

                string announcement = string.IsNullOrEmpty(totalPrice)
                    ? $"Quantity: {selectedCount}"
                    : $"Quantity: {selectedCount}, Total: {totalPrice}";

                MelonLogger.Msg($"[Shop Quantity] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in UpdateCotroller_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if we're currently in magic spell slot selection mode.
        /// Uses the reliable IsInMagicSlotSelection flag set by our patches.
        /// Magic shops don't use quantity selection - they select spell slots.
        /// </summary>
        private static bool IsInMagicShopState()
        {
            // Use the flag set by MagicTargetShow_Postfix
            if (ShopMenuTracker.IsInMagicSlotSelection)
            {
                MelonLogger.Msg("[Shop] Magic slot selection active - suppressing item list");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Reads selectedCount from ShopTradeWindowController at offset 0x3C (FF1 KeyInput).
        /// </summary>
        private static int GetSelectedCount(ShopTradeWindowController controller)
        {
            try
            {
                unsafe
                {
                    IntPtr ptr = controller.Pointer;
                    if (ptr != IntPtr.Zero)
                    {
                        return *(int*)((byte*)ptr.ToPointer() + OFFSET_SELECTED_COUNT);
                    }
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Reads the total price text from the trade window view.
        /// Uses pointer chain: controller -> view -> totarlPriceText -> text
        /// </summary>
        private static string GetTotalPriceText(ShopTradeWindowController controller)
        {
            // Try pointer-based access (most reliable for IL2CPP)
            try
            {
                unsafe
                {
                    IntPtr controllerPtr = controller.Pointer;
                    if (controllerPtr == IntPtr.Zero)
                    {
                        MelonLogger.Msg("[Shop] GetTotalPriceText: controller pointer is null");
                        return null;
                    }

                    // Read view pointer at offset 0x30 (FF1 KeyInput)
                    IntPtr viewPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_TRADE_VIEW);
                    if (viewPtr == IntPtr.Zero)
                    {
                        MelonLogger.Msg("[Shop] GetTotalPriceText: view pointer is null");
                        return null;
                    }

                    // Read totarlPriceText pointer at offset 0x70
                    IntPtr textPtr = *(IntPtr*)((byte*)viewPtr.ToPointer() + OFFSET_TOTAL_PRICE_TEXT);
                    if (textPtr == IntPtr.Zero)
                    {
                        MelonLogger.Msg("[Shop] GetTotalPriceText: totarlPriceText pointer is null");
                        return null;
                    }

                    // Wrap as Text component and read text property
                    var textComponent = new UnityEngine.UI.Text(textPtr);
                    string text = textComponent?.text;
                    MelonLogger.Msg($"[Shop] GetTotalPriceText: read '{text}'");
                    return text;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Shop] GetTotalPriceText pointer access failed: {ex.Message}");
            }

            // Fallback: try reflection
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
            catch { }
            return null;
        }

        /// <summary>
        /// Called when the shop closes.
        /// Clears the shop active state to allow main menu announcements.
        /// </summary>
        public static void ShopClose_Postfix()
        {
            MelonLogger.Msg("[Shop] Close() - clearing state");
            ShopMenuTracker.ClearState();
        }

        // Memory offsets - use centralized offsets
        private const int OFFSET_SLOT_CHARACTER_ID = IL2CppOffsets.ShopMagic.CharacterId;
        private const int OFFSET_SLOT_TYPE = IL2CppOffsets.ShopMagic.SlotType;
        private const int OFFSET_SLOT_VIEW = IL2CppOffsets.ShopMagic.View;
        private const int OFFSET_SLOT_ICON_TEXT_VIEW = IL2CppOffsets.ShopMagic.View; // Same as view offset

        // Track last announced slot to prevent duplicates
        private static string lastAnnouncedSlot = "";
        // Track last character to only announce name when switching characters
        private static int lastAnnouncedCharacterId = -1;

        /// <summary>
        /// Called when SetFocus changes on magic target select.
        /// Resets tracking when menu opens/closes.
        /// </summary>
        public static void MagicTargetSetFocus_Postfix(ShopMagicTargetSelectController __instance, bool isFocus)
        {
            try
            {
                if (!isFocus)
                {
                    // Only reset slot tracking, NOT character ID
                    // Keeping lastAnnouncedCharacterId prevents double character name when navigating
                    // (SetFocus(false) fires immediately after Show() due to game timing quirk)
                    lastAnnouncedSlot = "";
                    // Don't clear IsInMagicSlotSelection here - it fires too early
                    // Flag is cleared in ItemSetFocus_Postfix when spell list receives focus
                    MelonLogger.Msg("[Shop Magic] SetFocus(false) called");
                    return;
                }

                MelonLogger.Msg("[Shop Magic] SetFocus(true) - spell slot menu opened");
                ShopMenuTracker.IsShopMenuActive = true;
                ShopMenuTracker.IsInMagicSlotSelection = true;
                lastAnnouncedSlot = ""; // Reset to force first announcement
                lastAnnouncedCharacterId = -1; // Reset to announce first character name
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in MagicTargetSetFocus_Postfix: {ex.Message}");
            }
        }

        // Offset - use centralized offsets
        private const int OFFSET_IS_FOUND_EQUIP_SLOT = IL2CppOffsets.ShopMagic.IsFoundEquipSlot;

        // Track if we already announced "no characters can learn" to prevent duplicates
        private static bool announcedNoLearnersThisSession = false;

        /// <summary>
        /// Called when the magic shop spell selection grid opens (Show method).
        /// Checks isFoundEquipSlot to detect when no characters can learn the spell.
        /// If no one can learn, announces "No characters can learn this spell".
        /// </summary>
        public static void MagicTargetShow_Postfix(ShopMagicTargetSelectController __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                // Set shop active for suppression
                ShopMenuTracker.IsShopMenuActive = true;
                // Set magic slot selection flag to suppress item list announcements
                ShopMenuTracker.IsInMagicSlotSelection = true;

                // Reset tracking for new spell selection
                lastAnnouncedSlot = "";
                lastAnnouncedCharacterId = -1;
                announcedNoLearnersThisSession = false;

                // Read isFoundEquipSlot at offset 0x70
                IntPtr controllerPtr = __instance.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return;

                bool isFoundEquipSlot;
                unsafe
                {
                    isFoundEquipSlot = *(bool*)((byte*)controllerPtr.ToPointer() + OFFSET_IS_FOUND_EQUIP_SLOT);
                }

                MelonLogger.Msg($"[Shop Magic] Show() called, isFoundEquipSlot = {isFoundEquipSlot}");

                if (!isFoundEquipSlot && !announcedNoLearnersThisSession)
                {
                    announcedNoLearnersThisSession = true;
                    string announcement = "No characters can learn this spell";
                    MelonLogger.Msg($"[Shop Magic] {announcement}");
                    FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in MagicTargetShow_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when navigating between spell slots in magic shop.
        /// Only announces character name when switching to a different character.
        /// Format when switching characters: "Character Name: Slot X: Spell Name"
        /// Format when same character: "Slot X: Spell Name"
        /// </summary>
        public static void MagicSelectContent_Postfix(
            ShopMagicTargetSelectController __instance,
            GameCursor targetCursor,
            Il2CppSystem.Collections.Generic.List<Il2CppLast.UI.KeyInput.ShopGetMagicContentController> contentList)
        {
            try
            {
                // Set shop active state for suppression - must be BEFORE null checks
                // This is critical because ShopMagicTargetSelectController has no SetFocus(bool) method,
                // so MagicTargetSetFocus_Postfix never fires. We set the flag here instead.
                ShopMenuTracker.IsShopMenuActive = true;
                // Also set magic slot selection flag - this persists during navigation
                // and is checked first in ValidateState() to ensure proper suppression
                ShopMenuTracker.IsInMagicSlotSelection = true;

                if (__instance == null || targetCursor == null || contentList == null)
                    return;

                int cursorIndex = targetCursor.Index;
                MelonLogger.Msg($"[Shop Magic] SelectContent called, cursor index: {cursorIndex}");

                if (cursorIndex < 0 || cursorIndex >= contentList.Count)
                {
                    MelonLogger.Msg($"[Shop Magic] Index {cursorIndex} out of range (count={contentList.Count})");
                    return;
                }

                var slotController = contentList[cursorIndex];
                if (slotController == null)
                    return;

                // Read slot data via pointer
                IntPtr slotPtr = slotController.Pointer;
                if (slotPtr == IntPtr.Zero)
                    return;

                int characterId;
                int slotType;
                unsafe
                {
                    characterId = *(int*)((byte*)slotPtr.ToPointer() + OFFSET_SLOT_CHARACTER_ID);
                    slotType = *(int*)((byte*)slotPtr.ToPointer() + OFFSET_SLOT_TYPE);
                }

                // Get spell name from view -> iconTextView -> nameText
                string spellName = GetSpellSlotName(slotPtr);
                if (string.IsNullOrEmpty(spellName))
                {
                    spellName = "Empty";
                }

                // Build announcement - only include character name when switching characters
                string announcement;
                bool characterChanged = (characterId != lastAnnouncedCharacterId);

                if (characterChanged)
                {
                    // Get character name from UserDataManager
                    string characterName = GetCharacterNameById(characterId);
                    if (string.IsNullOrEmpty(characterName))
                    {
                        characterName = $"Character {characterId}";
                    }

                    // Format: "Character Name: Slot X: Spell Name"
                    announcement = $"{characterName}: Slot {slotType}: {spellName}";
                    lastAnnouncedCharacterId = characterId;
                }
                else
                {
                    // Same character - just announce slot info
                    // Format: "Slot X: Spell Name"
                    announcement = $"Slot {slotType}: {spellName}";
                }

                // Skip if same announcement (prevents duplicate announcements)
                if (announcement == lastAnnouncedSlot)
                    return;

                lastAnnouncedSlot = announcement;

                MelonLogger.Msg($"[Shop Magic] Announcing: {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in MagicSelectContent_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets character name by ID from UserDataManager.
        /// </summary>
        private static string GetCharacterNameById(int characterId)
        {
            try
            {
                var userDataManager = Il2CppLast.Management.UserDataManager.Instance();
                if (userDataManager == null)
                    return null;

                var partyList = userDataManager.GetOwnedCharactersClone(false);
                if (partyList == null)
                    return null;

                foreach (var charData in partyList)
                {
                    if (charData != null && charData.Id == characterId)
                    {
                        return charData.Name;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Shop Magic] Error getting character name: {ex.Message}");
            }
            return null;
        }

        // Offset - use centralized offsets
        private const int OFFSET_SLOT_CONTENT_ID = IL2CppOffsets.ShopMagic.ContentId;

        /// <summary>
        /// Gets spell name from ShopGetMagicContentController using ContentId.
        /// If ContentId > 0, looks up the ability name from master data.
        /// Returns null if slot is empty (ContentId == 0).
        /// </summary>
        private static string GetSpellSlotName(IntPtr slotControllerPtr)
        {
            try
            {
                // Read ContentId at offset 0x20
                int contentId;
                unsafe
                {
                    contentId = *(int*)((byte*)slotControllerPtr.ToPointer() + OFFSET_SLOT_CONTENT_ID);
                }

                // ContentId of 0 means empty slot
                if (contentId == 0)
                    return null;

                // Look up the ability name from master data
                var masterManager = MasterManager.Instance;
                if (masterManager == null)
                    return null;

                // ContentId refers to Content table, which has MesIdName for localization
                var content = masterManager.GetData<Content>(contentId);
                if (content == null)
                    return null;

                // Get the message ID for the ability name
                string mesIdName = content.MesIdName;
                if (string.IsNullOrEmpty(mesIdName))
                    return null;

                // Look up the localized name using MessageManager
                var messageManager = MessageManager.Instance;
                if (messageManager == null)
                    return null;

                string abilityName = messageManager.GetMessage(mesIdName, false);
                if (!string.IsNullOrEmpty(abilityName))
                {
                    return TextUtils.StripIconMarkup(abilityName);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Shop Magic] Error getting spell name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Resets the quantity tracking when leaving the trade window.
        /// Called from ShopMenuTracker.ClearState().
        /// </summary>
        public static void ResetQuantityTracking()
        {
            lastAnnouncedQuantity = -1;
        }
    }
}
