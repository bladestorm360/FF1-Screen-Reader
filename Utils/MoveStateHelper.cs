using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using MelonLoader;
using FFI_ScreenReader.Core;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Helper for tracking and announcing player movement state (walking, ship, airship, canoe, etc.)
    /// FF1 vehicles: Ship, Canoe, Airship.
    /// </summary>
    public static class MoveStateHelper
    {
        // MoveState enum values (from FieldPlayerConstants.MoveState)
        public const int MOVE_STATE_WALK = 0;
        public const int MOVE_STATE_DUSH = 1;    // Dash
        public const int MOVE_STATE_AIRSHIP = 2;
        public const int MOVE_STATE_SHIP = 3;
        public const int MOVE_STATE_LOWFLYING = 4;
        public const int MOVE_STATE_CANOE = 5;   // FF1 has Canoe (uses Chocobo slot in enum)
        public const int MOVE_STATE_GIMMICK = 6;
        public const int MOVE_STATE_UNIQUE = 7;

        // TransportationType enum values (from MapConstants.TransportationType)
        private const int TRANSPORT_SHIP = 2;
        private const int TRANSPORT_PLANE = 3;       // Airship
        private const int TRANSPORT_CONTENT = 5;     // Canoe in FF1
        private const int TRANSPORT_SUBMARINE = 6;
        private const int TRANSPORT_LOWFLYING = 7;
        private const int TRANSPORT_SPECIALPLANE = 8;
        private const int TRANSPORT_YELLOWCHOCOBO = 9;
        private const int TRANSPORT_BLACKCHOCOBO = 10;

        // Cached state tracking (set by GetOn/GetOff patches)
        private static int cachedMoveState = MOVE_STATE_WALK;
        private static int cachedTransportationType = 0;
        private static int lastAnnouncedState = -1;

        /// <summary>
        /// Set vehicle state when boarding (called from GetOn patch).
        /// Maps TransportationType to MoveState.
        /// </summary>
        public static void SetVehicleState(int transportationType)
        {
            cachedTransportationType = transportationType;
            cachedMoveState = TransportTypeToMoveState(transportationType);
            MelonLogger.Msg($"[MoveState] SetVehicleState: transport={transportationType} -> moveState={cachedMoveState}");
        }

        /// <summary>
        /// Set on-foot state when disembarking (called from GetOff patch).
        /// </summary>
        public static void SetOnFoot()
        {
            cachedTransportationType = 0;
            cachedMoveState = MOVE_STATE_WALK;
            MelonLogger.Msg("[MoveState] SetOnFoot: now walking");
        }

        /// <summary>
        /// Convert TransportationType to MoveState for compatibility with existing code.
        /// FF1 specific mapping.
        /// </summary>
        private static int TransportTypeToMoveState(int transportationType)
        {
            switch (transportationType)
            {
                case TRANSPORT_SHIP: return MOVE_STATE_SHIP;
                case TRANSPORT_PLANE: return MOVE_STATE_AIRSHIP;
                case TRANSPORT_CONTENT: return MOVE_STATE_CANOE;      // FF1: Canoe uses Content slot
                case TRANSPORT_SUBMARINE: return MOVE_STATE_SHIP;     // Treat submarine like ship (if exists)
                case TRANSPORT_LOWFLYING: return MOVE_STATE_LOWFLYING;
                case TRANSPORT_SPECIALPLANE: return MOVE_STATE_AIRSHIP;
                case TRANSPORT_YELLOWCHOCOBO:
                case TRANSPORT_BLACKCHOCOBO: return MOVE_STATE_CANOE; // Map any chocobo to canoe (unlikely in FF1)
                default: return MOVE_STATE_WALK;
            }
        }

        /// <summary>
        /// Check if a state is a vehicle state (ship, canoe, airship)
        /// </summary>
        private static bool IsVehicleState(int state)
        {
            return state == MOVE_STATE_SHIP || state == MOVE_STATE_CANOE ||
                   state == MOVE_STATE_AIRSHIP || state == MOVE_STATE_LOWFLYING;
        }

        /// <summary>
        /// Announce movement state changes.
        /// Public so it can be called for backup state monitoring if needed.
        /// </summary>
        public static void AnnounceStateChange(int previousState, int newState)
        {
            string announcement = null;

            if (newState == MOVE_STATE_SHIP)
            {
                announcement = "On ship";
            }
            else if (newState == MOVE_STATE_CANOE)
            {
                announcement = "On canoe";
            }
            else if (newState == MOVE_STATE_AIRSHIP || newState == MOVE_STATE_LOWFLYING)
            {
                announcement = "On airship";
            }
            else if ((previousState == MOVE_STATE_SHIP || previousState == MOVE_STATE_CANOE ||
                      previousState == MOVE_STATE_AIRSHIP || previousState == MOVE_STATE_LOWFLYING) &&
                     (newState == MOVE_STATE_WALK || newState == MOVE_STATE_DUSH))
            {
                announcement = "On foot";
            }

            if (announcement != null)
            {
                lastAnnouncedState = newState;
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
        }

        /// <summary>
        /// Get current MoveState - uses cached state from GetOn/GetOff patches.
        /// </summary>
        public static int GetCurrentMoveState()
        {
            // Return cached state (set by GetOn/GetOff patches)
            return cachedMoveState;
        }

        /// <summary>
        /// Check if currently controlling ship
        /// </summary>
        public static bool IsControllingShip()
        {
            return GetCurrentMoveState() == MOVE_STATE_SHIP;
        }

        /// <summary>
        /// Check if currently on foot (walking or dashing)
        /// </summary>
        public static bool IsOnFoot()
        {
            int state = GetCurrentMoveState();
            return state == MOVE_STATE_WALK || state == MOVE_STATE_DUSH;
        }

        /// <summary>
        /// Check if currently in canoe
        /// </summary>
        public static bool IsInCanoe()
        {
            return GetCurrentMoveState() == MOVE_STATE_CANOE;
        }

        /// <summary>
        /// Check if currently controlling airship
        /// </summary>
        public static bool IsControllingAirship()
        {
            int state = GetCurrentMoveState();
            return state == MOVE_STATE_AIRSHIP || state == MOVE_STATE_LOWFLYING;
        }

        /// <summary>
        /// Get pathfinding scope multiplier based on current MoveState
        /// </summary>
        public static float GetPathfindingMultiplier()
        {
            int moveState = GetCurrentMoveState();
            float multiplier;

            switch (moveState)
            {
                case MOVE_STATE_WALK:
                case MOVE_STATE_DUSH:
                    multiplier = 1.0f;  // Baseline (on foot)
                    break;

                case MOVE_STATE_SHIP:
                    multiplier = 2.5f;  // 2.5x scope for ship
                    break;

                case MOVE_STATE_CANOE:
                    multiplier = 1.5f;  // Moderate increase for canoe
                    break;

                case MOVE_STATE_AIRSHIP:
                case MOVE_STATE_LOWFLYING:
                    multiplier = 1.0f;  // Airship uses different navigation system
                    break;

                default:
                    multiplier = 1.0f;  // Default to baseline
                    break;
            }

            return multiplier;
        }

        /// <summary>
        /// Get human-readable name for MoveState
        /// </summary>
        public static string GetMoveStateName(int moveState)
        {
            switch (moveState)
            {
                case MOVE_STATE_WALK: return "Walking";
                case MOVE_STATE_DUSH: return "Dashing";
                case MOVE_STATE_SHIP: return "Ship";
                case MOVE_STATE_AIRSHIP: return "Airship";
                case MOVE_STATE_LOWFLYING: return "Low Flying";
                case MOVE_STATE_CANOE: return "Canoe";
                case MOVE_STATE_GIMMICK: return "Gimmick";
                case MOVE_STATE_UNIQUE: return "Unique";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// Reset state (call on map transitions)
        /// </summary>
        public static void ResetState()
        {
            cachedMoveState = MOVE_STATE_WALK;
            cachedTransportationType = 0;
            lastAnnouncedState = -1;
        }
    }
}
