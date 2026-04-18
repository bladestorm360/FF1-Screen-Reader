using System;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

using ShopController = Il2CppLast.UI.KeyInput.ShopController;
using ShopInfoController = Il2CppLast.UI.KeyInput.ShopInfoController;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Tracks shop menu state for 'I' key description access and suppression.
    /// Uses state machine validation to only suppress during item list navigation,
    /// not during command menu (Buy/Sell/Exit) navigation.
    /// </summary>
    public static class ShopMenuTracker
    {
        private static bool _isShopMenuActive;
        public static bool IsShopMenuActive
        {
            get => _isShopMenuActive;
            set
            {
                _isShopMenuActive = value;
                Utils.MenuStateRegistry.SetActive(Utils.MenuStateRegistry.SHOP_MENU, value);
            }
        }
        public static string LastItemName { get; set; }
        public static string LastItemPrice { get; set; }

        /// <summary>
        /// Tracks when we're actively in the magic spell slot selection grid.
        /// Set by MagicTargetShow_Postfix, cleared by MagicTargetSetFocus_Postfix(false) or shop close.
        /// Used to suppress item list announcements during spell slot navigation.
        /// </summary>
        public static bool IsInMagicSlotSelection { get; set; }

        /// <summary>
        /// Tracks when we've entered Equipment submenu from shop.
        /// Used to restore ShopState when returning from equipment command bar.
        /// </summary>
        public static bool EnteredEquipmentSubmenu { get; set; }

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
        /// Uses cached controller reference to avoid expensive FindObjectOfType calls.
        /// </summary>
        public static bool ValidateState()
        {
            // Magic slot selection always suppresses
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

                    if (currentState == STATE_NONE)
                    {
                        ClearState();
                        return false;
                    }

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

        /// <summary>
        /// Reads the description text directly from the shop UI panel.
        /// Uses pointer access: ShopInfoController -> view (0x18) -> descriptionText (0x38) -> text
        /// </summary>
        public static string GetDescriptionFromUI()
        {
            try
            {
                var shopInfoController = UnityEngine.Object.FindObjectOfType<ShopInfoController>();
                if (shopInfoController == null)
                    return null;

                IntPtr controllerPtr = shopInfoController.Pointer;
                if (controllerPtr == IntPtr.Zero) return null;

                IntPtr viewPtr = IL2CppFieldReader.ReadPointer(controllerPtr, IL2CppOffsets.ShopInfo.InfoView);
                if (viewPtr == IntPtr.Zero) return null;

                IntPtr textPtr = IL2CppFieldReader.ReadPointer(viewPtr, IL2CppOffsets.ShopInfo.DescriptionText);
                if (textPtr == IntPtr.Zero) return null;

                var textComponent = new UnityEngine.UI.Text(textPtr);
                return textComponent?.text;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Clears IsShopMenuActive but preserves EnteredEquipmentSubmenu flag.
        /// Called when entering Equipment submenu so generic cursor can read command bar.
        /// </summary>
        public static void ClearForEquipmentSubmenu()
        {
            EnteredEquipmentSubmenu = true;
            IsShopMenuActive = false;
            IsInMagicSlotSelection = false;
        }

        public static void ClearState()
        {
            IsShopMenuActive = false;
            IsInMagicSlotSelection = false;
            EnteredEquipmentSubmenu = false;
            cachedShopController = null;
            LastItemName = null;
            LastItemPrice = null;
            ShopPatches.ResetQuantityTracking();
        }
    }

    /// <summary>
    /// Announces shop item details when 'I' key is pressed.
    /// Reads the description directly from the shop UI panel (ShopInfoView.descriptionText).
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

                string announcement = ShopMenuTracker.GetDescriptionFromUI();
                if (string.IsNullOrEmpty(announcement))
                    announcement = "No description available";

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing shop details: {ex.Message}");
            }
        }
    }
}
