using System;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Centralized IL2CPP memory offsets for FF1.
    /// All offsets derived from dump.cs analysis.
    /// </summary>
    public static class IL2CppOffsets
    {
        /// <summary>
        /// Common state machine offsets (used by StateMachineHelper).
        /// </summary>
        public static class StateMachine
        {
            /// <summary>Offset to current state pointer within StateMachine object.</summary>
            public const int Current = 0x10;

            /// <summary>Offset to state tag (int) within State object.</summary>
            public const int StateTag = 0x10;
        }

        /// <summary>
        /// Menu controller state machine offsets.
        /// Each controller has stateMachine field at different offset.
        /// </summary>
        public static class MenuStateMachine
        {
            /// <summary>KeyInput.EquipmentWindowController.stateMachine</summary>
            public const int Equip = 0x60;

            /// <summary>KeyInput.ItemWindowController.stateMachine</summary>
            public const int Item = 0x70;

            /// <summary>Serial.FF1.UI.KeyInput.AbilityWindowController.stateMachine</summary>
            public const int Magic = 0x88;

            /// <summary>KeyInput.ShopController.stateMachine</summary>
            public const int Shop = 0x90;
        }

        /// <summary>
        /// Config menu offsets for tooltip/description reading.
        /// </summary>
        public static class ConfigMenu
        {
            /// <summary>KeyInput.ConfigActualDetailsControllerBase.descriptionText</summary>
            public const int DescriptionTextKeyInput = 0xA0;

            /// <summary>Touch.ConfigActualDetailsControllerBase.descriptionText</summary>
            public const int DescriptionTextTouch = 0x50;
        }

        /// <summary>
        /// Magic menu (AbilityContentListController) offsets.
        /// </summary>
        public static class MagicMenu
        {
            /// <summary>List&lt;BattleAbilityInfomationContentController&gt; contentList</summary>
            public const int ContentList = 0x30;

            /// <summary>List&lt;AbilityContentController&gt; slotContentList</summary>
            public const int SlotContentList = 0x38;

            /// <summary>Action&lt;int&gt; OnSelected</summary>
            public const int OnSelected = 0x40;

            /// <summary>Action&lt;int&gt; OnSelect</summary>
            public const int OnSelect = 0x48;

            /// <summary>Action OnCancel</summary>
            public const int OnCancel = 0x50;
        }

        /// <summary>
        /// Magic menu target selection (AbilityUseContentListController) offsets.
        /// </summary>
        public static class MagicTarget
        {
            /// <summary>List&lt;ItemTargetSelectContentController&gt; contentList</summary>
            public const int ContentList = 0x38;

            /// <summary>Cursor selectCursor</summary>
            public const int SelectCursor = 0x40;
        }

        /// <summary>
        /// AbilityWindowController (KeyInput) offsets for character data access.
        /// </summary>
        public static class AbilityWindow
        {
            /// <summary>AbilityCharaStatusController statusController</summary>
            public const int StatusController = 0x50;

            /// <summary>OwnedCharacterData targetData (on AbilityCharaStatusController)</summary>
            public const int TargetData = 0x48;
        }

        /// <summary>
        /// Shop menu offsets.
        /// </summary>
        public static class Shop
        {
            /// <summary>ShopTradeWindowController.selectedCount</summary>
            public const int SelectedCount = 0x3C;

            /// <summary>ShopTradeWindowController.view</summary>
            public const int TradeView = 0x30;

            /// <summary>ShopTradeWindowView.totarlPriceText</summary>
            public const int TotalPriceText = 0x70;

            /// <summary>ShopListItemContentController.shopListItemContentView</summary>
            public const int ShopListItemContentView = 0x30;

            /// <summary>ShopListItemContentView.priceText</summary>
            public const int PriceText = 0x18;
        }

        /// <summary>
        /// Shop magic target (spell slot selection) offsets.
        /// </summary>
        public static class ShopMagic
        {
            /// <summary>ShopGetMagicContentController.CharacterId</summary>
            public const int CharacterId = 0x18;

            /// <summary>ShopGetMagicContentController.SlotType</summary>
            public const int SlotType = 0x1C;

            /// <summary>ShopGetMagicContentController.ContentId</summary>
            public const int ContentId = 0x20;

            /// <summary>ShopGetMagicContentController.view</summary>
            public const int View = 0x30;

            /// <summary>ShopMagicTargetSelectController.isFoundEquipSlot</summary>
            public const int IsFoundEquipSlot = 0x70;
        }

        /// <summary>
        /// Battle magic menu (BattleAbilityInfomationControllerBase) offsets.
        /// </summary>
        public static class BattleMagic
        {
            /// <summary>List&lt;OwnedAbility&gt; dataList</summary>
            public const int DataList = 0x70;

            /// <summary>List&lt;BattleAbilityInfomationContentController&gt; contentList</summary>
            public const int ContentList = 0x78;

            /// <summary>BattlePlayerData selectedBattlePlayerData</summary>
            public const int SelectedPlayer = 0x30;
        }

        /// <summary>
        /// Battle item menu (BattleItemInfomationController - KeyInput) offsets.
        /// </summary>
        public static class BattleItem
        {
            /// <summary>List&lt;ItemListContentData&gt; displayDataList</summary>
            public const int DisplayDataList = 0xE0;

            /// <summary>List&lt;OwnedItemData&gt; dataList</summary>
            public const int DataList = 0x88;
        }

        /// <summary>
        /// Battle unit data offsets for accessing parameters in battle.
        /// </summary>
        public static class BattleUnit
        {
            /// <summary>BattleUnitData.BattleUnitDataInfo</summary>
            public const int BattleUnitDataInfo = 0x28;

            /// <summary>BattleUnitDataInfo.Parameter</summary>
            public const int Parameter = 0x10;
        }

        /// <summary>
        /// Status details (StatusDetailsController) offsets.
        /// </summary>
        public static class StatusDetails
        {
            /// <summary>StatusDetailsController.statusController</summary>
            public const int StatusController = 0x78;

            /// <summary>AbilityCharaStatusController.targetData</summary>
            public const int TargetData = 0x48;
        }

        /// <summary>
        /// Battle pause menu offsets (FF1-specific).
        /// NOTE: FF1 uses 0x98 for pauseController, FF3 uses 0x90.
        /// </summary>
        public static class BattlePause
        {
            /// <summary>BattleUIManager.pauseController (FF1)</summary>
            public const int PauseController = 0x98;

            /// <summary>BattlePauseController.isActivePauseMenu</summary>
            public const int IsActivePauseMenu = 0x71;
        }

        /// <summary>
        /// Battle result menu offsets.
        /// </summary>
        public static class BattleResult
        {
            /// <summary>ResultMenuController.targetData</summary>
            public const int TargetData = 0x60;
        }

        /// <summary>
        /// Message window (MessageWindowManager) offsets.
        /// </summary>
        public static class MessageWindow
        {
            /// <summary>List&lt;string&gt; messageList</summary>
            public const int MessageList = 0x88;

            /// <summary>List&lt;int&gt; newPageLineList</summary>
            public const int NewPageLineList = 0xA0;

            /// <summary>int messageLineIndex</summary>
            public const int MessageLineIndex = 0xB0;

            /// <summary>int currentPageNumber</summary>
            public const int CurrentPageNumber = 0xF8;

            /// <summary>Speaker value field</summary>
            public const int SpeakerValue = 0xA8;
        }

        /// <summary>
        /// Shop info view offsets.
        /// </summary>
        public static class ShopInfo
        {
            /// <summary>ShopInfoController.view</summary>
            public const int InfoView = 0x18;

            /// <summary>ShopInfoView.descriptionText</summary>
            public const int DescriptionText = 0x38;
        }

        /// <summary>
        /// Save/Load popup offsets.
        /// </summary>
        public static class SaveLoad
        {
            /// <summary>SavePopup.messageText</summary>
            public const int MessageText = 0x40;

            /// <summary>SavePopup.commandList</summary>
            public const int CommandList = 0x60;

            /// <summary>LoadGameWindowController.savePopup (title screen)</summary>
            public const int TitleLoadSavePopup = 0x58;

            /// <summary>LoadWindowController/SaveWindowController.savePopup (main menu)</summary>
            public const int MainMenuSavePopup = 0x28;

            /// <summary>InterruptionWindowController.savePopup (QuickSave)</summary>
            public const int InterruptionSavePopup = 0x38;
        }

        /// <summary>
        /// Popup dialog offsets.
        /// </summary>
        public static class Popup
        {
            /// <summary>IconTextView.nameText</summary>
            public const int IconTextViewNameText = 0x20;

            /// <summary>CommonCommand.text</summary>
            public const int CommonCommandText = 0x18;

            // CommonPopup (KeyInput)
            /// <summary>CommonPopup.title (IconTextView)</summary>
            public const int CommonTitle = 0x38;

            /// <summary>CommonPopup.message (Text)</summary>
            public const int CommonMessage = 0x40;

            /// <summary>CommonPopup.selectCursor (Cursor)</summary>
            public const int CommonSelectCursor = 0x68;

            /// <summary>CommonPopup.commandList (List&lt;CommonCommand&gt;)</summary>
            public const int CommonCommandList = 0x70;

            // ChangeMagicStonePopup
            /// <summary>ChangeMagicStonePopup.nameText (Text)</summary>
            public const int MagicStoneName = 0x28;

            /// <summary>ChangeMagicStonePopup.descriptionText (Text)</summary>
            public const int MagicStoneDesc = 0x30;

            /// <summary>ChangeMagicStonePopup.commandList</summary>
            public const int MagicStoneCommandList = 0x58;

            // GameOverSelectPopup
            /// <summary>GameOverSelectPopup.selectCursor (Cursor)</summary>
            public const int GameOverSelectCursor = 0x38;

            /// <summary>GameOverSelectPopup.commandList</summary>
            public const int GameOverCommandList = 0x40;

            // InfomationPopup
            /// <summary>InfomationPopup.title (IconTextView)</summary>
            public const int InfoTitle = 0x28;

            /// <summary>InfomationPopup.message (Text)</summary>
            public const int InfoMessage = 0x30;

            // InputPopup
            /// <summary>InputPopup.descriptionText (Text)</summary>
            public const int InputDesc = 0x30;

            // ChangeNamePopup
            /// <summary>ChangeNamePopup.descriptionText (Text)</summary>
            public const int ChangeNameDesc = 0x30;

            // GameOverLoadPopup
            /// <summary>GameOverLoadPopup.titleText (Text)</summary>
            public const int GameOverLoadTitle = 0x38;

            /// <summary>GameOverLoadPopup.messageText (Text)</summary>
            public const int GameOverLoadMessage = 0x40;

            /// <summary>GameOverLoadPopup.selectCursor (Cursor)</summary>
            public const int GameOverLoadSelectCursor = 0x58;

            /// <summary>GameOverLoadPopup.commandList</summary>
            public const int GameOverLoadCommandList = 0x60;

            // GameOverPopupController chain
            /// <summary>GameOverPopupController.view (GameOverPopupView)</summary>
            public const int GameOverPopupCtrlView = 0x30;

            /// <summary>GameOverPopupView.loadPopup (GameOverLoadPopup)</summary>
            public const int GameOverPopupViewLoadPopup = 0x18;

            // TitleWindowController
            /// <summary>TitleWindowController.view (KeyInput)</summary>
            public const int TitleViewKeyInput = 0x48;

            /// <summary>TitleWindowController.view (Touch)</summary>
            public const int TitleViewTouch = 0x50;

            /// <summary>TitleWindowView.startText (Text)</summary>
            public const int TitleViewStartText = 0x30;
        }

        /// <summary>
        /// UserDataManager / ConfigSaveData offsets for reading game settings.
        /// </summary>
        public static class UserData
        {
            /// <summary>UserDataManager.configSaveData</summary>
            public const int ConfigSaveData = 0xB8;

            /// <summary>ConfigSaveData.isAutoDash (int: 0=off, 1=on)</summary>
            public const int IsAutoDash = 0x40;
        }

    }
}

