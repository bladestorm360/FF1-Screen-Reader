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
    /// Mirror of ConfigController_SetActive_Patch for the title-screen options menu.
    /// Il2CppLast.UI.KeyInput.OptionController hosts language / screen / key / sound
    /// settings on the title screen via the same ConfigActualDetailsControllerBase
    /// pipeline as the in-game config — so we drive ConfigMenuState the same way.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.OptionController), "SetActive", new Type[] { typeof(bool) })]
    public static class OptionController_SetActive_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isActive)
        {
            if (isActive)
            {
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
            Il2CppLast.UI.KeyInput.ConfigControllCommandController command)
        {
            if (command == null) return null;

            var textParts = new System.Collections.Generic.List<string>();

            // Action name from the view's nameTexts
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

            // Keyboard binding — already readable key names.
            AppendIconTexts(textParts, command.keyboardIconController);

            // Gamepad binding — the icon is a sprite glyph carrying NO readable text (iconTextList is
            // empty), so reading it never worked. The keyboard and gamepad remap sections are mutually
            // exclusive per row: keyboard rows carry a key name, gamepad rows don't. So when the keyboard
            // icon is empty we're on the gamepad section — translate the LIVE bound button via
            // ControllerLabels (the keyboard binding above already handled keyboard-section rows).
            if (ResolveGamepadButtonText(owner, command) is string btn && !string.IsNullOrEmpty(btn)
                && !IconHasContent(command.keyboardIconController))
            {
                textParts.Add($"({btn})");
            }

            return textParts.Count == 0 ? null : string.Join(" ", textParts);
        }

        /// <summary>True if an icon controller is currently showing readable binding text.</summary>
        private static bool IconHasContent(ConfigKeyIconController icon)
        {
            var iconView = icon?.view;
            if (iconView == null || iconView.iconTextList == null) return false;
            for (int i = 0; i < iconView.iconTextList.Count; i++)
            {
                var t = iconView.iconTextList[i];
                if (t != null && !string.IsNullOrWhiteSpace(t.text)) return true;
            }
            return false;
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
                    if (!textParts.Contains(text))
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
                if (!ConfigMenuState.IsActive) return;
                if (string.IsNullOrWhiteSpace(label)) return;
                FFI_ScreenReaderMod.SpeakText(label.Trim(), interrupt: true);
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
