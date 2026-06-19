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

        // Self-contained Language(enum int) → display name. The game's own GetLanguageMessage returns
        // EMPTY for the CURRENT language (it blanks the current item's label), so we map here instead.
        // Names match the game's English-name style observed in the language dropdown.
        private static readonly System.Collections.Generic.Dictionary<int, string> LanguageNames = new()
        {
            { 1, "Japanese" }, { 2, "English" }, { 3, "French" }, { 4, "Italian" },
            { 5, "German" }, { 6, "Spanish" }, { 7, "Korean" }, { 8, "Chinese (Traditional)" },
            { 9, "Chinese (Simplified)" }, { 10, "Russian" }, { 11, "Thai" }, { 12, "Brazilian Portuguese" },
        };

        /// <summary>
        /// Current game language display name, mapped from MessageManager.currentLanguage (the same
        /// source EntityTranslator uses). Used for the Language config row and the open dropdown's
        /// current item (whose LabelText is blank). Deliberately does NOT use
        /// LangugeUtility.GetLanguageMessage, which returns empty for the current language.
        /// </summary>
        public static string GetCurrentLanguageDisplayName()
        {
            try
            {
                var mgr = Il2CppLast.Management.MessageManager.Instance;
                if (mgr == null) return null;
                int langId = (int)mgr.currentLanguage;
                return LanguageNames.TryGetValue(langId, out string name) ? name : null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Config Menu] GetCurrentLanguageDisplayName failed: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Converts a slider value to percentage based on its min/max range.
        /// For BGM/SFX sliders (1-10 range), returns the raw integer value instead.
        /// </summary>
        public static string GetSliderPercentage(UnityEngine.UI.Slider slider, string settingName = null)
        {
            if (slider == null) return null;

            float min = slider.minValue;
            float max = slider.maxValue;
            float current = slider.value;

            // BGM/SFX sliders are 1-10, return raw value
            if (!string.IsNullOrEmpty(settingName) &&
                (settingName.IndexOf("BGM", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 settingName.IndexOf("SFX", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return ((int)Math.Round(current)).ToString();
            }

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

            // Language row: its dropdown OptionData text is empty while the popup is closed, so read
            // the current language straight from the game → "Language: <current>". Identified by the
            // command's config type (not a localized string match).
            var cmdData = command.ConfigCommandsData;
            if (cmdData != null && cmdData.ConfigCommandType == Il2CppLast.UI.ConfigCommandType.Language)
            {
                string lang = GetCurrentLanguageDisplayName();
                if (!string.IsNullOrEmpty(lang))
                    return lang;
            }

            // Check arrow change text (for toggle/selection options like BGM Type)
            if (view.ArrowSelectTypeRoot != null && view.ArrowSelectTypeRoot.activeSelf)
            {
                var arrowText = GetArrowChangeTextKeyInput(view);
                if (!string.IsNullOrEmpty(arrowText))
                {
                    return arrowText;
                }
            }

            // Check slider value (for volume sliders)
            if (view.SliderTypeRoot != null && view.SliderTypeRoot.activeSelf)
            {
                if (view.Slider != null)
                {
                    string settingName = view.NameText?.text;
                    string sliderValue = GetSliderPercentage(view.Slider, settingName);
                    if (!string.IsNullOrEmpty(sliderValue))
                    {
                        return sliderValue;
                    }
                }
            }

            // Check dropdown — ONLY while the popup is actually open. Reading closed dropdowns made
            // sub-screen-opening rows (Gamepad/Keyboard Settings) announce Unity's default placeholder
            // "Option A". The Language row's current value is handled above via ConfigCommandType.Language.
            if (view.DropDownTypeRoot != null && view.DropDownTypeRoot.activeSelf && view.DropDown != null)
            {
                var dropdown = view.DropDown;
                if (dropdown.options != null && dropdown.value >= 0 && dropdown.value < dropdown.options.Count)
                {
                    var opt = dropdown.options[dropdown.value];
                    string dropdownText = opt != null ? opt.text : null;
                    if (!string.IsNullOrEmpty(dropdownText))
                    {
                        return dropdownText;
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

            // Check slider value (Touch mode doesn't expose setting name, uses percentage)
            if (view.SliderTypeRoot != null && view.SliderTypeRoot.activeSelf)
            {
                // Try to find the slider in the slider root
                var slider = view.SliderTypeRoot.GetComponentInChildren<UnityEngine.UI.Slider>();
                if (slider != null)
                {
                    string sliderValue = GetSliderPercentage(slider);
                    if (!string.IsNullOrEmpty(sliderValue))
                    {
                        return sliderValue;
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
