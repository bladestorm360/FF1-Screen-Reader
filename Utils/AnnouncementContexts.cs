namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Centralized announcement deduplication context strings.
    /// Each context represents a distinct UI element or state that tracks
    /// what was last announced to avoid repeating the same text.
    /// </summary>
    public static class AnnouncementContexts
    {
        // Field
        public const string FIELD_CHECK = "FieldContext";

        // Battle command/target selection (BattlePatches.cs state classes)
        public const string BATTLE_COMMAND = "BattleCommand.Select";
        public const string BATTLE_TARGET = "BattleTarget.Select";
        public const string BATTLE_ITEM_SELECT = "BattleItem.Select";
        public const string BATTLE_MAGIC_SELECT = "BattleMagic.Select";

        // Battle action/status (BattleMessagePatches, BattleCommandPatches)
        public const string BATTLE_ACTION = "BattleAction";
        public const string BATTLE_STATUS = "BattleStatus";
        public const string BATTLE_CMD_COMMAND = "BattleCmd.Command";
        public const string BATTLE_CMD_PLAYER = "BattleCmd.Player";
        public const string BATTLE_CMD_ENEMY = "BattleCmd.Enemy";

        // Battle item/magic menus (BattleItemPatches, BattleMagicPatches)
        public const string BATTLE_ITEM = "BattleItem";
        public const string BATTLE_MAGIC = "BattleMagic";

        // Menu patches
        public const string EQUIP_SELECT = "Equip.Select";
        public const string ITEM_SELECT = "Item.Select";
        public const string STATUS_SELECT = "Status.Select";
        public const string MAGIC_TARGET = "MagicTarget.Select";

        // Config menu
        public const string CONFIG_TEXT = "Config.Text";
        public const string CONFIG_SETTING = "Config.Setting";
        public const string CONFIG_ARROW = "Config.Arrow";
        public const string CONFIG_SLIDER = "Config.Slider";
        public const string CONFIG_TOUCH_ARROW = "Config.TouchArrow";
        public const string CONFIG_TOUCH_SLIDER = "Config.TouchSlider";

        // New game
        public const string NEW_GAME_SLOT = "NewGame.Slot";
        public const string NEW_GAME_NAME = "NewGame.Name";
        public const string NEW_GAME_AUTO_INDEX = "NewGame.AutoIndex";
        public const string NEW_GAME_FIELD = "NewGame.Field";

        // Popups
        public const string POPUP_BUTTON = "Popup.Button";
        public const string POPUP_GAMEOVER_BUTTON = "Popup.GameOverButton";
        public const string POPUP_GAMEOVER_LOAD_BUTTON = "Popup.GameOverLoadButton";

        // Job selection
        public const string JOB_SELECT_INDEX = "JobSelect.Index";
        public const string JOB_SELECT_NAME = "JobSelect.Name";

        // Message windows
        public const string MESSAGE_SPEAKER = "Message.Speaker";
        public const string MESSAGE_LINE = "Message.Line";

        // Shop
        public const string SHOP_ITEM = "Shop.Item";
        public const string SHOP_COMMAND = "Shop.Command";
        public const string SHOP_QUANTITY = "Shop.Quantity";
        public const string SHOP_SLOT = "Shop.Slot";
    }
}
