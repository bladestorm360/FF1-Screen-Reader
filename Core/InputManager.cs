using System;
using UnityEngine;
using UnityEngine.EventSystems;
using MelonLoader;
using HarmonyLib;
using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;
using ConfigActualDetailsControllerBase_Touch = Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase;
using FFI_ScreenReader.Menus;

namespace FFI_ScreenReader.Core
{
    /// <summary>
    /// Manages all keyboard input handling for the screen reader mod.
    /// Detects hotkeys and routes them to appropriate mod functions.
    /// </summary>
    public class InputManager
    {
        private readonly FFI_ScreenReaderMod mod;

        public InputManager(FFI_ScreenReaderMod mod)
        {
            this.mod = mod;
        }

        /// <summary>
        /// Called every frame to check for input and route hotkeys.
        /// </summary>
        public void Update()
        {
            // Early exit if no keys pressed this frame - avoids expensive operations
            if (!Input.anyKeyDown)
            {
                return;
            }

            // Check if ANY Unity InputField is focused - if so, let all keys pass through
            if (IsInputFieldFocused())
            {
                // Player is typing text - skip all hotkey processing
                return;
            }

            // Handle field input (entity navigation)
            HandleFieldInput();

            // Global hotkeys (work everywhere)
            HandleGlobalInput();
        }

        /// <summary>
        /// Checks if a Unity InputField is currently focused (player is typing).
        /// Uses EventSystem for efficient O(1) lookup instead of FindObjectOfType scene search.
        /// </summary>
        private bool IsInputFieldFocused()
        {
            try
            {
                // Check if EventSystem exists and has a selected object
                if (EventSystem.current == null)
                    return false;

                var currentObj = EventSystem.current.currentSelectedGameObject;

                // 1. Check if anything is selected
                if (currentObj == null)
                    return false;

                // 2. Check if the selected object is a standard InputField
                return currentObj.TryGetComponent(out UnityEngine.UI.InputField inputField);
            }
            catch (System.Exception ex)
            {
                // If we can't check input field state, continue with normal hotkey processing
                MelonLoader.MelonLogger.Warning($"Error checking input field state: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Handles input when on the field (entity navigation).
        /// </summary>
        private void HandleFieldInput()
        {
            // Hotkey: Ctrl+Arrow to teleport to direction of selected entity
            if (IsCtrlHeld())
            {
                if (Input.GetKeyDown(KeyCode.UpArrow))
                {
                    mod.TeleportInDirection(new Vector2(0, 16)); // North
                    return;
                }
                else if (Input.GetKeyDown(KeyCode.DownArrow))
                {
                    mod.TeleportInDirection(new Vector2(0, -16)); // South
                    return;
                }
                else if (Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    mod.TeleportInDirection(new Vector2(-16, 0)); // West
                    return;
                }
                else if (Input.GetKeyDown(KeyCode.RightArrow))
                {
                    mod.TeleportInDirection(new Vector2(16, 0)); // East
                    return;
                }
            }

            // Hotkey: J or [ to cycle backwards
            if (Input.GetKeyDown(KeyCode.J) || Input.GetKeyDown(KeyCode.LeftBracket))
            {
                // Check for Shift+J/[ (cycle categories backward)
                if (IsShiftHeld())
                {
                    mod.CyclePreviousCategory();
                }
                else
                {
                    // Just J/[ (cycle entities backward)
                    mod.CyclePrevious();
                }
            }

            // Hotkey: K to repeat current entity
            if (Input.GetKeyDown(KeyCode.K))
            {
                mod.AnnounceEntityOnly();
            }

            // Hotkey: L or ] to cycle forwards
            if (Input.GetKeyDown(KeyCode.L) || Input.GetKeyDown(KeyCode.RightBracket))
            {
                // Check for Shift+L/] (cycle categories forward)
                if (IsShiftHeld())
                {
                    mod.CycleNextCategory();
                }
                else
                {
                    // Just L/] (cycle entities forward)
                    mod.CycleNext();
                }
            }

            // Hotkey: P or \ to pathfind to current entity
            if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Backslash))
            {
                // Check for Shift+P/\ (toggle pathfinding filter)
                if (IsShiftHeld())
                {
                    mod.TogglePathfindingFilter();
                }
                else
                {
                    // Just P/\ (pathfind to current entity)
                    mod.AnnounceCurrentEntity();
                }
            }
        }

        /// <summary>
        /// Handles global input (works everywhere).
        /// </summary>
        private void HandleGlobalInput()
        {
            // Check for status details navigation (takes priority when active)
            if (HandleStatusDetailsInput())
            {
                return; // Status navigation consumed the input
            }

            // Hotkey: H to announce character health/status
            if (Input.GetKeyDown(KeyCode.H))
            {
                mod.AnnounceCharacterStatus();
            }

            // Hotkey: G to announce current gil amount
            if (Input.GetKeyDown(KeyCode.G))
            {
                mod.AnnounceGilAmount();
            }

            // Hotkey: M to announce current map name
            if (Input.GetKeyDown(KeyCode.M))
            {
                // Check for Shift+M (toggle map exit filter)
                if (IsShiftHeld())
                {
                    mod.ToggleMapExitFilter();
                }
                else
                {
                    // Just M (announce current map)
                    mod.AnnounceCurrentMap();
                }
            }

            // Hotkey: 0 (Alpha0) or Shift+K to reset to All category
            if (Input.GetKeyDown(KeyCode.Alpha0))
            {
                mod.ResetToAllCategory();
            }

            if (Input.GetKeyDown(KeyCode.K) && IsShiftHeld())
            {
                mod.ResetToAllCategory();
            }

            // Hotkey: = (Equals) to cycle to next category
            if (Input.GetKeyDown(KeyCode.Equals))
            {
                mod.CycleNextCategory();
            }

            // Hotkey: - (Minus) to cycle to previous category
            if (Input.GetKeyDown(KeyCode.Minus))
            {
                mod.CyclePreviousCategory();
            }

            // Hotkey: I to announce item details (in shops)
            if (Input.GetKeyDown(KeyCode.I))
            {
                HandleItemDetailsKey();
            }

            // Hotkey: V to announce current vehicle/movement mode
            if (Input.GetKeyDown(KeyCode.V))
            {
                AnnounceCurrentVehicle();
            }
        }

        /// <summary>
        /// Announces the current vehicle/movement mode.
        /// </summary>
        private void AnnounceCurrentVehicle()
        {
            try
            {
                int moveState = FFI_ScreenReader.Utils.MoveStateHelper.GetCurrentMoveState();
                string stateName = FFI_ScreenReader.Utils.MoveStateHelper.GetMoveStateName(moveState);
                MelonLogger.Msg($"[Vehicle] Current movement mode: {stateName}");
                FFI_ScreenReaderMod.SpeakText(stateName);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error announcing vehicle state: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles the I key for item details in shops and other menus.
        /// </summary>
        private void HandleItemDetailsKey()
        {
            try
            {
                // Config menu: Announce option description/tooltip
                if (IsConfigMenuActive())
                {
                    AnnounceConfigTooltip();
                    return;
                }

                // Shop menu: Announce item stats and description
                if (FFI_ScreenReader.Patches.ShopMenuTracker.ValidateState())
                {
                    FFI_ScreenReader.Patches.ShopDetailsAnnouncer.AnnounceCurrentItemDetails();
                    return;
                }

                // Item menu: Announce equipment requirements (which jobs can equip)
                if (FFI_ScreenReader.Patches.ItemMenuState.IsItemMenuActive)
                {
                    FFI_ScreenReader.Menus.ItemDetailsAnnouncer.AnnounceEquipRequirements();
                    return;
                }
            }
            catch (System.Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"Error handling I key: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a config menu is currently active.
        /// </summary>
        private bool IsConfigMenuActive()
        {
            try
            {
                // Check for KeyInput config controller
                var keyInputController = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (keyInputController != null && keyInputController.gameObject.activeInHierarchy)
                {
                    return true;
                }

                // Check for Touch config controller
                var touchController = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_Touch>();
                if (touchController != null && touchController.gameObject.activeInHierarchy)
                {
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error checking config menu state: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Announces the description/tooltip text for the currently highlighted config option.
        /// Only works when in the config menu.
        /// </summary>
        private void AnnounceConfigTooltip()
        {
            try
            {
                // Try KeyInput controller (in-game config menu - keyboard/gamepad mode)
                var keyInputController = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (keyInputController != null && keyInputController.gameObject.activeInHierarchy)
                {
                    string description = GetDescriptionTextKeyInput(keyInputController);
                    if (!string.IsNullOrEmpty(description))
                    {
                        MelonLogger.Msg($"[Config Tooltip] {description}");
                        FFI_ScreenReaderMod.SpeakText(description);
                        return;
                    }
                }

                // Try Touch controller
                var touchController = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_Touch>();
                if (touchController != null && touchController.gameObject.activeInHierarchy)
                {
                    string description = GetDescriptionTextTouch(touchController);
                    if (!string.IsNullOrEmpty(description))
                    {
                        MelonLogger.Msg($"[Config Tooltip] {description}");
                        FFI_ScreenReaderMod.SpeakText(description);
                        return;
                    }
                }

                // Not in a config menu or no description available
                MelonLogger.Msg("[Config Tooltip] No config menu active or no description available");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error reading config tooltip: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the description text from a KeyInput ConfigActualDetailsControllerBase.
        /// descriptionText field is at offset 0xA0 in FF1.
        /// </summary>
        // IL2CPP field offset for descriptionText (from dump.cs)
        private const int OFFSET_DESCRIPTION_TEXT_KEYINPUT = 0xA0;

        private string GetDescriptionTextKeyInput(ConfigActualDetailsControllerBase_KeyInput controller)
        {
            if (controller == null) return null;

            try
            {
                // Use IL2CPP pointer access - reflection doesn't work for IL2CPP private fields
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr != IntPtr.Zero)
                {
                    unsafe
                    {
                        IntPtr textPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_DESCRIPTION_TEXT_KEYINPUT);
                        if (textPtr != IntPtr.Zero)
                        {
                            var descText = new UnityEngine.UI.Text(textPtr);
                            if (descText != null && !string.IsNullOrWhiteSpace(descText.text))
                            {
                                return descText.text.Trim();
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error accessing KeyInput description text: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets the description text from a Touch ConfigActualDetailsControllerBase.
        /// descriptionText field is at offset 0x50 in FF1.
        /// </summary>
        // IL2CPP field offset for descriptionText in Touch controller (from dump.cs)
        private const int OFFSET_DESCRIPTION_TEXT_TOUCH = 0x50;

        private string GetDescriptionTextTouch(ConfigActualDetailsControllerBase_Touch controller)
        {
            if (controller == null) return null;

            try
            {
                // Use IL2CPP pointer access - reflection doesn't work for IL2CPP private fields
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr != IntPtr.Zero)
                {
                    unsafe
                    {
                        IntPtr textPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_DESCRIPTION_TEXT_TOUCH);
                        if (textPtr != IntPtr.Zero)
                        {
                            var descText = new UnityEngine.UI.Text(textPtr);
                            if (descText != null && !string.IsNullOrWhiteSpace(descText.text))
                            {
                                return descText.text.Trim();
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error accessing Touch description text: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Handles input for status details screen navigation.
        /// Returns true if input was consumed (status navigation is active and arrow was pressed).
        /// </summary>
        private bool HandleStatusDetailsInput()
        {
            var tracker = StatusNavigationTracker.Instance;

            // Check if status navigation is active
            if (!tracker.IsNavigationActive || !tracker.ValidateState())
            {
                return false;
            }

            // Handle arrow key navigation through stats
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                if (IsCtrlHeld())
                {
                    // Ctrl+Up: Jump to first stat
                    StatusNavigationReader.JumpToTop();
                }
                else if (IsShiftHeld())
                {
                    // Shift+Up: Jump to previous stat group
                    StatusNavigationReader.JumpToPreviousGroup();
                }
                else
                {
                    // Up: Navigate to previous stat
                    StatusNavigationReader.NavigatePrevious();
                }
                return true;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (IsCtrlHeld())
                {
                    // Ctrl+Down: Jump to last stat
                    StatusNavigationReader.JumpToBottom();
                }
                else if (IsShiftHeld())
                {
                    // Shift+Down: Jump to next stat group
                    StatusNavigationReader.JumpToNextGroup();
                }
                else
                {
                    // Down: Navigate to next stat
                    StatusNavigationReader.NavigateNext();
                }
                return true;
            }

            // R: Repeat current stat
            if (Input.GetKeyDown(KeyCode.R))
            {
                StatusNavigationReader.ReadCurrentStat();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if either Shift key is held.
        /// </summary>
        private bool IsShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        /// <summary>
        /// Checks if either Ctrl key is held.
        /// </summary>
        private bool IsCtrlHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }
    }
}
