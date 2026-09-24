using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

using KeyInputTitleMenuCommandController = Il2CppLast.UI.KeyInput.TitleMenuCommandController;
using TouchTitleMenuCommandController = Il2CppLast.UI.Touch.TitleMenuCommandController;
using TitleWindowController = Il2CppLast.UI.KeyInput.TitleWindowController;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Title-screen accessibility: speaks the "Press any button" prompt whenever it appears, and clears
    /// menu states when the main menu enables.
    ///
    /// Event-driven (round 2, 2026-09-24; replaces a 0.15 s FindObjectOfType poll):
    ///  - ARM: FFI_ScreenReaderMod.OnSceneLoaded calls <see cref="ArmPressPrompt"/> when the "Title" Unity
    ///    scene loads — it loads on EVERY appearance (boot AND return-to-title).
    ///  - SPEAK: the prompt is shown by KeyInput TitleWindowController.UpdateNone (0x7D82A0), which calls
    ///    SystemIndicator.Hide() (0x4FF4A0) and in the same branch SafeActiveSet(view.startParent, true).
    ///    A postfix on SystemIndicator.Hide, while armed, reads startText one frame later (after the
    ///    activation). Other Hide callers (SceneTitleScreen.CreateInstance during the title load, field
    ///    loads) find the prompt still inactive, so they stay silent and keep the arm.
    ///  - DISARM: prompt spoken, main menu enabled (SetEnableMainMenu(true)), or the title controller gone.
    /// The old "Hide never fired on return" note came from an arm on the boot-only SplashController
    /// (commit 3fe2e3a); the scene-load arm fires on every title visit.
    /// </summary>
    public static class TitleScreenPatches
    {
        // Armed on each "Title" scene load; cleared once the prompt is spoken or the title is left.
        private static bool _pressPromptArmed = false;
        // Saw the TitleWindowController during this visit (so a later miss means the title was left).
        private static bool _titleControllerSeen = false;
        // Set true when the title main menu enables (the press-any-button screen has been dismissed).
        private static bool _titleMainMenuShown = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                TryPatchTitleMenuCommand(harmony);
                TryPatchSystemIndicatorHide(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] Error applying patches: {ex.Message}");
            }
        }

        private static void TryPatchSystemIndicatorHide(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type indicatorType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        indicatorType = asm.GetType("Il2CppLast.Systems.Indicator.SystemIndicator");
                        if (indicatorType != null) break;
                    }
                    catch { } // assembly may not expose types
                }
                if (indicatorType == null)
                {
                    MelonLogger.Warning("[Title] SystemIndicator type not found");
                    return;
                }

                var hide = AccessTools.Method(indicatorType, "Hide", Type.EmptyTypes);
                if (hide == null)
                {
                    MelonLogger.Warning("[Title] SystemIndicator.Hide not found");
                    return;
                }
                harmony.Patch(hide, postfix: new HarmonyMethod(
                    typeof(TitleScreenPatches).GetMethod(nameof(SystemIndicator_Hide_Postfix), BindingFlags.Public | BindingFlags.Static)));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] Error patching SystemIndicator.Hide: {ex.Message}");
            }
        }

        /// <summary>Arms the press-prompt announce for this title visit (called on the "Title" scene load).</summary>
        public static void ArmPressPrompt()
        {
            _pressPromptArmed = true;
            _titleControllerSeen = false;
            _titleMainMenuShown = false;
            // One check right away as well, in case the prompt is already up (normally it is not: the
            // scene loads seconds before UpdateNone shows the prompt, so this stays silent and armed).
            CoroutineManager.StartManaged(SpeakPressPromptIfShown());
        }

        /// <summary>SystemIndicator.Hide postfix: while armed, check the prompt one frame later.</summary>
        public static void SystemIndicator_Hide_Postfix()
        {
            if (!_pressPromptArmed) return;
            CoroutineManager.StartManaged(SpeakPressPromptIfShown());
        }

        private static void TryPatchTitleMenuCommand(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type keyInputType = typeof(KeyInputTitleMenuCommandController);
                var keyInputMethod = AccessTools.Method(keyInputType, "SetEnableMainMenu", new[] { typeof(bool) });
                if (keyInputMethod != null)
                {
                    var postfix = typeof(TitleScreenPatches).GetMethod(nameof(TitleMenuCommand_SetEnableMainMenu_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(keyInputMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Title] KeyInput.TitleMenuCommandController.SetEnableMainMenu not found");
                }

                Type touchType = typeof(TouchTitleMenuCommandController);
                var touchMethod = AccessTools.Method(touchType, "SetEnableMainMenu", new[] { typeof(bool) });
                if (touchMethod != null)
                {
                    var postfix = typeof(TitleScreenPatches).GetMethod(nameof(TitleMenuCommand_SetEnableMainMenu_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(touchMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Title] Touch.TitleMenuCommandController.SetEnableMainMenu not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] Error patching TitleMenuCommandController: {ex.Message}");
            }
        }

        /// <summary>
        /// One frame after a SystemIndicator.Hide while armed (UpdateNone activates view.startParent right
        /// after its Hide call): if the press-prompt Text (TitleWindowView.startText) is on screen, speak its
        /// REAL displayed text (empty during the load, set when the prompt renders; falls back to the
        /// MENU_TITLE_PRESS_TEXT constant) and disarm. Not shown yet → stay armed for the next Hide.
        /// </summary>
        private static IEnumerator SpeakPressPromptIfShown()
        {
            yield return null;

            try
            {
                if (!_pressPromptArmed) yield break;
                if (_titleMainMenuShown) { _pressPromptArmed = false; yield break; }

                // One lookup per Hide event (not a poll). The controller exists only during the title visit:
                // missing before it was seen = still loading (stay armed); missing after = title left.
                var ctrl = UnityEngine.Object.FindObjectOfType<TitleWindowController>();
                if (ctrl == null)
                {
                    if (_titleControllerSeen) _pressPromptArmed = false;
                    yield break;
                }
                _titleControllerSeen = true;

                IntPtr viewPtr = IL2CppFieldReader.ReadPointerSafe(ctrl.Pointer, IL2CppOffsets.Popup.TitleViewKeyInput);
                if (viewPtr == IntPtr.Zero) yield break;
                IntPtr textPtr = IL2CppFieldReader.ReadPointerSafe(viewPtr, IL2CppOffsets.Popup.TitleViewStartText);
                if (textPtr == IntPtr.Zero) yield break;

                var startText = new UnityEngine.UI.Text(textPtr);
                if (startText.gameObject == null || !startText.gameObject.activeInHierarchy) yield break;

                // The press prompt is on screen now — read its actual displayed text.
                _pressPromptArmed = false;
                string raw = startText.text;
                string text = !string.IsNullOrWhiteSpace(raw) ? TextUtils.StripIconMarkup(raw.Trim()) : GetPressText();
                MelonLogger.Msg($"[Title] press prompt shown, text='{raw}'");
                if (!string.IsNullOrWhiteSpace(text))
                    FFI_ScreenReaderMod.SpeakText(text, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] press prompt read failed: {ex.Message}");
            }
        }

        // Localized "Press any button" via the UiMessageConstants.MENU_TITLE_PRESS_TEXT static field.
        // Falls back to the mod's own translated text.
        private static string GetPressText()
        {
            try
            {
                var uiMsgType = Type.GetType("Il2CppUiMessageConstants, Assembly-CSharp")
                             ?? Type.GetType("UiMessageConstants, Assembly-CSharp");
                if (uiMsgType != null)
                {
                    var field = uiMsgType.GetField("MENU_TITLE_PRESS_TEXT", BindingFlags.Public | BindingFlags.Static);
                    var pressText = field?.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(pressText))
                        return TextUtils.StripIconMarkup(pressText.Trim());
                }
            }
            catch { } // constant lookup is best-effort
            return T("Press any button");
        }

        public static void TitleMenuCommand_SetEnableMainMenu_Postfix(bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    _titleMainMenuShown = true;   // press-any-button dismissed
                    _pressPromptArmed = false;
                    MenuStateRegistry.ResetAll();
                    BattleResultPatches.ClearAllBattleMenuFlags();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] Error in SetEnableMainMenu postfix: {ex.Message}");
            }
        }
    }
}
