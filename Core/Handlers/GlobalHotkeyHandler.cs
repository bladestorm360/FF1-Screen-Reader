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
    /// Helpers shared by the InputManager keyboard dispatch and ControllerRouter gamepad
    /// dispatch: vehicle announce, item-details cascade, config tooltip readout.
    /// </summary>
    internal static class GlobalHotkeyHandler
    {
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
                // 15-puzzle: announce current cursor row/column
                if (PuzzleGameState.IsActive)
                {
                    PuzzlePatches.AnnouncePosition();
                    return;
                }

                // Config menu tooltip
                var configController = TryGetActiveConfigController(out bool isKeyInput);
                if (configController != IntPtr.Zero)
                {
                    AnnounceConfigTooltip(configController, isKeyInput);
                    return;
                }

                // New-game class selection: read the focused class description on demand
                if (JobSelectionPatches.IsActive)
                {
                    string desc = JobSelectionPatches.GetCurrentDescription();
                    FFI_ScreenReaderMod.SpeakText(
                        string.IsNullOrWhiteSpace(desc) ? ModTextTranslator.T("No description") : desc.Trim(),
                        interrupt: true);
                    return;
                }

                // Shop item details
                if (ShopMenuTracker.ValidateState())
                {
                    ShopDetailsAnnouncer.AnnounceCurrentItemDetails();
                    return;
                }

                // Equipment menu details (stats/description from game panel)
                if (EquipMenuState.IsActive)
                {
                    EquipDetailsAnnouncer.AnnounceCurrentItemDetails();
                    return;
                }

                // Magic menu spell description
                if (MagicMenuState.IsSpellListActive)
                {
                    FFI_ScreenReader.Menus.MagicDetailsAnnouncer.AnnounceCurrentSpellDescription();
                    return;
                }

                // Item menu description (equip-requirements moved to U key)
                if (ItemMenuState.IsItemMenuActive)
                {
                    FFI_ScreenReader.Menus.ItemDescriptionAnnouncer.AnnounceCurrentDescription();
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
