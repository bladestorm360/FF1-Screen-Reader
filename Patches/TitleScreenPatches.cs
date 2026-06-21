using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

using KeyInputTitleMenuCommandController = Il2CppLast.UI.KeyInput.TitleMenuCommandController;
using TouchTitleMenuCommandController = Il2CppLast.UI.Touch.TitleMenuCommandController;
using TitleWindowController = Il2CppLast.UI.KeyInput.TitleWindowController;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Title-screen accessibility: speaks the "Press any button" prompt whenever it appears, and clears
    /// menu states when the main menu enables.
    ///
    /// The prompt is spoken from FFI_ScreenReaderMod.OnSceneLoaded when the "Title" Unity scene loads —
    /// that scene IS the press-any-button screen (distinct from "TitleScreen" = the main menu), and it
    /// loads on EVERY appearance (boot AND return-to-title). The text is the localized
    /// MENU_TITLE_PRESS_TEXT constant (reading the rendered Text never vocalized in earlier attempts;
    /// the constant is reliable). Prior method-hook triggers (SystemIndicator.Hide / InitShortcutCommand /
    /// SetEnableStartObject) were boot-only or never fired on return — confirmed via the MelonLoader log.
    /// </summary>
    public static class TitleScreenPatches
    {
        // Set true when the title main menu enables (the press-any-button screen has been dismissed) —
        // a hard stop for the prompt poll. Reset at the start of each poll (each "Title" scene load).
        private static bool _titleMainMenuShown = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                TryPatchTitleMenuCommand(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] Error applying patches: {ex.Message}");
            }
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
        /// Started when the "Title" scene loads (the start of the multi-second title LOAD — too early to
        /// speak). Polls until the press-prompt Text (TitleWindowView.startText) is actually on screen
        /// (gameObject.activeInHierarchy), then speaks its REAL displayed text — which is empty during the
        /// load and set only when the prompt renders. Reading at that moment fixes the timing AND the text
        /// (the earlier live read failed because it read too early when the label was empty). Falls back to
        /// the MENU_TITLE_PRESS_TEXT constant if the visible label is empty. Bounded one-shot (exits on
        /// speak or timeout); fires on boot and return-to-title (both load the "Title" scene).
        /// </summary>
        public static IEnumerator WaitForPressPromptThenSpeak()
        {
            _titleMainMenuShown = false;   // fresh title visit
            bool everFound = false;        // saw the TitleWindowController at least once

            // Bounded entirely by the three exit conditions below (prompt read, main menu shown, or the
            // title controller gone) — no arbitrary timeout. The press-any-button always resolves to one
            // of these, so the poll lives only across the boot-load / return-to-title window.
            while (true)
            {
                yield return new WaitForSeconds(0.15f);

                try
                {
                    // Stop 1: the user pressed past the prompt to the main menu (prompt dismissed).
                    if (_titleMainMenuShown) yield break;

                    // Stop 2: the TitleWindowController exists ONLY during the title visit (destroyed when
                    // gameplay starts). Re-find it each tick: keep waiting while it hasn't appeared yet
                    // (still loading), but STOP once it's gone after having been found — this keeps the poll
                    // out of the gameplay sequence entirely.
                    var ctrl = UnityEngine.Object.FindObjectOfType<TitleWindowController>();
                    if (ctrl == null)
                    {
                        if (everFound) yield break;   // left the title → stop polling
                        continue;                      // not instantiated yet during the load
                    }
                    everFound = true;

                    IntPtr viewPtr = IL2CppFieldReader.ReadPointerSafe(ctrl.Pointer, IL2CppOffsets.Popup.TitleViewKeyInput);
                    if (viewPtr == IntPtr.Zero) continue;
                    IntPtr textPtr = IL2CppFieldReader.ReadPointerSafe(viewPtr, IL2CppOffsets.Popup.TitleViewStartText);
                    if (textPtr == IntPtr.Zero) continue;

                    var startText = new UnityEngine.UI.Text(textPtr);
                    if (startText.gameObject == null || !startText.gameObject.activeInHierarchy) continue;

                    // The press prompt is on screen now — read its actual displayed text.
                    string raw = startText.text;
                    string text = !string.IsNullOrWhiteSpace(raw) ? TextUtils.StripIconMarkup(raw.Trim()) : GetPressText();
                    MelonLogger.Msg($"[Title] press prompt shown, text='{raw}'");
                    if (!string.IsNullOrWhiteSpace(text))
                        FFI_ScreenReaderMod.SpeakText(text, interrupt: false);
                    yield break;
                }
                catch { } // transient read failure — keep polling
            }
        }

        // Localized "Press any button" via the UiMessageConstants.MENU_TITLE_PRESS_TEXT static field.
        // Falls back to the English literal.
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
            return "Press any button";
        }

        public static void TitleMenuCommand_SetEnableMainMenu_Postfix(bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    _titleMainMenuShown = true;   // press-any-button dismissed → stop the prompt poll
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
