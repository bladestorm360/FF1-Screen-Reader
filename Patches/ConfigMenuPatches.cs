using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Menus;
using FFI_ScreenReader.Utils;
using UnityEngine;
using Key = Il2CppSystem.Input.Key;
using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;
using GameCursor = Il2CppLast.UI.Cursor;
using CustomScrollViewType = Il2CppLast.UI.CustomScrollView;
using CustomScrollViewWithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;

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
            try
            {
                // The keyboard/gamepad remap sub-screen (ConfigKeysSettingController) is its own
                // controller; its navigation/assign is announced by our SelectContent / SettingInit
                // / ChangeKeySetting postfixes. Suppress the generic index-based reader whenever it
                // is active — even if IsActive is stale — so the generic reader can't double-speak,
                // read the "Keyboard"/"Gamepad" header, or read a stale cursor index after back-out.
                var keysController = UnityEngine.Object.FindObjectOfType<Il2CppLast.UI.KeyInput.ConfigKeysSettingController>();
                if (keysController != null && keysController.gameObject.activeInHierarchy)
                    return true;

                if (!IsActive)
                    return false;

                // Validate the base config controller is still active (auto-resets stuck flags).
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
                // Gate: only announce when a real config menu is open. The in-game
                // ConfigController and the title-screen OptionController both flip
                // ConfigMenuState.IsActive via their SetActive postfixes. Without that
                // gate, this base class would leak announcements in the load-game flow
                // and other contexts that share ConfigActualDetailsControllerBase.
                if (!ConfigMenuState.IsActive)
                    return;

                AnnounceSelectedConfigCommand(__instance);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ConfigActualDetails.SelectCommand patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces the focused config row as "Name: Value" (or just "Name" when valueless). Shared by
        /// the SelectCommand navigation hook and the on-open initial-focus announce; the caller gates on
        /// ConfigMenuState. Skips template placeholders and inactive controllers.
        /// </summary>
        internal static string AnnounceSelectedConfigCommand(Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase instance)
        {
            if (instance == null)
                return null;

            // Position of the focused row within the config list. CommandList is the same list the cursor
            // navigates and SelectedCommand is the focused row, so this serves both the in-game config and
            // the title-screen Options menu (both use this base). Best-effort: -1 → no position suffix.
            int index = -1, count = -1;
            try
            {
                var selectedCmd = instance.SelectedCommand;
                var list = instance.CommandList;
                if (list != null && selectedCmd != null)
                {
                    count = list.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var c = list[i];
                        if (c != null && c.Pointer == selectedCmd.Pointer) { index = i; break; }
                    }
                }
            }
            catch { } // position is best-effort

            return AnnounceConfigCommand(instance.SelectedCommand, index, count);
        }

        /// <summary>Announces a focused config command ("Name: Value", plus its "(X of Y)" row position).
        /// Returns the spoken text or null.</summary>
        internal static string AnnounceConfigCommand(ConfigCommandController selected, int index = -1, int count = -1)
        {
            if (selected == null)
                return null;

            // Skip if controller is not active (prevents announcements during scene loading)
            if (!selected.gameObject.activeInHierarchy)
                return null;

            var view = selected.view;
            if (view == null)
                return null;

            var nameText = view.NameText;
            if (nameText == null || string.IsNullOrWhiteSpace(nameText.text))
                return null;

            string menuText = nameText.text.Trim();

            // Filter out template/placeholder values
            if (menuText == "NewText" || menuText == "Text" || menuText == "Name" || menuText == "Label")
                return null;

            string configValue = ConfigMenuReader.FindConfigValueFromController(selected);

            string announcement = string.IsNullOrWhiteSpace(configValue)
                ? menuText
                : $"{menuText}: {configValue}";

            announcement = FFI_ScreenReader.Utils.MenuPosition.Format(announcement, index, count);
            FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            return announcement;
        }

        /// <summary>
        /// One-frame-delayed initial-focus announce for the TITLE-screen Options menu, whose
        /// OptionController doesn't fire SelectCommand on open (the in-game ConfigController does, so it
        /// is NOT wired here — that would double-read). OptionController.SetActive can fire during title
        /// construction and ShowConfig on user-open, so both call this; the pending guard collapses a
        /// same-frame pair to one announce. AnnounceSelectedConfigCommand still gates on the focused
        /// controller being active, so construction-time fires stay silent.
        /// </summary>
        private static bool _initialFocusPending = false;
        private static string _initialFocusSource = null;
        private static Il2CppLast.UI.KeyInput.OptionController _initialFocusOption = null;

        internal static void AnnounceInitialFocusDelayed(Il2CppLast.UI.KeyInput.OptionController inst, string source)
        {
            if (_initialFocusPending) return;
            _initialFocusPending = true;
            _initialFocusSource = source;
            _initialFocusOption = inst;
            CoroutineManager.StartManaged(InitialFocusCoroutine());
        }

        private static IEnumerator InitialFocusCoroutine()
        {
            yield return null;
            _initialFocusPending = false;
            var inst = _initialFocusOption;
            _initialFocusOption = null;
            try
            {
                if (inst != null && ConfigMenuState.IsActive)
                {
                    // Read the OptionController's ACTIVE sub-screen controller's focused row.
                    // OptionController.selectedCommand is null until the first navigation; the active
                    // page's configActualDetailsController already has its SelectedCommand set at init.
                    // The Options menu keeps several ConfigActualDetailsControllerBase instances active,
                    // so a blind FindObjectOfType returns the wrong one (the language section) — use it
                    // only as a fallback when the active controller isn't assigned yet.
                    var ctrl = inst.configActualDetailsController;
                    if (ctrl == null)
                        ctrl = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();

                    string announced = AnnounceSelectedConfigCommand(ctrl);
                    if (!string.IsNullOrEmpty(announced))
                        MelonLogger.Msg($"[Config Menu] Initial focus announced via {_initialFocusSource}: {announced}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing initial config focus: {ex.Message}");
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
                // NOTE: no initial-focus announce here — the in-game ConfigController fires SelectCommand
                // for the first row itself on open, so announcing again would double-read.
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
    /// Mirror of ConfigController_SetActive_Patch for the title-screen options menu.
    /// Il2CppLast.UI.KeyInput.OptionController hosts language / screen / key / sound
    /// settings on the title screen via the same ConfigActualDetailsControllerBase
    /// pipeline as the in-game config — so we drive ConfigMenuState the same way.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.OptionController), "SetActive", new Type[] { typeof(bool) })]
    public static class OptionController_SetActive_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.UI.KeyInput.OptionController __instance, bool isActive)
        {
            if (isActive)
            {
                FFI_ScreenReaderMod.ClearOtherMenuStates("Config");
                ConfigMenuState.IsActive = true;
                // No initial-focus announce here: the title Options LIST (Privacy Policy / Configuration
                // / … / Language) is TitleMenuCommandController, announced by TitleMenuPatches'
                // InitializeOption hook. SetActive's configActualDetailsController points at the always-
                // present Language section, so announcing here read the wrong row ("Language"). The
                // Config-settings sub-screen is still announced by ShowConfig below.
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
    /// Title-screen Options: ShowConfig() fires when the user opens the Options list (SetActive may
    /// have already run during title construction). Announces the initially-focused row.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.OptionController), "ShowConfig")]
    public static class OptionController_ShowConfig_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.UI.KeyInput.OptionController __instance)
        {
            ConfigActualDetails_SelectCommand_Patch.AnnounceInitialFocusDelayed(__instance, "OptionController.ShowConfig");
        }
    }

    /// <summary>
    /// Title-screen Language: when the language option is highlighted (closed dropdown), announce the
    /// current "Language: English" — the same the navigation-repeat reads. This is the highlighted state,
    /// NOT the open-items state, and is specific to the language selector (so it can't re-announce the
    /// Options list like the removed SetActive hook did).
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.OptionController), "InitSelectLanguage")]
    public static class OptionController_InitSelectLanguage_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.UI.KeyInput.OptionController __instance)
        {
            ConfigActualDetails_SelectCommand_Patch.AnnounceInitialFocusDelayed(__instance, "InitSelectLanguage");
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
                // ConfigKeysSettingController has TWO SelectContent overloads (the 5-arg
                // navigation method and a 2-arg variant), so AccessTools.Method without an
                // explicit Type[] throws AmbiguousMatchException — disambiguate to the
                // navigation overload: SelectContent(int, CustomScrollView, Cursor,
                // IEnumerable<ConfigControllCommandController>, CustomScrollView.WithinRangeType).
                var selectContentMethod = AccessTools.Method(
                    typeof(Il2CppLast.UI.KeyInput.ConfigKeysSettingController), "SelectContent",
                    new Type[]
                    {
                        typeof(int),
                        typeof(CustomScrollViewType),
                        typeof(GameCursor),
                        typeof(Il2CppSystem.Collections.Generic.IEnumerable<Il2CppLast.UI.KeyInput.ConfigControllCommandController>),
                        typeof(CustomScrollViewWithinRangeType)
                    });
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

            // Title-screen Language dropdown (keyboard/gamepad uses a Unity Dropdown driven by the
            // KeyInput OptionController). SetDropDownItemFocus is the discrete, event-driven hook —
            // it announces the focused language directly (no dedup), gated on the config menu being open.
            // DO NOT hook OptionController.UpdateSelectLanguage — it is an EMPTY method whose body is
            // the shared 0x2698F0 stub (2,458 methods); detouring it corrupts them all → launch crash.
            void PatchOption(string method, string postfixName)
            {
                try
                {
                    var m = AccessTools.Method(typeof(Il2CppLast.UI.KeyInput.OptionController), method);
                    if (m != null)
                    {
                        harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuPatches), postfixName)));
                        MelonLogger.Msg($"[Config Menu] OptionController.{method} patch applied");
                    }
                    else
                    {
                        MelonLogger.Warning($"[Config Menu] OptionController.{method} not found");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Config Menu] Error patching OptionController.{method}: {ex.Message}");
                }
            }

            PatchOption("SetDropDownItemFocus", nameof(SetDropDownItemFocus_Postfix));

            // Remap assign-flow speaking (ConfigKeysSettingController, all real-bodied methods).
            // KeyboardSettingInit / GamePadSettingInit fire on entering assign mode → "press a
            // key/button" prompt. ChangeKeySetting (overloaded keyboard + gamepad) fires when the
            // binding is applied → announce the new mapping.
            void PatchKeysSetting(string method, string postfixName)
            {
                try
                {
                    var m = AccessTools.Method(typeof(Il2CppLast.UI.KeyInput.ConfigKeysSettingController), method);
                    if (m != null)
                    {
                        harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuPatches), postfixName)));
                        MelonLogger.Msg($"[Config Menu] ConfigKeysSettingController.{method} patch applied");
                    }
                    else
                    {
                        MelonLogger.Warning($"[Config Menu] ConfigKeysSettingController.{method} not found");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Config Menu] Error patching ConfigKeysSettingController.{method}: {ex.Message}");
                }
            }

            PatchKeysSetting("KeyboardSettingInit", nameof(KeyboardSettingInit_Postfix));
            PatchKeysSetting("GamePadSettingInit", nameof(GamePadSettingInit_Postfix));

            // Gamepad/Keyboard "Controls" pop-up (read-only list of every control) → navigation buffer.
            // Entering the GamePad/Keyboard Help state shows helpContentList/keyboardHelpContentList;
            // we render it once and hand it to KeyHelpReader so arrows/WASD can step the entries.
            PatchKeysSetting("GamePadHelpInit", nameof(GamePadHelpInit_Postfix));
            PatchKeysSetting("KeyboardHelpInit", nameof(KeyboardHelpInit_Postfix));
            // Leaving the help state (back to the select list, or closing the controls screen) clears it.
            PatchKeysSetting("GamePadSelectInit", nameof(ControlsHelpClose_Postfix));
            PatchKeysSetting("KeyboardSelectInit", nameof(ControlsHelpClose_Postfix));
            PatchKeysSetting("Close", nameof(ControlsHelpClose_Postfix));

            // ChangeKeySetting is overloaded — patch every overload with the same __instance-only
            // postfix (avoids AmbiguousMatchException without needing an exact Type[]).
            try
            {
                var changePostfix = new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuPatches), nameof(ChangeKeySetting_Postfix)));
                int changeCount = 0;
                foreach (var m in typeof(Il2CppLast.UI.KeyInput.ConfigKeysSettingController).GetMethods(
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                {
                    if (m.Name == "ChangeKeySetting") { harmony.Patch(m, postfix: changePostfix); changeCount++; }
                }
                MelonLogger.Msg($"[Config Menu] ConfigKeysSettingController.ChangeKeySetting patched ({changeCount} overload(s))");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Config Menu] Error patching ChangeKeySetting: {ex.Message}");
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

                string announcement = BuildCommandAnnouncement(__instance, command);
                if (string.IsNullOrWhiteSpace(announcement)) return;

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ConfigKeysSettingController.SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the controls-screen announcement for one command: action name + keyboard binding
        /// (readable key names) + gamepad binding. The gamepad icon is an unreadable controller glyph,
        /// so we translate the LIVE bound button (from the screen's KeyConfigData) to family-aware text
        /// via ControllerLabels, falling back to the raw glyph only if that can't be resolved.
        /// Shared by the navigation read (SelectContent) and the rebind read (ChangeKeySetting).
        /// </summary>
        private static string BuildCommandAnnouncement(
            Il2CppLast.UI.KeyInput.ConfigKeysSettingController owner,
            Il2CppLast.UI.KeyInput.ConfigControllCommandController command,
            bool isHelpList = false)
        {
            if (command == null) return null;

            var textParts = new System.Collections.Generic.List<string>();

            AppendCommandName(textParts, command, isHelpList);

            // Binding text. Mouse rows render their button as a glyph (no readable text), so translate the
            // bound mouse button/scroll to plain text. Keyboard rows already carry readable key names in
            // the keyboard icon controller's iconTextList (empty on the gamepad section).
            if (command.IsMouseKey)
            {
                string mouse = ResolveMouseButtonText(command);
                if (!string.IsNullOrEmpty(mouse))
                    textParts.Add($"({mouse})");
            }
            else
            {
                AppendIconTexts(textParts, command.keyboardIconController);
            }

            // Gamepad binding — ONLY when this row actually displays a gamepad binding icon
            // (gamePadIconsRoot active). That excludes non-binding rows like "Reset to Defaults" /
            // "Gamepad Controls" (whose key defaults to Action, so they would otherwise read the
            // Confirm button) and keyboard-section rows. The icon is a sprite glyph with no readable
            // text, so translate the LIVE bound button via ControllerLabels.
            var gpRoot = command.view != null ? command.view.gamePadIconsRoot : null;
            if (gpRoot != null && gpRoot.activeSelf)
            {
                // Face buttons are remappable → read the LIVE binding (remap-aware + controller-aware).
                string btn = ResolveGamepadButtonText(owner, command);
                // The fixed (non-remappable) buttons — shoulders, triggers, sticks, Start, movement —
                // aren't in the remap dictionary, so the live read returns null. Fall back to the rendered
                // glyph sprite under gamePadIconsRoot, mapped to controller-aware text. Face-button rows
                // never reach this (live read already resolved them), so they're never locked in.
                if (string.IsNullOrEmpty(btn))
                    btn = GetGamepadGlyphLabel(command);
                if (!string.IsNullOrEmpty(btn))
                    textParts.Add($"({btn})");
            }

            return textParts.Count == 0 ? null : string.Join(" ", textParts);
        }

        /// <summary>
        /// Reads a row's rendered gamepad glyph sprite (under view.gamePadIconsRoot) and maps it to a
        /// controller-aware label. Used only for the FIXED buttons, where the live keydata read can't
        /// resolve a binding (the four remappable face buttons are handled by the live read and excluded
        /// from the sprite map, so they're never read from a static glyph).
        /// </summary>
        private static string GetGamepadGlyphLabel(Il2CppLast.UI.KeyInput.ConfigControllCommandController command)
        {
            try
            {
                var gpRoot = command?.view != null ? command.view.gamePadIconsRoot : null;
                if (gpRoot == null || !gpRoot.activeSelf) return null;
                var images = gpRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                if (images == null) return null;
                for (int i = 0; i < images.Length; i++)
                {
                    var img = images[i];
                    if (img == null || !img.gameObject.activeInHierarchy) continue;
                    var sp = img.sprite;
                    if (sp == null) continue;
                    string label = GamepadGlyphSpriteToLabel(sp.name);
                    if (!string.IsNullOrEmpty(label)) return label;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Maps a controls-screen glyph sprite name ("UI_Common_&lt;Button&gt;button01") to a controller-aware
        /// label via ControllerLabels (which keys off the connected GamepadManager.GamepadType). Covers ONLY
        /// the FIXED buttons; the remappable face buttons (A/B/X/Y) and unknowns return null so they fall
        /// through to the live remap-aware read and are never locked in.
        /// </summary>
        private static string GamepadGlyphSpriteToLabel(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;

            if (Has(spriteName, "LBbutton")) return ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER);
            if (Has(spriteName, "RBbutton")) return ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER);
            if (Has(spriteName, "LTbutton")) return ControllerLabels.GetLeftTriggerLabel();
            if (Has(spriteName, "RTbutton")) return ControllerLabels.GetRightTriggerLabel();
            // Stick clicks: "L3"/"R3" (or "L Stick Click"/"R Stick Click" on Switch) — phrased as a CLICK,
            // not "LS"/"RS" which reads like moving the stick.
            if (Has(spriteName, "L3button")) return ControllerLabels.GetLeftStickClickLabel();
            if (Has(spriteName, "R3button")) return ControllerLabels.GetRightStickClickLabel();
            // The Menu (Start) button is repurposed by the mod for the mod menu and can't be remapped, so
            // the mini-map row (its only user) is announced as the mod menu rather than the physical button.
            if (Has(spriteName, "Menubutton")) return "used for mod menu";
            if (Has(spriteName, "Backbutton") || Has(spriteName, "Selectbutton") || Has(spriteName, "Viewbutton"))
                return ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_BACK);
            // The movement glyph (directional/D-pad icon). The mod repurposed the D-pad and right stick for
            // its own navigation, so only the left stick still moves the character — announce it as such.
            if (Has(spriteName, "Tenkeybutton") || Has(spriteName, "Dpadbutton")
                || Has(spriteName, "Crossbutton") || Has(spriteName, "Directionbutton"))
                return "Left Stick";

            return null;   // face buttons (A/B/X/Y) + unknowns: leave to the live read, never lock in
        }

        private static bool Has(string s, string token)
            => s.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Appends a command's action name. Remap rows carry their localized name in the view's
        /// nameTexts; HELP rows (the read-only Gamepad/Keyboard Controls list) leave nameTexts as a
        /// permanent "New Text" placeholder and instead render the real name into the controller's own
        /// messageTexts list — so for help rows we read messageTexts, falling back to resolving MessageId
        /// via the game's localization.
        /// </summary>
        private static void AppendCommandName(
            System.Collections.Generic.List<string> textParts,
            Il2CppLast.UI.KeyInput.ConfigControllCommandController command,
            bool isHelpList)
        {
            if (isHelpList)
            {
                var msgTexts = command.messageTexts;
                if (msgTexts != null)
                {
                    for (int i = 0; i < msgTexts.Count; i++)
                    {
                        var t = msgTexts[i];
                        if (t != null && IsRealName(t.text))
                        {
                            string s = t.text.Trim();
                            if (!textParts.Contains(s)) textParts.Add(s);
                        }
                    }
                }
                // Fallback: the rendered text wasn't ready — resolve the message id directly.
                if (textParts.Count == 0)
                {
                    string loc = TryGetMessage(command.MessageId);
                    if (IsRealName(loc)) textParts.Add(loc.Trim());
                }
                return;
            }

            // Remap rows: name from the view's nameTexts.
            if (command.view != null && command.view.nameTexts != null && command.view.nameTexts.Count > 0)
            {
                foreach (var textComp in command.view.nameTexts)
                {
                    if (textComp != null && !string.IsNullOrWhiteSpace(textComp.text))
                    {
                        string text = textComp.text.Trim();
                        if (!text.StartsWith("MENU_") && !textParts.Contains(text))
                            textParts.Add(text);
                    }
                }
            }
        }

        /// <summary>True if the text is a usable name (not blank or an editor placeholder).</summary>
        private static bool IsRealName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.Trim();
            return t != "New Text" && t != "NewText" && t != "Text" && t != "Name" && t != "Label";
        }

        /// <summary>Resolves a message id to its localized text via the game's MessageManager.</summary>
        private static string TryGetMessage(string messageId)
        {
            if (string.IsNullOrWhiteSpace(messageId)) return null;
            try
            {
                var mm = Il2CppLast.Management.MessageManager.Instance;
                if (mm != null)
                {
                    string text = mm.GetMessage(messageId, false);
                    if (!string.IsNullOrWhiteSpace(text))
                        return TextUtils.StripIconMarkup(text);
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Resolves a remap row's CURRENT (remappable) gamepad button to family-aware text. Reads the
        /// live binding from the screen's KeyConfigData (GameKey → Unity KeyCode), maps the KeyCode to
        /// an SDL button index, and lets ControllerLabels pick the text for the connected controller.
        /// Returns null if it can't be resolved (caller falls back to the raw glyph).
        /// </summary>
        private static string ResolveGamepadButtonText(
            Il2CppLast.UI.KeyInput.ConfigKeysSettingController owner,
            Il2CppLast.UI.KeyInput.ConfigControllCommandController command)
        {
            try
            {
                if (owner == null || command == null) return null;
                var kd = owner.keydata;
                if (kd == null) return null;
                var dict = kd.GetGamePadKeyConfigtDictionary();
                if (dict == null || !dict.ContainsKey(command.key)) return null;
                int sdl = JoystickKeyCodeToSdlButton((int)dict[command.key]);
                if (sdl < 0) return null;
                return ControllerLabels.GetButtonLabel(sdl);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Resolves a mouse row's bound button/scroll to plain text by reading its rendered glyph sprite
        /// ("UI_Common_mouse_l/_r/_rad", under the row's keyboardIconsRoot). The glyph tracks the LIVE
        /// binding, so this stays correct after the user rebinds a mouse control — unlike the game's default
        /// mouse config, which is keyed by GameKey and doesn't match these rows' placeholder keys. Mirrors
        /// the gamepad fixed-button glyph read. Returns null on failure (degrades to no binding).
        /// </summary>
        private static string ResolveMouseButtonText(Il2CppLast.UI.KeyInput.ConfigControllCommandController command)
        {
            try
            {
                if (command == null) return null;
                var root = command.view != null ? command.view.keyboardIconsRoot : null;
                UnityEngine.UI.Image[] imgs = root != null
                    ? root.GetComponentsInChildren<UnityEngine.UI.Image>(true)
                    : (command.gameObject != null ? command.gameObject.GetComponentsInChildren<UnityEngine.UI.Image>(true) : null);
                if (imgs == null) return null;
                for (int i = 0; i < imgs.Length; i++)
                {
                    var img = imgs[i];
                    if (img == null || !img.gameObject.activeInHierarchy || img.sprite == null) continue;
                    string label = MouseSpriteToLabel(img.sprite.name);
                    if (!string.IsNullOrEmpty(label)) return label;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Maps a mouse glyph sprite name ("UI_Common_mouse_l/_r/_rad/…") to plain text. Order matters:
        /// "mouse_rad" (the scroll wheel) contains "mouse_r", so the wheel must be checked before right.
        /// </summary>
        private static string MouseSpriteToLabel(string s)
        {
            if (string.IsNullOrEmpty(s) || !Has(s, "mouse")) return null;       // not a mouse glyph
            if (Has(s, "mouse_rad") || Has(s, "wheel") || Has(s, "scroll")) return "Mouse Wheel";
            if (Has(s, "mouse_l")) return "Left Mouse Button";
            if (Has(s, "mouse_r")) return "Right Mouse Button";
            if (Has(s, "mouse_c") || Has(s, "mouse_m")) return "Middle Mouse Button";
            return "Mouse Button";   // a rebound extra button (gaming mouse) whose glyph we don't name explicitly
        }

        /// <summary>
        /// Maps a Unity legacy KeyCode.JoystickButtonN (330+) to an SDL gamepad button index using the
        /// XInput-standard layout (correct for Xbox + most PC controllers). ControllerLabels then yields
        /// the right family text for whichever controller is connected. Returns -1 if not a mapped button.
        /// </summary>
        private static int JoystickKeyCodeToSdlButton(int keyCode)
        {
            switch (keyCode)
            {
                // FFPR keeps the bottom/right face buttons in the Japanese arrangement: Confirm is
                // stored on JoystickButton1 and Cancel on JoystickButton0. The American build confirms
                // with the BOTTOM button (A/Cross), so JB1→SOUTH and JB0→EAST — i.e. swapped from
                // Unity's XInput default. (X/Y below are unaffected.)
                case 330: return SDL3.SDL_GAMEPAD_BUTTON_EAST;           // JoystickButton0 — Cancel (B/Circle)
                case 331: return SDL3.SDL_GAMEPAD_BUTTON_SOUTH;          // JoystickButton1 — Confirm (A/Cross)
                case 332: return SDL3.SDL_GAMEPAD_BUTTON_WEST;           // JoystickButton2 — X/Square
                case 333: return SDL3.SDL_GAMEPAD_BUTTON_NORTH;          // JoystickButton3 — Y/Triangle
                case 334: return SDL3.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER;  // JoystickButton4 — LB
                case 335: return SDL3.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER; // JoystickButton5 — RB
                case 336: return SDL3.SDL_GAMEPAD_BUTTON_BACK;           // JoystickButton6 — Back/View
                case 337: return SDL3.SDL_GAMEPAD_BUTTON_START;          // JoystickButton7 — Start/Menu
                case 338: return SDL3.SDL_GAMEPAD_BUTTON_LEFT_STICK;     // JoystickButton8 — LS
                case 339: return SDL3.SDL_GAMEPAD_BUTTON_RIGHT_STICK;    // JoystickButton9 — RS
                default: return -1;
            }
        }

        /// <summary>Appends an icon controller's binding labels (iconTextList) to textParts, deduped.</summary>
        private static void AppendIconTexts(System.Collections.Generic.List<string> textParts, ConfigKeyIconController icon)
        {
            var iconView = icon?.view;
            if (iconView == null || iconView.iconTextList == null) return;
            for (int i = 0; i < iconView.iconTextList.Count; i++)
            {
                var iconText = iconView.iconTextList[i];
                if (iconText != null && !string.IsNullOrWhiteSpace(iconText.text))
                {
                    string text = iconText.text.Trim();
                    // Gamepad-help rows leave the keyboard icon text as a "New Text" placeholder; skip it.
                    if (IsRealName(text) && !textParts.Contains(text))
                        textParts.Add(text);
                }
            }
        }

        // ── Title-screen Language dropdown ──────────────────────────────────────────────
        // SetDropDownItemFocus is the EVENT-DRIVEN announce (no dedup), gated to when the config menu
        // is actually open so it cannot speak the current language at the title "Press any button."

        /// <summary>Focused language label: prefer the tracked dropdown item, else the dropdown value.</summary>
        private static string GetFocusedLanguageLabel(Il2CppLast.UI.KeyInput.OptionController inst)
        {
            var item = inst.selectedItem;
            if (item == null) return null;

            if (item.view != null && item.view.LabelText != null)
            {
                string t = item.view.LabelText.text;
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }
            var dd = inst.selectedDoropDown;
            if (dd != null && dd.options != null && dd.value >= 0 && dd.value < dd.options.Count)
            {
                var opt = dd.options[dd.value];
                if (opt != null && !string.IsNullOrWhiteSpace(opt.text)) return opt.text;
            }
            // The CURRENT language's item has an empty LabelText (its native name is a sprite; the
            // parenthetical English name is omitted for the current language). A focused item with no
            // readable label is therefore the current language → name it via the game's LangugeUtility.
            return ConfigMenuReader.GetCurrentLanguageDisplayName();
        }

        /// <summary>
        /// EVENT-DRIVEN announce. OptionController.SetDropDownItemFocus (KeyInput) is the discrete
        /// "focused dropdown item changed" hook — speaks the focused language directly, NO dedup.
        /// </summary>
        // The game fires SetDropDownItemFocus twice when the dropdown opens; track the last spoken label
        // to drop an immediately-identical repeat. Cleared when the menu isn't open so re-opening
        // re-announces. (String compare, not time-based.)
        private static string lastDropDownLabel = null;

        public static void SetDropDownItemFocus_Postfix(Il2CppLast.UI.KeyInput.OptionController __instance)
        {
            try
            {
                if (__instance == null) return;
                string label = GetFocusedLanguageLabel(__instance);
                // Gate on the config menu being genuinely OPEN — the same lifecycle flag SelectCommand
                // uses (driven by ConfigController/OptionController.SetActive). The title screen
                // instantiates the language OptionController during load and fires SetDropDownItemFocus
                // BEFORE the menu is opened (IsActive=false), which was speaking the current language
                // over "Press any button." Real dropdown navigation happens with the menu open
                // (IsActive=true), so this kills the title regression without muting the language menu.
                if (!ConfigMenuState.IsActive) { lastDropDownLabel = null; return; }
                if (string.IsNullOrWhiteSpace(label)) return;
                string trimmed = label.Trim();
                if (trimmed == lastDropDownLabel) return;   // drop the open-time duplicate fire (dedup on the bare label)
                lastDropDownLabel = trimmed;

                // Position within the language list. The dropdown's `value` is the COMMITTED selection
                // (it does NOT track the highlighted row), so derive the focused index from the focused
                // item's position within the item list instead.
                int index = -1, count = -1;
                try
                {
                    var list = __instance.dropdownItemList;   // List<CommandDropdownItemController>
                    var item = __instance.selectedItem;        // the highlighted item
                    if (list != null && item != null)
                    {
                        count = list.Count;
                        for (int i = 0; i < list.Count; i++)
                        {
                            var li = list[i];
                            if (li != null && li.Pointer == item.Pointer) { index = i; break; }
                        }
                    }
                }
                catch { } // best-effort; -1 → no suffix

                FFI_ScreenReaderMod.SpeakText(FFI_ScreenReader.Utils.MenuPosition.Format(trimmed, index, count), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetDropDownItemFocus patch: {ex.Message}");
            }
        }

        // ── Remap assign-flow (ConfigKeysSettingController) ──────────────────────────────
        // Entering assign mode → announce the "press a key/button" prompt. Init methods fire once on
        // state entry → event-driven, no dedup.
        public static void KeyboardSettingInit_Postfix(Il2CppLast.UI.KeyInput.ConfigKeysSettingController __instance)
            => AnnounceAssignPrompt(__instance, gamepad: false);

        public static void GamePadSettingInit_Postfix(Il2CppLast.UI.KeyInput.ConfigKeysSettingController __instance)
            => AnnounceAssignPrompt(__instance, gamepad: true);

        private static void AnnounceAssignPrompt(Il2CppLast.UI.KeyInput.ConfigKeysSettingController inst, bool gamepad)
        {
            try
            {
                if (inst == null) return;
                FFI_ScreenReaderMod.SpeakText(gamepad ? "Press a button." : "Press a key.", interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in assign-prompt patch: {ex.Message}");
            }
        }

        // ── Gamepad/Keyboard Controls pop-up (read-only controls list) → navigation buffer ──
        // The pop-up is ConfigKeysSettingController entering its GamePad/Keyboard Help state (NOT a
        // KeyHelpController.SetValues event — that's the always-present hint bar). On state entry we
        // render helpContentList/keyboardHelpContentList once and hand the strings to KeyHelpReader,
        // which gates KeyContext.KeyHelp and exposes arrow/WASD navigation over them.

        public static void GamePadHelpInit_Postfix(Il2CppLast.UI.KeyInput.ConfigKeysSettingController __instance)
            => OpenControlsHelp(__instance, gamepad: true);

        public static void KeyboardHelpInit_Postfix(Il2CppLast.UI.KeyInput.ConfigKeysSettingController __instance)
            => OpenControlsHelp(__instance, gamepad: false);

        /// <summary>Returning to the controls list (Select state) or closing the screen tears the buffer down.</summary>
        public static void ControlsHelpClose_Postfix() => KeyHelpReader.CloseControlsHelp();

        private static void OpenControlsHelp(Il2CppLast.UI.KeyInput.ConfigKeysSettingController inst, bool gamepad)
        {
            try
            {
                if (inst == null) return;
                // One-frame delay so each row's binding text is populated before we render it.
                CoroutineManager.StartManaged(DelayedOpenControlsHelp(inst, gamepad));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error opening controls help: {ex.Message}");
            }
        }

        private static IEnumerator DelayedOpenControlsHelp(Il2CppLast.UI.KeyInput.ConfigKeysSettingController inst, bool gamepad)
        {
            yield return null;
            System.Collections.Generic.List<string> entries = null;
            try
            {
                // The state machine cycles its Help-state Init callbacks during scene construction (e.g.
                // the new-game scene-load flurry), firing this hook while the controls screen isn't shown.
                // Only build/announce when the controller is genuinely on-screen.
                if (inst == null || inst.gameObject == null || !inst.gameObject.activeInHierarchy)
                {
                    KeyHelpReader.CloseControlsHelp();
                }
                else
                {
                    var list = gamepad ? inst.HelpContentList : inst.KeyboardHelpContentList;
                    entries = BuildHelpEntries(inst, list);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading controls help list: {ex.Message}");
            }
            if (entries != null && entries.Count > 0)
                KeyHelpReader.OpenControlsHelp(inst, entries);
        }

        /// <summary>Renders each control row in a help list to "action + binding" via BuildCommandAnnouncement.</summary>
        private static System.Collections.Generic.List<string> BuildHelpEntries(
            Il2CppLast.UI.KeyInput.ConfigKeysSettingController owner,
            Il2CppSystem.Collections.Generic.List<Il2CppLast.UI.KeyInput.ConfigControllCommandController> list)
        {
            var entries = new System.Collections.Generic.List<string>();
            if (list == null) return entries;
            for (int i = 0; i < list.Count; i++)
            {
                var cmd = list[i];
                if (cmd == null) continue;
                string ann = BuildCommandAnnouncement(owner, cmd, isHelpList: true);
                if (!string.IsNullOrWhiteSpace(ann)) entries.Add(ann);
            }
            return entries;
        }

        /// <summary>
        /// Postfix for ConfigKeysSettingController.ChangeKeySetting (all overloads). Fires when a
        /// binding is applied — re-reads the just-edited command and announces the new mapping.
        /// Event-driven, no dedup.
        /// </summary>
        public static void ChangeKeySetting_Postfix(Il2CppLast.UI.KeyInput.ConfigKeysSettingController __instance)
        {
            try
            {
                if (__instance == null) return;
                string announcement = BuildCommandAnnouncement(__instance, __instance.selectedCommand);
                if (string.IsNullOrWhiteSpace(announcement)) return;
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ChangeKeySetting patch: {ex.Message}");
            }
        }
    }
}
