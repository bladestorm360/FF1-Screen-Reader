using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

// FF1 Battle types
using BattleController = Il2CppLast.Battle.BattleController;
using BattlePopPlug = Il2CppLast.Battle.BattlePopPlug;
using BattlePlugManager = Il2CppLast.Battle.BattlePlugManager;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Battle start patches for announcing battle conditions.
    /// Handles: "Preemptive Attack!", "Back Attack!", "Ambush!", etc.
    /// </summary>
    public static class BattleStartPatches
    {
        // PreeMptiveState enum values (from dump.cs)
        private const int STATE_NON = -1;
        private const int STATE_NORMAL = 0;
        private const int STATE_PREEMPTIVE = 1;
        private const int STATE_BACK_ATTACK = 2;
        private const int STATE_ENEMY_PREEMPTIVE = 3;
        private const int STATE_ENEMY_SIDE_ATTACK = 4;
        private const int STATE_SIDE_ATTACK = 5;

        private static bool announcedBattleStart = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[Battle Start] Applying battle start patches...");

                // Patch StartPreeMptiveMes for battle condition announcements
                PatchStartPreeMptiveMes(harmony);

                // Patch ExitPreeMptive to reset state
                PatchExitPreeMptive(harmony);

                // Patch StartEscape for escape message
                PatchStartEscape(harmony);

                MelonLogger.Msg("[Battle Start] Battle start patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Battle Start] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch StartPreeMptiveMes to announce battle start condition.
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
                    MelonLogger.Msg("[Battle Start] Patched StartPreeMptiveMes successfully");
                }
                else
                {
                    // Debug: dump state methods
                    MelonLogger.Warning("[Battle Start] StartPreeMptiveMes method not found, dumping Start* methods:");
                    var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    foreach (var m in methods)
                    {
                        if (m.Name.StartsWith("Start") || m.Name.Contains("PreeMptive"))
                        {
                            MelonLogger.Msg($"[Battle Start]   - {m.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error patching StartPreeMptiveMes: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch ExitPreeMptive to reset announcement state.
        /// </summary>
        private static void PatchExitPreeMptive(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(BattleController);

                // Use AccessTools for better IL2CPP compatibility
                var method = AccessTools.Method(controllerType, "ExitPreeMptive");

                if (method == null)
                {
                    method = controllerType.GetMethod(
                        "ExitPreeMptive",
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public
                    );
                }

                if (method != null)
                {
                    var prefix = typeof(BattleStartPatches).GetMethod(
                        nameof(ExitPreeMptive_Prefix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                    MelonLogger.Msg("[Battle Start] Patched ExitPreeMptive successfully");
                }
                else
                {
                    MelonLogger.Warning("[Battle Start] ExitPreeMptive method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error patching ExitPreeMptive: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch StartEscape to announce escape message.
        /// </summary>
        private static void PatchStartEscape(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(BattleController);

                // Use AccessTools for better IL2CPP compatibility
                var method = AccessTools.Method(controllerType, "StartEscape");

                if (method == null)
                {
                    method = controllerType.GetMethod(
                        "StartEscape",
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public
                    );
                }

                if (method != null)
                {
                    var postfix = typeof(BattleStartPatches).GetMethod(
                        nameof(StartEscape_Postfix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Battle Start] Patched StartEscape successfully");
                }
                else
                {
                    MelonLogger.Warning("[Battle Start] StartEscape method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error patching StartEscape: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for StartPreeMptiveMes - announces battle start condition.
        /// </summary>
        public static void StartPreeMptiveMes_Postfix(BattleController __instance)
        {
            try
            {
                // Mark that we're in battle (enables state clearing on battle end)
                BattleStateHelper.OnBattleStart();

                // Reset battle result state for this new battle
                BattleResultPatches.ResetState();

                if (announcedBattleStart) return;
                announcedBattleStart = true;

                // Get the preemptive state
                int state = GetPreeMptiveState(__instance);
                MelonLogger.Msg($"[Battle Start] PreeMptiveState = {state}");

                // Get announcement based on state
                string announcement = GetBattleStartAnnouncement(state);
                if (string.IsNullOrEmpty(announcement)) return;

                MelonLogger.Msg($"[Battle Start] Announcing: {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error in StartPreeMptiveMes_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Prefix for ExitPreeMptive - resets announcement state.
        /// </summary>
        public static void ExitPreeMptive_Prefix()
        {
            // Reset for next battle
            announcedBattleStart = false;
        }

        /// <summary>
        /// Postfix for StartEscape - announces escape success.
        /// </summary>
        public static void StartEscape_Postfix()
        {
            try
            {
                MelonLogger.Msg("[Battle Start] Party escaped!");
                FFI_ScreenReaderMod.SpeakText("The party escaped!", interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error in StartEscape_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Get the preemptive state from BattlePlugManager (singleton).
        /// Uses direct IL2CPP access instead of .NET reflection.
        /// </summary>
        private static int GetPreeMptiveState(BattleController controller)
        {
            try
            {
                // BattlePopPlug is stored in BattlePlugManager singleton, not BattleController
                var plugManager = BattlePlugManager.Instance();
                if (plugManager == null)
                {
                    MelonLogger.Warning("[Battle Start] BattlePlugManager.Instance() is null");
                    return STATE_NORMAL;
                }

                // Direct IL2CPP property access (not .NET reflection)
                var battlePopPlug = plugManager.BattlePopPlug;
                if (battlePopPlug == null)
                {
                    MelonLogger.Warning("[Battle Start] BattlePopPlug is null");
                    return STATE_NORMAL;
                }

                // Direct method call on IL2CPP type
                var result = battlePopPlug.GetResult();
                return (int)result;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error getting PreeMptiveState: {ex.Message}");
            }

            return STATE_NORMAL;
        }

        /// <summary>
        /// Get announcement text for battle start condition.
        /// </summary>
        private static string GetBattleStartAnnouncement(int state)
        {
            switch (state)
            {
                case STATE_PREEMPTIVE:
                    return "Preemptive attack!";
                case STATE_BACK_ATTACK:
                    return "Back attack!";
                case STATE_ENEMY_PREEMPTIVE:
                    return "Ambush!";
                case STATE_ENEMY_SIDE_ATTACK:
                    return "Enemy side attack!";
                case STATE_SIDE_ATTACK:
                    return "Side attack!";
                case STATE_NORMAL:
                case STATE_NON:
                default:
                    return null; // No announcement for normal battles
            }
        }

        /// <summary>
        /// Reset state (call at battle end).
        /// </summary>
        public static void ResetState()
        {
            announcedBattleStart = false;
            // Also reset battle result state for next battle
            BattleResultPatches.ResetState();
        }
    }
}
