using Il2CppLast.UI.KeyInput;
using Il2CppLast.UI.Touch;
using UnityEngine;
using MelonLoader;
using System;
using FFI_ScreenReader.Core;
using ConfigCommandView_Touch = Il2CppLast.UI.Touch.ConfigCommandView;
using ConfigCommandView_KeyInput = Il2CppLast.UI.KeyInput.ConfigCommandView;
using ConfigCommandController_KeyInput = Il2CppLast.UI.KeyInput.ConfigCommandController;
using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;
using ConfigActualDetailsControllerBase_Touch = Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase;

namespace FFI_ScreenReader.Menus
{
    /// <summary>
    /// Helper class for reading config menu values (sliders, toggles, dropdowns).
    /// </summary>
    public static class ConfigMenuReader
    {
        /// <summary>
        /// Find config value directly from a ConfigCommandController instance.
        /// This is used by the controller-based patch system.
        /// </summary>
        public static string FindConfigValueFromController(ConfigCommandController_KeyInput controller)
        {
            try
            {
                if (controller == null)
                {
                    return null;
                }

                return GetValueFromKeyInputCommand(controller);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading config value from controller: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Converts a slider value to percentage based on its min/max range.
        /// </summary>
        public static string GetSliderPercentage(UnityEngine.UI.Slider slider)
        {
            if (slider == null) return null;

            float min = slider.minValue;
            float max = slider.maxValue;
            float current = slider.value;

            // Calculate percentage based on range
            float range = max - min;
            if (range <= 0) return "0%";

            float percentage = ((current - min) / range) * 100f;
            int roundedPercentage = (int)Math.Round(percentage);

            return $"{roundedPercentage}%";
        }

        /// <summary>
        /// Gets the value from a KeyInput ConfigCommandController.
        /// </summary>
        private static string GetValueFromKeyInputCommand(ConfigCommandController_KeyInput command)
        {
            if (command == null || command.view == null)
                return null;

            var view = command.view;

            // Check arrow change text (for toggle/selection options like BGM Type)
            if (view.ArrowSelectTypeRoot != null && view.ArrowSelectTypeRoot.activeSelf)
            {
                var arrowText = GetArrowChangeTextKeyInput(view);
                if (!string.IsNullOrEmpty(arrowText))
                {
                    return arrowText;
                }
            }

            // Check slider value (for volume sliders) - always use percentage
            if (view.SliderTypeRoot != null && view.SliderTypeRoot.activeSelf)
            {
                if (view.Slider != null)
                {
                    string percentage = GetSliderPercentage(view.Slider);
                    if (!string.IsNullOrEmpty(percentage))
                    {
                        return percentage;
                    }
                }
            }

            // Check dropdown (for language selection, etc.)
            if (view.DropDownTypeRoot != null && view.DropDownTypeRoot.activeSelf)
            {
                if (view.DropDown != null)
                {
                    var dropdown = view.DropDown;
                    if (dropdown.options != null && dropdown.value >= 0 && dropdown.value < dropdown.options.Count)
                    {
                        string dropdownText = dropdown.options[dropdown.value].text;
                        if (!string.IsNullOrEmpty(dropdownText))
                        {
                            return dropdownText;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the arrow change text from a KeyInput ConfigCommandView.
        /// </summary>
        private static string GetArrowChangeTextKeyInput(ConfigCommandView_KeyInput view)
        {
            try
            {
                // Try to access the arrowChangeText field via the view's child transforms
                var arrowRoot = view.ArrowSelectTypeRoot;
                if (arrowRoot != null)
                {
                    var texts = arrowRoot.GetComponentsInChildren<UnityEngine.UI.Text>();
                    foreach (var text in texts)
                    {
                        // Skip inactive text components
                        if (text == null || text.gameObject == null || !text.gameObject.activeInHierarchy)
                            continue;

                        if (!string.IsNullOrWhiteSpace(text.text))
                        {
                            string value = text.text.Trim();
                            // Filter out arrow characters, empty text, and template/placeholder values
                            if (IsValidConfigValue(value))
                            {
                                return value;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting arrow change text: {ex.Message}");
            }
            return null;
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

        /// <summary>
        /// Gets the value from a Touch ConfigCommandController.
        /// </summary>
        public static string GetValueFromTouchCommand(Il2CppLast.UI.Touch.ConfigCommandController command)
        {
            if (command == null || command.view == null)
                return null;

            var view = command.view;

            // Check arrow button type (for toggle/selection options)
            if (view.ArrowButtonTypeRoot != null && view.ArrowButtonTypeRoot.activeSelf)
            {
                var arrowText = GetArrowChangeTextTouch(view);
                if (!string.IsNullOrEmpty(arrowText))
                {
                    return arrowText;
                }
            }

            // Check slider value - use percentage from slider component
            if (view.SliderTypeRoot != null && view.SliderTypeRoot.activeSelf)
            {
                // Try to find the slider in the slider root
                var slider = view.SliderTypeRoot.GetComponentInChildren<UnityEngine.UI.Slider>();
                if (slider != null)
                {
                    string percentage = GetSliderPercentage(slider);
                    if (!string.IsNullOrEmpty(percentage))
                    {
                        return percentage;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the arrow change text from a Touch ConfigCommandView.
        /// </summary>
        private static string GetArrowChangeTextTouch(ConfigCommandView_Touch view)
        {
            try
            {
                var arrowRoot = view.ArrowButtonTypeRoot;
                if (arrowRoot != null)
                {
                    var texts = arrowRoot.GetComponentsInChildren<UnityEngine.UI.Text>();
                    foreach (var text in texts)
                    {
                        // Skip inactive text components
                        if (text == null || text.gameObject == null || !text.gameObject.activeInHierarchy)
                            continue;

                        if (!string.IsNullOrWhiteSpace(text.text))
                        {
                            string value = text.text.Trim();
                            // Filter out arrow characters, empty text, and template/placeholder values
                            if (IsValidConfigValue(value))
                            {
                                return value;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting touch arrow change text: {ex.Message}");
            }
            return null;
        }
    }
}
