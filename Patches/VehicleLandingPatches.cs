using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Utils;
using FFI_ScreenReader.Core;

// FF1 types
using Il2CppLast.Map;
using Il2CppLast.Entity.Field;
using MapUIManager = Il2CppLast.Map.MapUIManager;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Announces when player enters a zone where vehicle can land.
    /// Patches MapUIManager.SwitchLandable which is called by the game
    /// when the landing state changes based on terrain under the vehicle.
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
    /// Patches FieldPlayer.GetOn/GetOff to announce when entering/exiting vehicles.
    /// FF1 vehicles: Ship, Canoe, Airship.
    /// Uses manual Harmony patching for IL2CPP compatibility.
    /// </summary>
    public static class MovementSpeechPatches
    {
        private static bool isPatched = false;

        // TransportationType enum values (from MapConstants.TransportationType in dump.cs)
        private const int TRANSPORT_NONE = 0;
        private const int TRANSPORT_PLAYER = 1;
        private const int TRANSPORT_SHIP = 2;
        private const int TRANSPORT_PLANE = 3;       // Airship
        private const int TRANSPORT_SYMBOL = 4;
        private const int TRANSPORT_CONTENT = 5;     // Likely Canoe in FF1
        private const int TRANSPORT_SUBMARINE = 6;
        private const int TRANSPORT_LOWFLYING = 7;
        private const int TRANSPORT_SPECIALPLANE = 8;
        private const int TRANSPORT_YELLOWCHOCOBO = 9;
        private const int TRANSPORT_BLACKCHOCOBO = 10;

        /// <summary>
        /// Apply manual Harmony patches for vehicle boarding/disembarking.
        /// Called from FFI_ScreenReaderMod initialization.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchGetOn(harmony);
                TryPatchGetOff(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MoveState] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch GetOn - called when player boards a vehicle.
        /// FF1 Signature: public void GetOn(int typeId, bool isBackground = False)
        /// </summary>
        private static void TryPatchGetOn(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type fieldPlayerType = typeof(FieldPlayer);
                MethodInfo targetMethod = null;

                foreach (var method in fieldPlayerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name == "GetOn")
                    {
                        var parameters = method.GetParameters();
                        // FF1: GetOn(int typeId, bool isBackground = False) - 2 params
                        if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(int))
                        {
                            MelonLogger.Msg($"[MoveState] Found GetOn with {parameters.Length} params");
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(MovementSpeechPatches).GetMethod(nameof(GetOn_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[MoveState] Patched GetOn successfully");
                }
                else
                {
                    MelonLogger.Warning("[MoveState] Could not find GetOn method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MoveState] Error patching GetOn: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch GetOff - called when player disembarks a vehicle.
        /// FF1 Signature: public void GetOff(int typeId, int layer = -1)
        /// </summary>
        private static void TryPatchGetOff(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type fieldPlayerType = typeof(FieldPlayer);
                MethodInfo targetMethod = null;

                foreach (var method in fieldPlayerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name == "GetOff")
                    {
                        var parameters = method.GetParameters();
                        // FF1: GetOff(int typeId, int layer = -1) - 2 params
                        if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(int))
                        {
                            MelonLogger.Msg($"[MoveState] Found GetOff with {parameters.Length} params");
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(MovementSpeechPatches).GetMethod(nameof(GetOff_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[MoveState] Patched GetOff successfully");
                }
                else
                {
                    MelonLogger.Warning("[MoveState] Could not find GetOff method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MoveState] Error patching GetOff: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for GetOn - announces vehicle boarding AND disembarking.
        /// FF1 uses GetOn for both: GetOn(vehicleType) to board, GetOn(1/Player) to disembark.
        /// </summary>
        public static void GetOn_Postfix(int typeId)
        {
            try
            {
                MelonLogger.Msg($"[MoveState] GetOn called with typeId={typeId}");

                // typeId=1 (TRANSPORT_PLAYER) means returning to walking
                // FF1 uses this instead of GetOff when disembarking
                if (typeId == TRANSPORT_PLAYER)
                {
                    // Only announce if we were previously in a vehicle
                    if (!MoveStateHelper.IsOnFoot())
                    {
                        MoveStateHelper.SetOnFoot();
                        FFI_ScreenReaderMod.SpeakText("On foot", interrupt: true);
                        MelonLogger.Msg("[MoveState] Announced: On foot (disembarked via GetOn)");
                    }
                    return;
                }

                string vehicleName = GetTransportationName(typeId);
                if (!string.IsNullOrEmpty(vehicleName))
                {
                    string announcement = $"On {vehicleName}";
                    MoveStateHelper.SetVehicleState(typeId);
                    FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                    MelonLogger.Msg($"[MoveState] Announced: {announcement}");
                }
                else
                {
                    MelonLogger.Msg($"[MoveState] Unknown vehicle typeId={typeId}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MoveState] Error in GetOn patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for GetOff - announces vehicle disembarking.
        /// </summary>
        public static void GetOff_Postfix(int typeId)
        {
            try
            {
                MelonLogger.Msg($"[MoveState] GetOff called with typeId={typeId}");

                string vehicleName = GetTransportationName(typeId);
                MoveStateHelper.SetOnFoot();

                // Only announce "On foot" if we were on a known vehicle
                if (!string.IsNullOrEmpty(vehicleName))
                {
                    FFI_ScreenReaderMod.SpeakText("On foot", interrupt: true);
                    MelonLogger.Msg("[MoveState] Announced: On foot");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MoveState] Error in GetOff patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Get human-readable name for TransportationType.
        /// FF1 vehicles: Ship, Canoe, Airship.
        /// </summary>
        private static string GetTransportationName(int typeId)
        {
            switch (typeId)
            {
                case TRANSPORT_SHIP: return "ship";
                case TRANSPORT_PLANE: return "airship";
                case TRANSPORT_CONTENT: return "canoe";           // FF1: Canoe uses Content slot
                case TRANSPORT_SUBMARINE: return "submarine";     // May not exist in FF1, but included for completeness
                case TRANSPORT_LOWFLYING: return "airship";       // Low flying mode of airship
                case TRANSPORT_SPECIALPLANE: return "airship";    // Special airship variant
                case TRANSPORT_YELLOWCHOCOBO: return "chocobo";   // May not exist in FF1
                case TRANSPORT_BLACKCHOCOBO: return "chocobo";    // May not exist in FF1
                default: return null;
            }
        }

        /// <summary>
        /// Reset state tracking (call on map transitions)
        /// </summary>
        public static void ResetState()
        {
            MoveStateHelper.ResetState();
        }
    }
}
