using System;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Menus;
using UnityEngine;
using Key = Il2CppSystem.Input.Key;
using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Tracks config menu state for suppressing MenuTextDiscovery.
    /// </summary>
    public static class ConfigMenuState
    {
        /// <summary>
        /// True when config menu is actively handling cursor events.
        /// </summary>
        public static bool IsActive { get; set; } = false;

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// Validates controller is still active (auto-resets stuck flags).
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            try
            {
                // Validate controller is still active
                var controller = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (controller == null || !controller.gameObject.activeInHierarchy)
                {
                    ResetState();
                    return false;
                }
                return true;
            }
            catch
            {
                ResetState();
                return false;
            }
        }

        /// <summary>
        /// Resets config menu state.
        /// </summary>
        public static void ResetState()
        {
            IsActive = false;
            ConfigCommandController_SetFocus_Patch.ResetState();
        }
    }

    /// <summary>
    /// Controller-based patches for config menu navigation.
    /// Announces menu items directly from ConfigCommandController when navigating with up/down arrows.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.ConfigCommandController), nameof(Il2CppLast.UI.KeyInput.ConfigCommandController.SetFocus))]
    public static class ConfigCommandController_SetFocus_Patch
    {
        private static string lastAnnouncedText = "";
        private static string lastAnnouncedSettingName = "";

        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.UI.KeyInput.ConfigCommandController __instance, bool isFocus)
        {
            try
            {
                // Only announce when gaining focus (not losing it)
                if (!isFocus)
                {
                    return;
                }

                // Safety checks
                if (__instance == null)
                {
                    return;
                }

                // Don't announce if controller is not active (prevents announcements during scene loading)
                if (!__instance.gameObject.activeInHierarchy)
                {
                    return;
                }

                // Verify this controller is actually the selected one by checking the parent ConfigActualDetailsControllerBase
                var configDetailsController = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (configDetailsController != null)
                {
                    var selectedCommand = configDetailsController.SelectedCommand;
                    if (selectedCommand != null && selectedCommand != __instance)
                    {
                        // This is not the selected controller, skip
                        return;
                    }
                }

                // Get the view which contains the localized text
                var view = __instance.view;
                if (view == null)
                {
                    return;
                }

                // Get the name text (localized)
                var nameText = view.NameText;
                if (nameText == null || string.IsNullOrWhiteSpace(nameText.text))
                {
                    return;
                }

                string menuText = nameText.text.Trim();

                // Filter out template/placeholder values
                if (menuText == "NewText" || menuText == "Text" || menuText == "Name" || menuText == "Label")
                {
                    return;
                }

                // Also try to get the current value for this config option
                string configValue = ConfigMenuReader.FindConfigValueFromController(__instance);

                string announcement = menuText;
                if (!string.IsNullOrWhiteSpace(configValue))
                {
                    announcement = $"{menuText}: {configValue}";
                }

                // Skip duplicate announcements
                if (announcement == lastAnnouncedText)
                {
                    return;
                }

                // Check if this is the same setting but different value (value changed)
                if (menuText == lastAnnouncedSettingName && !string.IsNullOrWhiteSpace(configValue))
                {
                    // Same setting, value changed - announce just the value
                    lastAnnouncedText = announcement;

                    // Ensure state is still active
                    FFI_ScreenReaderMod.ClearOtherMenuStates("Config");
                    ConfigMenuState.IsActive = true;

                    MelonLogger.Msg($"[Config Menu] Value: {configValue}");
                    FFI_ScreenReaderMod.SpeakText(configValue, interrupt: true);
                    return;
                }

                // Different setting - announce full "Setting: Value"
                lastAnnouncedText = announcement;
                lastAnnouncedSettingName = menuText;

                // Set active state and clear other menu states
                FFI_ScreenReaderMod.ClearOtherMenuStates("Config");
                ConfigMenuState.IsActive = true;

                MelonLogger.Msg($"[Config Menu] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ConfigCommandController.SetFocus patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets the duplicate tracker (call when exiting config menu)
        /// </summary>
        public static void ResetState()
        {
            lastAnnouncedText = "";
            lastAnnouncedSettingName = "";
        }
    }

    /// <summary>
    /// Patch for SwitchArrowSelectTypeProcess - called when left/right arrows change toggle options.
    /// Only announces when the value actually changes.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase), "SwitchArrowSelectTypeProcess")]
    public static class ConfigActualDetails_SwitchArrowSelectType_Patch
    {
        private static string lastArrowValue = "";

        [HarmonyPostfix]
        public static void Postfix(
            Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase __instance,
            ConfigCommandController controller,
            Key key)
        {
            try
            {
                if (controller == null || controller.view == null) return;

                var view = controller.view;

                // Get arrow select value
                if (view.ArrowSelectTypeRoot != null && view.ArrowSelectTypeRoot.activeSelf)
                {
                    var arrowRoot = view.ArrowSelectTypeRoot;
                    var texts = arrowRoot.GetComponentsInChildren<UnityEngine.UI.Text>();
                    foreach (var text in texts)
                    {
                        // Skip inactive text components
                        if (text == null || text.gameObject == null || !text.gameObject.activeInHierarchy)
                            continue;

                        if (!string.IsNullOrWhiteSpace(text.text))
                        {
                            string textValue = text.text.Trim();
                            // Filter out arrow characters and template values
                            if (IsValidConfigValue(textValue))
                            {
                                // Only announce if value changed
                                if (textValue == lastArrowValue) return;

                                lastArrowValue = textValue;
                                MelonLogger.Msg($"[ConfigMenu] Arrow value changed: {textValue}");
                                FFI_ScreenReaderMod.SpeakText(textValue, interrupt: true);
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in SwitchArrowSelectTypeProcess patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a config value is valid (not a placeholder, template, or arrow character).
        /// </summary>
        private static bool IsValidConfigValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // Filter out arrow characters
            if (value == "<" || value == ">" || value == "◀" || value == "▶" ||
                value == "←" || value == "→")
                return false;

            // Filter out known template/placeholder values
            if (value == "NewText" || value == "ReEquip" || value == "Text" ||
                value == "Label" || value == "Value" || value == "Name")
                return false;

            return true;
        }
    }

    /// <summary>
    /// Patch for SwitchSliderTypeProcess - called when left/right arrows change slider values.
    /// Only announces when the value actually changes for the SAME option.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase), "SwitchSliderTypeProcess")]
    public static class ConfigActualDetails_SwitchSliderType_Patch
    {
        private static string lastSliderPercentage = "";
        private static ConfigCommandController lastController = null;

        [HarmonyPostfix]
        public static void Postfix(
            Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase __instance,
            ConfigCommandController controller,
            Key key)
        {
            try
            {
                if (controller == null || controller.view == null) return;

                var view = controller.view;
                if (view.Slider == null) return;

                // Calculate percentage using proper min/max range
                string percentage = ConfigMenuReader.GetSliderPercentage(view.Slider);
                if (string.IsNullOrEmpty(percentage)) return;

                // Only announce if value changed for the SAME controller
                // This prevents announcements when navigating between different sliders
                if (controller == lastController && percentage == lastSliderPercentage)
                {
                    return;
                }

                // If we moved to a different controller (different option), don't announce
                // Let SetFocus handle the full "Name: Value" announcement
                if (controller != lastController)
                {
                    lastController = controller;
                    lastSliderPercentage = percentage;
                    return;
                }

                // Same controller, value changed - announce just the new value
                lastSliderPercentage = percentage;

                MelonLogger.Msg($"[ConfigMenu] Slider value changed: {percentage}");
                FFI_ScreenReaderMod.SpeakText(percentage, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in SwitchSliderTypeProcess patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for Touch mode arrow button handling.
    /// Only announces when the value actually changes.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase), "SwitchArrowTypeProcess")]
    public static class ConfigActualDetailsTouch_SwitchArrowType_Patch
    {
        private static string lastTouchArrowValue = "";

        [HarmonyPostfix]
        public static void Postfix(
            Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase __instance,
            Il2CppLast.UI.Touch.ConfigCommandController controller,
            int value)
        {
            try
            {
                if (controller == null || controller.view == null) return;

                var view = controller.view;

                // Check arrow button type
                if (view.ArrowButtonTypeRoot != null && view.ArrowButtonTypeRoot.activeSelf)
                {
                    var texts = view.ArrowButtonTypeRoot.GetComponentsInChildren<UnityEngine.UI.Text>();
                    foreach (var text in texts)
                    {
                        // Skip inactive text components
                        if (text == null || text.gameObject == null || !text.gameObject.activeInHierarchy)
                            continue;

                        if (!string.IsNullOrWhiteSpace(text.text))
                        {
                            string textValue = text.text.Trim();
                            // Filter out arrow characters and template values
                            if (IsValidTouchConfigValue(textValue))
                            {
                                // Only announce if value changed
                                if (textValue == lastTouchArrowValue) return;

                                lastTouchArrowValue = textValue;
                                MelonLogger.Msg($"[ConfigMenu] Touch arrow value changed: {textValue}");
                                FFI_ScreenReaderMod.SpeakText(textValue, interrupt: true);
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in Touch SwitchArrowTypeProcess patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a touch config value is valid (not a placeholder, template, or arrow character).
        /// </summary>
        private static bool IsValidTouchConfigValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // Filter out arrow characters
            if (value == "<" || value == ">" || value == "◀" || value == "▶" ||
                value == "←" || value == "→")
                return false;

            // Filter out known template/placeholder values
            if (value == "NewText" || value == "ReEquip" || value == "Text" ||
                value == "Label" || value == "Value" || value == "Name")
                return false;

            return true;
        }
    }

    /// <summary>
    /// Patch for Touch mode slider handling.
    /// Only announces when the value actually changes for the SAME option.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase), "SwitchSliderTypeProcess")]
    public static class ConfigActualDetailsTouch_SwitchSliderType_Patch
    {
        private static string lastTouchSliderPercentage = "";
        private static Il2CppLast.UI.Touch.ConfigCommandController lastTouchController = null;

        [HarmonyPostfix]
        public static void Postfix(
            Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase __instance,
            Il2CppLast.UI.Touch.ConfigCommandController controller,
            float value)
        {
            try
            {
                if (controller == null || controller.view == null) return;

                var view = controller.view;
                if (view.SliderTypeRoot == null) return;

                // Find the slider in the slider root
                var slider = view.SliderTypeRoot.GetComponentInChildren<UnityEngine.UI.Slider>();
                if (slider == null) return;

                // Calculate percentage using proper min/max range
                string percentage = ConfigMenuReader.GetSliderPercentage(slider);
                if (string.IsNullOrEmpty(percentage)) return;

                // Only announce if value changed for the SAME controller
                // This prevents announcements when navigating between different sliders
                if (controller == lastTouchController && percentage == lastTouchSliderPercentage)
                {
                    return;
                }

                // If we moved to a different controller (different option), don't announce
                // Let SetFocus handle the full "Name: Value" announcement
                if (controller != lastTouchController)
                {
                    lastTouchController = controller;
                    lastTouchSliderPercentage = percentage;
                    return;
                }

                // Same controller, value changed - announce just the new value
                lastTouchSliderPercentage = percentage;

                MelonLogger.Msg($"[ConfigMenu] Touch slider value changed: {percentage}");
                FFI_ScreenReaderMod.SpeakText(percentage, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in Touch SwitchSliderTypeProcess patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for ConfigController.SetActive to clear state when config menu closes.
    /// This ensures the active state flag is properly reset when backing out to main menu.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.ConfigController), "SetActive", new Type[] { typeof(bool) })]
    public static class ConfigController_SetActive_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isActive)
        {
            if (!isActive)
            {
                MelonLogger.Msg("[Config Menu] SetActive(false) - clearing state");
                ConfigMenuState.ResetState();
            }
        }
    }

    /// <summary>
    /// Legacy static class for manual patch application.
    /// Now only used for registering with the main mod's harmony instance.
    /// </summary>
    public static class ConfigMenuPatches
    {
        /// <summary>
        /// Applies config menu patches using manual Harmony patching.
        /// Note: Most patches now use HarmonyPatch attributes and are auto-applied by MelonLoader.
        /// This method is kept for any future manual patching needs.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            MelonLogger.Msg("Config menu patches registered (using HarmonyPatch attributes)");
        }
    }
}
