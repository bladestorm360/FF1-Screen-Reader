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
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GameState] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when game state changes. Dispatches config menu bestiary states (17/18)
        /// and handles exit back to field or other states.
        /// </summary>
        public static void ChangeState_Postfix(SubSceneManagerMainGame.State state)
        {
            try
            {
                int stateValue = (int)state;

                // Field states — if we were in config bestiary, handle exit
                if (stateValue == STATE_CHANGE_MAP || stateValue == STATE_FIELD_READY || stateValue == STATE_PLAYER)
                {
                    if (BestiaryManualPatches.ConfigBestiaryStateHandler.WasInConfigBestiary)
                    {
                        BestiaryManualPatches.ConfigBestiaryStateHandler.HandleExit();
                    }

                }
                // Config menu bestiary states
                else if (stateValue == 17 || stateValue == 18)
                {
                    BestiaryManualPatches.ConfigBestiaryStateHandler.HandleStateChange(stateValue);
                }
                // Exiting config bestiary to another non-field state
                else if (BestiaryManualPatches.ConfigBestiaryStateHandler.WasInConfigBestiary)
                {
                    BestiaryManualPatches.ConfigBestiaryStateHandler.HandleExit();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameState] Error in ChangeState_Postfix: {ex.Message}");
            }
        }

    }
}
