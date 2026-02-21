using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

using SplashController = Il2CppLast.UI.SplashController;
using KeyInputTitleMenuCommandController = Il2CppLast.UI.KeyInput.TitleMenuCommandController;
using TouchTitleMenuCommandController = Il2CppLast.UI.Touch.TitleMenuCommandController;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Patches for title screen "Press any button" and title menu activation.
    /// Uses SplashController.InitializeTitle to capture text, SystemIndicator.Hide to speak it.
    /// </summary>
    public static class TitleScreenPatches
    {
        private static string pendingTitleText = null;
        private static bool isTitleScreenTextPending = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                TryPatchSplashController(harmony);
                TryPatchSystemIndicator(harmony);
                TryPatchTitleMenuCommand(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] Error applying patches: {ex.Message}");
            }
        }

        private static void TryPatchSplashController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type splashControllerType = typeof(SplashController);
                var initTitleMethod = AccessTools.Method(splashControllerType, "InitializeTitle");

                if (initTitleMethod != null)
                {
                    var postfix = typeof(TitleScreenPatches).GetMethod(nameof(SplashController_InitializeTitle_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initTitleMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Title] SplashController.InitializeTitle method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] Error patching SplashController: {ex.Message}");
            }
        }

        private static void TryPatchSystemIndicator(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type systemIndicatorType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        systemIndicatorType = asm.GetType("Il2CppLast.Systems.Indicator.SystemIndicator");
                        if (systemIndicatorType != null)
                            break;
                    }
                    catch { } // Assembly may throw on GetType
                }

                if (systemIndicatorType == null)
                {
                    MelonLogger.Warning("[Title] SystemIndicator type not found");
                    return;
                }

                var showMethod = AccessTools.Method(systemIndicatorType, "Show");
                if (showMethod != null)
                {
                    var postfix = typeof(TitleScreenPatches).GetMethod(nameof(SystemIndicator_Show_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(showMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Title] SystemIndicator.Show method not found");
                }

                var hideMethod = AccessTools.Method(systemIndicatorType, "Hide");
                if (hideMethod != null)
                {
                    var postfix = typeof(TitleScreenPatches).GetMethod(nameof(SystemIndicator_Hide_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(hideMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Title] SystemIndicator.Hide method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] Error patching SystemIndicator: {ex.Message}");
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

        #region Postfixes

        public static void SplashController_InitializeTitle_Postfix(SplashController __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                string pressText = null;

                try
                {
                    var uiMsgType = Type.GetType("Il2CppUiMessageConstants, Assembly-CSharp")
                                 ?? Type.GetType("UiMessageConstants, Assembly-CSharp");

                    if (uiMsgType != null)
                    {
                        var field = uiMsgType.GetField("MENU_TITLE_PRESS_TEXT", BindingFlags.Public | BindingFlags.Static);
                        if (field != null)
                        {
                            pressText = field.GetValue(null) as string;
                        }
                    }
                }
                catch
                {
                    // Type lookup for press text may fail; fallback below
                }

                if (!string.IsNullOrWhiteSpace(pressText))
                {
                    pendingTitleText = TextUtils.StripIconMarkup(pressText.Trim());
                }
                else
                {
                    pendingTitleText = "Press any button";
                }

                isTitleScreenTextPending = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] Error in SplashController.InitializeTitle postfix: {ex.Message}");
                pendingTitleText = "Press any button";
                isTitleScreenTextPending = true;
            }
        }

        public static void SystemIndicator_Show_Postfix(int mode)
        {
        }

        public static void SystemIndicator_Hide_Postfix()
        {
            try
            {
                if (isTitleScreenTextPending && !string.IsNullOrWhiteSpace(pendingTitleText))
                {
                    FFI_ScreenReaderMod.SpeakText(pendingTitleText, interrupt: false);

                    pendingTitleText = null;
                    isTitleScreenTextPending = false;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] Error in SystemIndicator.Hide postfix: {ex.Message}");
            }
        }

        public static void TitleMenuCommand_SetEnableMainMenu_Postfix(bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    MenuStateRegistry.ResetAll();
                    BattleResultPatches.ClearAllBattleMenuFlags();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Title] Error in SetEnableMainMenu postfix: {ex.Message}");
            }
        }

        #endregion
    }
}
