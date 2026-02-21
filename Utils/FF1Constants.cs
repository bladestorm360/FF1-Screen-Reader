namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Game-specific state machine values and constants for FF1.
    /// Derived from dump.cs analysis of FF1's UI controllers.
    /// </summary>
    public static class FF1Constants
    {
        /// <summary>
        /// Item menu state machine values (ItemWindowController).
        /// </summary>
        public static class ItemMenuStates
        {
            public const int STATE_NONE = 0;
            public const int STATE_COMMAND_SELECT = 1;    // Command bar: Use/Key Items/Sort
            public const int STATE_USE_SELECT = 2;        // Item list for Use
            public const int STATE_IMPORTANT_SELECT = 3;  // Key Items list
            public const int STATE_ORGANIZE_SELECT = 4;   // Sort list
            public const int STATE_TARGET_SELECT = 5;     // Target selection
        }

        /// <summary>
        /// Equipment menu state machine values (EquipmentWindowController).
        /// </summary>
        public static class EquipMenuStates
        {
            public const int STATE_NONE = 0;
            public const int STATE_COMMAND = 1;   // Command bar: Equip/Optimal/Remove All
            public const int STATE_INFO = 2;      // Equipment slot selection
            public const int STATE_SELECT = 3;    // Equipment item selection
        }

        /// <summary>
        /// Shop menu state machine values (ShopController).
        /// </summary>
        public static class ShopMenuStates
        {
            public const int STATE_NONE = 0;
            public const int STATE_SELECT_COMMAND = 1;            // Command bar: Buy/Sell/Equipment/Back
            public const int STATE_SELECT_PRODUCT = 2;            // Buy item list
            public const int STATE_SELECT_SELL_ITEM = 3;          // Sell item list
            public const int STATE_SELECT_ABILITY_TARGET = 4;     // Magic shop character select
            public const int STATE_SELECT_EQUIPMENT = 5;          // Equipment sub-menu
            public const int STATE_CONFIRMATION_BUY_ITEM = 6;     // Buy quantity confirmation
            public const int STATE_CONFIRMATION_SELL_ITEM = 7;    // Sell quantity confirmation
            public const int STATE_CONFIRMATION_FORGET_MAGIC = 8; // Magic forget confirmation
            public const int STATE_CONFIRMATION_BUY_MAGIC = 9;    // Magic buy confirmation
        }

        /// <summary>
        /// Magic menu state machine values (AbilityWindowController).
        /// </summary>
        public static class MagicMenuStates
        {
            public const int STATE_NONE = 0;
            public const int STATE_COMMAND = 4;
        }

        /// <summary>
        /// Battle start condition state values (BattleStartType).
        /// </summary>
        public static class BattleStartStates
        {
            public const int STATE_NON = -1;
            public const int STATE_NORMAL = 0;
            public const int STATE_PREEMPTIVE = 1;
            public const int STATE_BACK_ATTACK = 2;
            public const int STATE_ENEMY_PREEMPTIVE = 3;
            public const int STATE_ENEMY_SIDE_ATTACK = 4;
            public const int STATE_SIDE_ATTACK = 5;
        }

        /// <summary>
        /// Map transition state values (FieldMapProvisionInformation).
        /// </summary>
        public static class MapTransitionStates
        {
            public const int STATE_CHANGE_MAP = 1;
            public const int STATE_FIELD_READY = 2;
            public const int STATE_PLAYER = 3;
        }

        /// <summary>
        /// MoveState enum values (FieldPlayerConstants.MoveState).
        /// </summary>
        public static class MoveStates
        {
            public const int WALK = 0;
            public const int DASH = 1;
            public const int AIRSHIP = 2;
            public const int SHIP = 3;
            public const int LOW_FLYING = 4;
            public const int CANOE = 5;
            public const int GIMMICK = 6;
            public const int UNIQUE = 7;
        }

        /// <summary>
        /// TransportationType enum values (MapConstants.TransportationType).
        /// </summary>
        public static class TransportTypes
        {
            public const int PLAYER = 1;
            public const int SHIP = 2;
            public const int AIRSHIP = 3;       // Plane
            public const int CANOE = 5;         // Content slot in FF1
            public const int SUBMARINE = 6;
            public const int LOW_FLYING = 7;
            public const int SPECIAL_PLANE = 8;
            public const int YELLOW_CHOCOBO = 9;
            public const int BLACK_CHOCOBO = 10;
        }
    }
}
