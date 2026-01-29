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

    }
}
