using System;
using UnityEngine;
using UnityEngine.EventSystems;
using MelonLoader;
using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;
using ConfigActualDetailsControllerBase_Touch = Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase;
using FFI_ScreenReader.Menus;
using FFI_ScreenReader.Utils;
using FFI_ScreenReader.Field;
using FFI_ScreenReader.Patches;

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
            // Handle mod menu input first (consumes all input when open)
            if (ModMenu.HandleInput())
                return;

            // F8 to open mod menu (only when not in battle)
            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (!BattleStateHelper.IsInBattle)
                {
                    ModMenu.Open();
                }
                else
                {
                    FFI_ScreenReaderMod.SpeakText("Unavailable in battle", interrupt: true);
                }
                return;
            }

            // F5 to toggle enemy HP display (only when not in battle)
            if (Input.GetKeyDown(KeyCode.F5))
            {
                if (!BattleStateHelper.IsInBattle)
                {
                    // Cycle HP display: 0→1→2→0 (Numbers→Percentage→Hidden→Numbers)
                    int current = FFI_ScreenReaderMod.EnemyHPDisplay;
                    int next = (current + 1) % 3;
                    FFI_ScreenReaderMod.SetEnemyHPDisplay(next);

                    string[] options = { "Numbers", "Percentage", "Hidden" };
                    FFI_ScreenReaderMod.SpeakText($"Enemy HP: {options[next]}", interrupt: true);
                }
                else
                {
                    FFI_ScreenReaderMod.SpeakText("Unavailable in battle", interrupt: true);
                }
                return;
            }

            // F1 toggles walk/run speed - announce after game processes it
            if (Input.GetKeyDown(KeyCode.F1))
            {
                CoroutineManager.StartUntracked(AnnounceWalkRunState());
                return;
            }

            // F3 toggles encounters - announce after game processes it
            if (Input.GetKeyDown(KeyCode.F3))
            {
                CoroutineManager.StartUntracked(AnnounceEncounterState());
                return;
            }

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
        /// Coroutine that announces walk/run state after game processes F1 key.
        /// </summary>
        private static System.Collections.IEnumerator AnnounceWalkRunState()
        {
            // Wait 3 frames for game to fully process F1 and update dashFlag
            yield return null; // Frame 1
            yield return null; // Frame 2
            yield return null; // Frame 3

            try
            {
                // Read actual dash state from FieldKeyController
                bool isDashing = FFI_ScreenReader.Utils.MoveStateHelper.GetDashFlag();
                string state = isDashing ? "Run" : "Walk";
                FFI_ScreenReaderMod.SpeakText(state, interrupt: true);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[F1] Error reading walk/run state: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine that announces encounter state after game processes F3 key.
        /// </summary>
        private static System.Collections.IEnumerator AnnounceEncounterState()
        {
            yield return null; // Wait one frame for game to process
            try
            {
                var userData = Il2CppLast.Management.UserDataManager.Instance();
                if (userData?.CheatSettingsData != null)
                {
                    bool enabled = userData.CheatSettingsData.IsEnableEncount;
                    string state = enabled ? "Encounters on" : "Encounters off";
                    FFI_ScreenReaderMod.SpeakText(state, interrupt: true);
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"[F3] Error reading encounter state: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles global input (works everywhere).
        /// </summary>
        private void HandleGlobalInput()
        {
            // Tab key opens main menu - clear battle state as fallback
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                if (BattleStateHelper.IsInBattle)
                {
                    MelonLogger.Msg("[InputManager] Tab pressed - clearing stale battle state");
                    BattleStateHelper.ForceClearBattleState();
                }
            }

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

            // Hotkey: Shift+K to reset to All category
            if (Input.GetKeyDown(KeyCode.K) && IsShiftHeld())
            {
                mod.ResetToAllCategory();
            }

            // Hotkey: 9 (Alpha9) to toggle audio beacons
            if (Input.GetKeyDown(KeyCode.Alpha9))
            {
                mod.ToggleAudioBeacons();
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

            // Hotkey: ' (Quote) to toggle footsteps
            if (Input.GetKeyDown(KeyCode.Quote))
            {
                mod.ToggleFootsteps();
            }

            // Hotkey: ; (Semicolon) to toggle wall tones
            if (Input.GetKeyDown(KeyCode.Semicolon))
            {
                mod.ToggleWallTones();
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
                // TryGetActiveConfigController does a single lookup pass and returns the controller if found
                var configController = TryGetActiveConfigController(out bool isKeyInput);
                if (configController != IntPtr.Zero)
                {
                    AnnounceConfigTooltip(configController, isKeyInput);
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
        /// Tries to find an active config menu controller.
        /// Returns the controller's IL2CPP pointer if found, IntPtr.Zero otherwise.
        /// Avoids redundant FindObjectOfType calls by doing a single pass.
        /// </summary>
        /// <param name="isKeyInput">True if KeyInput controller, false if Touch controller</param>
        private IntPtr TryGetActiveConfigController(out bool isKeyInput)
        {
            isKeyInput = false;
            try
            {
                // Check for KeyInput config controller first (more common for keyboard users)
                var keyInputController = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (keyInputController != null && keyInputController.gameObject.activeInHierarchy)
                {
                    isKeyInput = true;
                    return keyInputController.Pointer;
                }

                // Check for Touch config controller
                var touchController = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_Touch>();
                if (touchController != null && touchController.gameObject.activeInHierarchy)
                {
                    isKeyInput = false;
                    return touchController.Pointer;
                }
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error checking config menu state: {ex.Message}");
            }

            return IntPtr.Zero;
        }

        /// <summary>
        /// Announces the description/tooltip text for the currently highlighted config option.
        /// Takes the controller pointer directly to avoid redundant FindObjectOfType calls.
        /// </summary>
        /// <param name="controllerPtr">The IL2CPP pointer to the config controller</param>
        /// <param name="isKeyInput">True if this is a KeyInput controller, false for Touch</param>
        private void AnnounceConfigTooltip(IntPtr controllerPtr, bool isKeyInput)
        {
            try
            {
                int offset = isKeyInput
                    ? IL2CppOffsets.ConfigMenu.DescriptionTextKeyInput
                    : IL2CppOffsets.ConfigMenu.DescriptionTextTouch;

                string description = GetDescriptionText(controllerPtr, offset);
                if (!string.IsNullOrEmpty(description))
                {
                    MelonLogger.Msg($"[Config Tooltip] {description}");
                    FFI_ScreenReaderMod.SpeakText(description);
                    return;
                }

                MelonLogger.Msg("[Config Tooltip] No description available");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error($"Error reading config tooltip: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the description text from a config controller using IL2CPP pointer access.
        /// </summary>
        /// <param name="controllerPtr">Pointer to the IL2CPP controller object</param>
        /// <param name="offset">Offset of the descriptionText field</param>
        /// <returns>The description text, or null if not available</returns>
        private string GetDescriptionText(IntPtr controllerPtr, int offset)
        {
            if (controllerPtr == IntPtr.Zero) return null;

            try
            {
                unsafe
                {
                    IntPtr textPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + offset);
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
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error accessing description text: {ex.Message}");
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
