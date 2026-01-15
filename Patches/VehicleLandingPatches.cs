using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Utils;
using FFI_ScreenReader.Core;

// FF1 types - may need adjustment
using Il2CppLast.Map;
using Il2CppLast.Entity.Field;
using MapUIManager = Il2CppLast.Map.MapUIManager;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Announces when player enters a zone where vehicle can land.
    /// Patches MapUIManager.SwitchLandable which is called by the game
    /// when the landing state changes based on terrain under the vehicle.
    /// Ported from FF3 screen reader.
    /// NOTE: May require debugging - FF1 class names/offsets may differ.
    /// </summary>
    public static class VehicleLandingPatches
    {
        private static bool isPatched = false;
        private static bool lastLandableState = false;

        /// <summary>
        /// Apply manual Harmony patches for landing zone detection.
        /// Called from FFI_ScreenReaderMod initialization.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchSwitchLandable(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Landing] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch SwitchLandable - called when terrain under vehicle changes landing validity.
        /// </summary>
        private static void TryPatchSwitchLandable(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type mapUIManagerType = typeof(MapUIManager);

                // Find SwitchLandable(bool)
                var methods = mapUIManagerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                MethodInfo targetMethod = null;

                foreach (var method in methods)
                {
                    if (method.Name == "SwitchLandable")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                        {
                            MelonLogger.Msg($"[Landing] Found SwitchLandable(bool)");
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(VehicleLandingPatches).GetMethod(nameof(SwitchLandable_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Landing] Patched SwitchLandable successfully");
                }
                else
                {
                    MelonLogger.Warning("[Landing] Could not find SwitchLandable method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Landing] Error patching SwitchLandable: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SwitchLandable - announces when entering landable zone.
        /// </summary>
        public static void SwitchLandable_Postfix(bool landable)
        {
            try
            {
                // Only announce when in a vehicle (not on foot)
                if (MoveStateHelper.IsOnFoot())
                    return;

                // Only announce when entering landable zone (false -> true)
                if (landable && !lastLandableState)
                {
                    FFI_ScreenReaderMod.SpeakText("Can land", interrupt: false);
                }

                lastLandableState = landable;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Landing] Error in SwitchLandable patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset state when leaving vehicle or changing maps.
        /// </summary>
        public static void ResetState()
        {
            lastLandableState = false;
        }
    }

    /// <summary>
    /// Patches FieldPlayer.ChangeMoveState to announce when entering/exiting vehicles.
    /// Ported from FF3 screen reader.
    /// NOTE: May require debugging - FF1 class names/offsets may differ.
    /// </summary>
    public static class MovementSpeechPatches
    {
        private static bool isPatched = false;
        private static int lastMoveState = -1;

        /// <summary>
        /// Apply manual Harmony patches for movement state changes.
        /// Called from FFI_ScreenReaderMod initialization.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchChangeMoveState(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MoveState] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch ChangeMoveState - called when player changes movement mode (walk, ship, airship, etc.)
        /// </summary>
        private static void TryPatchChangeMoveState(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type fieldPlayerType = typeof(FieldPlayer);

                // Find ChangeMoveState(FieldPlayerConstants.MoveState, bool)
                var methods = fieldPlayerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                MethodInfo targetMethod = null;

                foreach (var method in methods)
                {
                    if (method.Name == "ChangeMoveState")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length >= 1)
                        {
                            MelonLogger.Msg($"[MoveState] Found ChangeMoveState with {parameters.Length} params");
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(MovementSpeechPatches).GetMethod(nameof(ChangeMoveState_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[MoveState] Patched ChangeMoveState successfully");
                }
                else
                {
                    MelonLogger.Warning("[MoveState] Could not find ChangeMoveState method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MoveState] Error patching ChangeMoveState: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ChangeMoveState - announces vehicle state changes.
        /// </summary>
        public static void ChangeMoveState_Postfix(object __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var fieldPlayer = __instance as FieldPlayer;
                if (fieldPlayer == null)
                    return;

                int currentMoveState = (int)fieldPlayer.moveState;

                // Only announce if state actually changed
                if (currentMoveState != lastMoveState)
                {
                    // Update cached state in MoveStateHelper (this will also announce the change)
                    MoveStateHelper.UpdateCachedMoveState(currentMoveState);
                    lastMoveState = currentMoveState;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MoveState] Error in ChangeMoveState patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset state tracking (call on map transitions)
        /// </summary>
        public static void ResetState()
        {
            lastMoveState = -1;
            MoveStateHelper.ResetState();
        }
    }

    /// <summary>
    /// Proactive state monitoring for world map contexts (where ships/vehicles are available).
    /// Uses coroutine to check state every 0.5 seconds, announcing changes immediately.
    /// </summary>
    public static class MoveStateMonitor
    {
        private static object stateMonitorCoroutine = null;
        private static int lastKnownState = -1;
        private const float STATE_CHECK_INTERVAL = 0.5f;

        /// <summary>
        /// Coroutine that monitors move state changes every 0.5 seconds.
        /// Only runs when on world map (where ships/vehicles are available).
        /// </summary>
        private static IEnumerator MonitorMoveStateChanges()
        {
            while (true)
            {
                yield return new WaitForSeconds(STATE_CHECK_INTERVAL);

                try
                {
                    // Read current state and detect changes
                    int currentState = MoveStateHelper.GetCurrentMoveState();

                    // Only announce if state actually changed (skip initial -1 state)
                    if (currentState != lastKnownState && lastKnownState != -1)
                    {
                        MoveStateHelper.AnnounceStateChange(lastKnownState, currentState);
                    }

                    lastKnownState = currentState;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[MoveState] Error in state monitoring coroutine: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Start state monitoring coroutine when entering world map.
        /// </summary>
        public static void StartStateMonitoring()
        {
            if (stateMonitorCoroutine == null)
            {
                lastKnownState = -1; // Reset state tracking
                stateMonitorCoroutine = MelonCoroutines.Start(MonitorMoveStateChanges());
                MelonLogger.Msg("[MoveState] Started state monitoring coroutine");
            }
        }

        /// <summary>
        /// Stop state monitoring coroutine when leaving world map.
        /// </summary>
        public static void StopStateMonitoring()
        {
            if (stateMonitorCoroutine != null)
            {
                MelonCoroutines.Stop(stateMonitorCoroutine);
                stateMonitorCoroutine = null;
                lastKnownState = -1;
                MelonLogger.Msg("[MoveState] Stopped state monitoring coroutine");
            }
        }
    }
}
