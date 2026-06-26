using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

// FF1 Battle types
using BattleController = Il2CppLast.Battle.BattleController;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Battle-start lifecycle hook.
    /// The battle-start condition itself ("Preemptive strike!", "Back attack!", etc.) is the game's own
    /// on-screen message and is already spoken by BattleMessagePatches.SetMessage_Postfix. We do NOT
    /// synthesize a second announcement here — doing so produced a duplicate at battle start ("Preemptive
    /// strike!" from the game message + "Preemptive attack!" from the synthetic announce). This patch now
    /// exists solely to mark the in-battle lifecycle (which enables state clearing on battle end).
    /// </summary>
    public static class BattleStartPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch StartPreeMptiveMes purely as the battle-start lifecycle hook.
                PatchStartPreeMptiveMes(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Battle Start] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch StartPreeMptiveMes to mark battle start (lifecycle only).
        /// </summary>
        private static void PatchStartPreeMptiveMes(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(BattleController);

                // Use AccessTools for better IL2CPP compatibility
                var method = AccessTools.Method(controllerType, "StartPreeMptiveMes");

                if (method == null)
                {
                    // Fallback to reflection
                    method = controllerType.GetMethod(
                        "StartPreeMptiveMes",
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public
                    );
                }

                if (method != null)
                {
                    var postfix = typeof(BattleStartPatches).GetMethod(
                        nameof(StartPreeMptiveMes_Postfix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Battle Start] StartPreeMptiveMes method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error patching StartPreeMptiveMes: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for StartPreeMptiveMes - marks the in-battle lifecycle. The condition message itself is
        /// spoken by BattleMessagePatches.SetMessage_Postfix (the game's own battle message), so this no
        /// longer announces anything (that produced a duplicate at battle start).
        /// </summary>
        public static void StartPreeMptiveMes_Postfix()
        {
            try
            {
                // Mark that we're in battle (enables state clearing on battle end)
                BattleStateHelper.OnBattleStart();

                // Reset battle result state for this new battle
                BattleResultPatches.ResetState();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error in StartPreeMptiveMes_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset state (call at battle end).
        /// </summary>
        public static void ResetState()
        {
            // Reset battle result state for next battle
            BattleResultPatches.ResetState();
        }
    }
}
