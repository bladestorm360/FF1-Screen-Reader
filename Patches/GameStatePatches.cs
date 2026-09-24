using System;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Utils;
using SubSceneManagerMainGame = Il2CppLast.Management.SubSceneManagerMainGame;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Patches for game state transitions.
    /// Dispatches config menu bestiary states (17/18) to BestiaryManualPatches.ConfigBestiaryStateHandler.
    /// Map transition announcements are handled by MapTransitionPatches.
    /// </summary>
    internal static class GameStatePatches
    {
        // Field states from SubSceneManagerMainGame.State enum
        private const int STATE_CHANGE_MAP = 1;
        private const int STATE_FIELD_READY = 2;
        private const int STATE_PLAYER = 3;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                var changeStateMethod = AccessTools.Method(
                    typeof(SubSceneManagerMainGame),
                    "ChangeState",
                    new Type[] { typeof(SubSceneManagerMainGame.State) }
                );

                if (changeStateMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(GameStatePatches), nameof(ChangeState_Postfix));
                    harmony.Patch(changeStateMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[GameState] ChangeState patch applied");
                }
                else
                {
                    MelonLogger.Warning("[GameState] Could not find SubSceneManagerMainGame.ChangeState method");
                }

                PatchMenuResumeAfterLibrary(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GameState] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Returning from the config-menu bestiary: MenuExtraLibraryUi.GotoMenu passes
        /// FieldMap.CreateReturnMonsterLibraryArguemtns ("MenuReturnMonsterLibrary"), so FieldMap.InitMenu
        /// (the Menu-state entry) takes its library-return branch — FadeManager.FadeIn with the completion
        /// callback FieldMap.&lt;InitMenu&gt;b__92_0 (RVA 0x2FA9C0, unique), which switches FieldMap's
        /// menu state machine to ViewMenu. That callback is built only in that branch, so it marks exactly
        /// "the config menu is back on screen after the bestiary". Replaces the per-frame
        /// ConfigController.UpdateController consume (round 2, 2026-09-24).
        /// </summary>
        private static void PatchMenuResumeAfterLibrary(HarmonyLib.Harmony harmony)
        {
            try
            {
                System.Reflection.MethodInfo target = null;
                foreach (var m in typeof(Il2Cpp.FieldMap).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                {
                    if (m.Name.Contains("InitMenu") && m.Name.Contains("b__") && m.ReturnType == typeof(void)
                        && m.GetParameters().Length == 0)
                    {
                        target = m;
                        break;
                    }
                }
                if (target == null)
                {
                    MelonLogger.Warning("[GameState] FieldMap InitMenu fade-in callback not found — no config re-announce after the bestiary");
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(GameStatePatches), nameof(MenuResumedAfterLibrary_Postfix))));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameState] Error patching the library-return callback: {ex.Message}");
            }
        }

        public static void MenuResumedAfterLibrary_Postfix()
        {
            ConfigController_SetActive_Patch.OnMenuResumedAfterLibrary();
        }

        // Main-game state machine, cached from ChangeState (event) for the field-toggle announcements.
        private const int STATE_PLAYER_VALUE = 3;   // SubSceneManagerMainGame.State.Player
        private static SubSceneManagerMainGame _mainGame;

        /// <summary>
        /// True while the main game is in its Player state (on the field, player in control) — the only
        /// state whose FieldMap update (UpdatePlayer → UpdatePlayerStatePlay) handles the field toggle keys.
        /// </summary>
        internal static bool IsFieldPlayerState()
        {
            try
            {
                var mg = _mainGame;
                return mg != null && (int)mg.GetCurrentState() == STATE_PLAYER_VALUE;
            }
            catch { return false; }
        }

        /// <summary>Forget the cached main-game manager (called on the "Title" scene load).</summary>
        internal static void ResetMainGameState() => _mainGame = null;

        /// <summary>
        /// Called when game state changes. Dispatches config menu bestiary states (17/18)
        /// and handles exit back to field or other states.
        /// </summary>
        // FF1 SubSceneManagerMainGame.State: MenuLibraryUi=19 (list), MenuLibraryInfo=20 (detail).
        // (FF3/4/5 used 17/18 — these values are game-specific.)
        private const int STATE_MENU_LIBRARY_UI = 19;
        private const int STATE_MENU_LIBRARY_INFO = 20;

        public static void ChangeState_Postfix(SubSceneManagerMainGame __instance, SubSceneManagerMainGame.State state)
        {
            try
            {
                if (__instance != null) _mainGame = __instance;
                int stateValue = (int)state;

                // Field states — if we were in config bestiary, handle exit
                if (stateValue == STATE_CHANGE_MAP || stateValue == STATE_FIELD_READY || stateValue == STATE_PLAYER)
                {
                    if (BestiaryManualPatches.ConfigBestiaryStateHandler.WasInConfigBestiary)
                    {
                        BestiaryManualPatches.ConfigBestiaryStateHandler.HandleExit();
                        // Returning from the bestiary lands back on the config menu (which resumes after the
                        // bestiary's loading screen and does NOT re-fire SelectCommand). Arm the config
                        // re-announce; the library-return fade-in callback (MenuResumedAfterLibrary_Postfix)
                        // reads the row. If the exit really went to the field, that callback never runs.
                        ConfigController_SetActive_Patch.ArmReannounceAfterLibrary();
                    }

                }
                // Config menu bestiary states
                else if (stateValue == STATE_MENU_LIBRARY_UI || stateValue == STATE_MENU_LIBRARY_INFO)
                {
                    BestiaryManualPatches.ConfigBestiaryStateHandler.HandleStateChange(stateValue);
                }
                // Exiting config bestiary to another non-field state
                else if (BestiaryManualPatches.ConfigBestiaryStateHandler.WasInConfigBestiary)
                {
                    BestiaryManualPatches.ConfigBestiaryStateHandler.HandleExit();
                    ConfigController_SetActive_Patch.ArmReannounceAfterLibrary();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameState] Error in ChangeState_Postfix: {ex.Message}");
            }
        }

    }
}
