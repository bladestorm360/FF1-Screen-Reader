using System;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Menus;
using FFI_ScreenReader.Utils;
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
        public static bool IsActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.CONFIG_MENU);
            set => MenuStateRegistry.SetActive(MenuStateRegistry.CONFIG_MENU, value);
        }


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
        }
    }

    /// <summary>
    /// Hooks ConfigActualDetailsControllerBase.SelectCommand — the private method the
    /// game invokes when the user navigates up/down within the config menu. Unlike
    /// ConfigCommandController.SetFocus (which the game re-asserts every frame on the
    /// focused controller), SelectCommand fires exactly once per cursor movement. That
    /// makes this patch event-driven with no dedup required.
    /// </summary>
    public static class ConfigCommandController_SetFocus_Patch
    {
        internal static void ResetTracking()
        {
            // Kept for compatibility with ConfigController_SetActive_Patch — the
            // event-driven hook has no state to clear, but external callers reference this.
        }
    }

    [HarmonyPatch]
    public static class ConfigActualDetails_SelectCommand_Patch
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase),
                "SelectCommand");
        }

        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase __instance)
        {
            try
            {
                // Gate: only announce when the real config menu is open. The
                // title-screen language selector and load-game flow share this base
                // class and would otherwise leak announcements like "Language" or
                // "Cursor memory: off" outside the config menu. IsActive is driven
                // by ConfigController.SetActive — set in the postfix below.
                if (!ConfigMenuState.IsActive)
                    return;

                if (__instance == null)
                    return;

                var selected = __instance.SelectedCommand;
                if (selected == null)
                    return;

                // Skip if controller is not active (prevents announcements during scene loading)
                if (!selected.gameObject.activeInHierarchy)
                    return;

                var view = selected.view;
                if (view == null)
                    return;

                var nameText = view.NameText;
                if (nameText == null || string.IsNullOrWhiteSpace(nameText.text))
                    return;

                string menuText = nameText.text.Trim();

                // Filter out template/placeholder values
                if (menuText == "NewText" || menuText == "Text" || menuText == "Name" || menuText == "Label")
                    return;

                string configValue = ConfigMenuReader.FindConfigValueFromController(selected);

                string announcement = string.IsNullOrWhiteSpace(configValue)
                    ? menuText
                    : $"{menuText}: {configValue}";

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ConfigActualDetails.SelectCommand patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for SwitchArrowSelectTypeProcess - called when left/right arrows change toggle options.
    /// Only announces when the value actually changes.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase), "SwitchArrowSelectTypeProcess")]
    public static class ConfigActualDetails_SwitchArrowSelectType_Patch
    {
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
    /// Hooks ConfigCommandController.SetSliderValue. The game calls this every frame on
    /// the focused slider to keep the visual in sync — value-change detection turns that
    /// per-frame stream into one announcement per actual user-driven value change. The
    /// detection compares the formatted percentage string (what we'd say) so float drift
    /// doesn't cause spurious announcements. State resets on menu close.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.ConfigCommandController), "SetSliderValue")]
    public static class ConfigCommandController_SetSliderValue_Patch
    {
        // Tracks the focused slider and its last-announced percentage. Single-slot is
        // sufficient because only one slider can be focused at a time.
        private static IntPtr lastSliderPtr = IntPtr.Zero;
        private static string lastPercentage = null;

        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.UI.KeyInput.ConfigCommandController __instance, float value)
        {
            try
            {
                if (__instance == null) return;

                // Only fire while user is actively in the config menu.
                if (!ConfigMenuState.IsActive) return;

                // Skip per-slider initialization fires for non-focused sliders.
                var details = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (details == null) return;
                var selected = details.SelectedCommand;
                IntPtr instancePtr = __instance.Pointer;
                if (selected == null || selected.Pointer != instancePtr) return;

                var view = __instance.view;
                if (view == null || view.Slider == null) return;

                string settingName = view.NameText?.text;
                string percentage = ConfigMenuReader.GetSliderPercentage(view.Slider, settingName);
                if (string.IsNullOrEmpty(percentage)) return;

                // Newly focused slider: SelectCommand already announced "Name: Value".
                // Record the value and skip — only adjustments after this should announce.
                if (instancePtr != lastSliderPtr)
                {
                    lastSliderPtr = instancePtr;
                    lastPercentage = percentage;
                    return;
                }

                // No change: this is a per-frame redundant SetSliderValue call from the
                // UI sync loop. Not an event.
                if (percentage == lastPercentage) return;

                // Value actually changed — user adjusted the slider.
                lastPercentage = percentage;
                FFI_ScreenReaderMod.SpeakText(percentage, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetSliderValue patch: {ex.Message}");
            }
        }

        internal static void ResetTracking()
        {
            lastSliderPtr = IntPtr.Zero;
            lastPercentage = null;
        }
    }

    /// <summary>
    /// Patch for Touch mode arrow button handling.
    /// Only announces when the value actually changes.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase), "SwitchArrowTypeProcess")]
    public static class ConfigActualDetailsTouch_SwitchArrowType_Patch
    {
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

                // Calculate value (Touch mode doesn't expose setting name, uses percentage)
                string percentage = ConfigMenuReader.GetSliderPercentage(slider);
                if (string.IsNullOrEmpty(percentage)) return;

                // If we moved to a different controller (different option), don't announce
                // Let SetFocus handle the full "Name: Value" announcement
                if (controller != lastTouchController)
                {
                    lastTouchController = controller;
                    return;
                }

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
            if (isActive)
            {
                // Drive IsActive from the config menu's lifecycle so the SelectCommand
                // postfix only announces inside the real config menu, not for sibling
                // controllers (title-screen language selector, save-load options) that
                // share ConfigActualDetailsControllerBase.
                FFI_ScreenReaderMod.ClearOtherMenuStates("Config");
                ConfigMenuState.IsActive = true;
            }
            else
            {
                ConfigMenuState.ResetState();
                ConfigCommandController_SetFocus_Patch.ResetTracking();
                ConfigCommandController_SetSliderValue_Patch.ResetTracking();
            }
        }
    }

    /// <summary>
    /// Manual patch application for config menu.
    /// Handles controls reading (keyboard/gamepad bindings).
    /// </summary>
    public static class ConfigMenuPatches
    {
        /// <summary>
        /// Applies config menu patches using manual Harmony patching.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            // Patch controls/keys settings navigation
            try
            {
                var selectContentMethod = AccessTools.Method(
                    typeof(Il2CppLast.UI.KeyInput.ConfigKeysSettingController), "SelectContent");
                if (selectContentMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(ConfigMenuPatches), nameof(SelectContent_Postfix));
                    harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Config Menu] Controls SelectContent patch applied");
                }
                else
                {
                    MelonLogger.Warning("[Config Menu] ConfigKeysSettingController.SelectContent not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Config Menu] Error applying controls patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ConfigKeysSettingController.SelectContent.
        /// Announces action name and key bindings when navigating controls settings.
        /// </summary>
        public static void SelectContent_Postfix(
            Il2CppLast.UI.KeyInput.ConfigKeysSettingController __instance,
            int index,
            Il2CppSystem.Collections.Generic.IEnumerable<Il2CppLast.UI.KeyInput.ConfigControllCommandController> contentList)
        {
            try
            {
                if (__instance == null || contentList == null) return;

                var command = SelectContentHelper.TryGetItem(
                    contentList.TryCast<Il2CppSystem.Collections.Generic.List<Il2CppLast.UI.KeyInput.ConfigControllCommandController>>(),
                    index);
                if (command == null) return;

                var textParts = new System.Collections.Generic.List<string>();

                // Read action name from the view's nameTexts
                if (command.view != null && command.view.nameTexts != null && command.view.nameTexts.Count > 0)
                {
                    foreach (var textComp in command.view.nameTexts)
                    {
                        if (textComp != null && !string.IsNullOrWhiteSpace(textComp.text))
                        {
                            string text = textComp.text.Trim();
                            if (!text.StartsWith("MENU_") && !textParts.Contains(text))
                            {
                                textParts.Add(text);
                            }
                        }
                    }
                }

                // Read key bindings from keyboardIconController.view
                if (command.keyboardIconController != null && command.keyboardIconController.view != null)
                {
                    if (command.keyboardIconController.view.iconTextList != null)
                    {
                        for (int i = 0; i < command.keyboardIconController.view.iconTextList.Count; i++)
                        {
                            var iconText = command.keyboardIconController.view.iconTextList[i];
                            if (iconText != null && !string.IsNullOrWhiteSpace(iconText.text))
                            {
                                string text = iconText.text.Trim();
                                if (!textParts.Contains(text))
                                {
                                    textParts.Add(text);
                                }
                            }
                        }
                    }
                }

                if (textParts.Count == 0) return;

                string announcement = string.Join(" ", textParts);

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ConfigKeysSettingController.SelectContent patch: {ex.Message}");
            }
        }
    }
}
