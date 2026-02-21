using System;
using UnityEngine;
using MelonLoader;
using FFI_ScreenReader.Utils;
using FFI_ScreenReader.Patches;

using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;
using ConfigActualDetailsControllerBase_Touch = Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase;

namespace FFI_ScreenReader.Core.Handlers
{
    /// <summary>
    /// Handles global hotkeys: H, G, M, V, I, 9, =, -, ', ;, Tab, Shift+K.
    /// </summary>
    internal static class GlobalHotkeyHandler
    {
        internal static void HandleInput(FFI_ScreenReaderMod mod)
        {
            // Tab key - clear battle state as fallback
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                if (BattleStateHelper.IsInBattle)
                {
                    BattleStateHelper.ForceClearBattleState();
                }
            }

            // H - character health/status
            if (Input.GetKeyDown(KeyCode.H))
            {
                mod.AnnounceCharacterStatus();
            }

            // G - current gil
            if (Input.GetKeyDown(KeyCode.G))
            {
                mod.AnnounceGilAmount();
            }

            // M - current map (Shift+M for map exit filter)
            if (Input.GetKeyDown(KeyCode.M))
            {
                if (InputHelper.IsShiftHeld())
                {
                    mod.ToggleMapExitFilter();
                }
                else
                {
                    mod.AnnounceCurrentMap();
                }
            }

            // Shift+K - reset to All category
            if (Input.GetKeyDown(KeyCode.K) && InputHelper.IsShiftHeld())
            {
                mod.ResetToAllCategory();
            }

            // 9 - toggle audio beacons
            if (Input.GetKeyDown(KeyCode.Alpha9))
            {
                mod.ToggleAudioBeacons();
            }

            // = - next category
            if (Input.GetKeyDown(KeyCode.Equals))
            {
                mod.CycleNextCategory();
            }

            // - (Minus) - previous category
            if (Input.GetKeyDown(KeyCode.Minus))
            {
                mod.CyclePreviousCategory();
            }

            // I - item details (config/shop/item menu)
            if (Input.GetKeyDown(KeyCode.I))
            {
                HandleItemDetailsKey(mod);
            }

            // V - current vehicle/movement mode
            if (Input.GetKeyDown(KeyCode.V))
            {
                AnnounceCurrentVehicle();
            }

            // ' (Quote) - toggle footsteps
            if (Input.GetKeyDown(KeyCode.Quote))
            {
                mod.ToggleFootsteps();
            }

            // ; (Semicolon) - toggle wall tones
            if (Input.GetKeyDown(KeyCode.Semicolon))
            {
                mod.ToggleWallTones();
            }
        }

        internal static void AnnounceCurrentVehicle()
        {
            try
            {
                int moveState = MoveStateHelper.GetCurrentMoveState();
                string stateName = MoveStateHelper.GetMoveStateName(moveState);
                FFI_ScreenReaderMod.SpeakText(stateName);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing vehicle state: {ex.Message}");
            }
        }

        internal static void HandleItemDetailsKey(FFI_ScreenReaderMod mod)
        {
            try
            {
                // Config menu tooltip
                var configController = TryGetActiveConfigController(out bool isKeyInput);
                if (configController != IntPtr.Zero)
                {
                    AnnounceConfigTooltip(configController, isKeyInput);
                    return;
                }

                // Shop item details
                if (ShopMenuTracker.ValidateState())
                {
                    ShopDetailsAnnouncer.AnnounceCurrentItemDetails();
                    return;
                }

                // Item menu equip requirements
                if (ItemMenuState.IsItemMenuActive)
                {
                    FFI_ScreenReader.Menus.ItemDetailsAnnouncer.AnnounceEquipRequirements();
                    return;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error handling I key: {ex.Message}");
            }
        }

        private static IntPtr TryGetActiveConfigController(out bool isKeyInput)
        {
            isKeyInput = false;
            try
            {
                var keyInputController = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (keyInputController != null && keyInputController.gameObject.activeInHierarchy)
                {
                    isKeyInput = true;
                    return keyInputController.Pointer;
                }

                var touchController = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_Touch>();
                if (touchController != null && touchController.gameObject.activeInHierarchy)
                {
                    isKeyInput = false;
                    return touchController.Pointer;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error checking config menu state: {ex.Message}");
            }

            return IntPtr.Zero;
        }

        private static void AnnounceConfigTooltip(IntPtr controllerPtr, bool isKeyInput)
        {
            try
            {
                int offset = isKeyInput
                    ? IL2CppOffsets.ConfigMenu.DescriptionTextKeyInput
                    : IL2CppOffsets.ConfigMenu.DescriptionTextTouch;

                if (controllerPtr == IntPtr.Zero) return;

                IntPtr textPtr = IL2CppFieldReader.ReadPointer(controllerPtr, offset);
                if (textPtr != IntPtr.Zero)
                {
                    var descText = new UnityEngine.UI.Text(textPtr);
                    if (descText != null && !string.IsNullOrWhiteSpace(descText.text))
                    {
                        FFI_ScreenReaderMod.SpeakText(descText.text.Trim());
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading config tooltip: {ex.Message}");
            }
        }
    }
}
