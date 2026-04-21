using System;
using System.Collections.Generic;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using Il2CppLast.Management;
using static FFI_ScreenReader.Utils.ModTextTranslator;

using AbilityWindowController = Il2CppSerial.FF1.UI.KeyInput.AbilityWindowController;
using OwnedAbility = Il2CppLast.Data.User.OwnedAbility;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using AbilityLevelType = Il2CppLast.Defaine.Master.AbilityLevelType;
using PlayerCharacterParameter = Il2CppLast.Data.PlayerCharacterParameter;
using Condition = Il2CppLast.Data.Master.Condition;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// State tracker for magic menu with proper suppression pattern.
    /// </summary>
    public static class MagicMenuState
    {
        private static bool _isTargetSelectionActive = false;
        private static OwnedCharacterData _currentCharacter = null;
        private static OwnedAbility _lastAnnouncedAbility = null;

        /// <summary>
        /// The most recently announced ability in the spell list.
        /// Used by MagicDetailsAnnouncer for the I key.
        /// </summary>
        public static OwnedAbility LastAnnouncedAbility
        {
            get => _lastAnnouncedAbility;
            set => _lastAnnouncedAbility = value;
        }

        private const int STATE_NONE = 0;
        private const int STATE_COMMAND = 4;
        private const int OFFSET_STATE_MACHINE = IL2CppOffsets.MenuStateMachine.Magic;

        static MagicMenuState()
        {
            MenuStateRegistry.RegisterResetHandler(MenuStateRegistry.MAGIC_MENU, () =>
            {
                _isTargetSelectionActive = false;
                _currentCharacter = null;
                _lastAnnouncedAbility = null;
            });
        }

        public static bool IsSpellListActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.MAGIC_MENU);
        }

        public static bool IsTargetSelectionActive
        {
            get => _isTargetSelectionActive;
            set => _isTargetSelectionActive = value;
        }

        public static void OnSpellListFocused()
        {
            FFI_ScreenReader.Core.FFI_ScreenReaderMod.ClearOtherMenuStates("Magic");
            MenuStateRegistry.SetActive(MenuStateRegistry.MAGIC_MENU, true);
        }

        public static void OnSpellListUnfocused()
        {
            MenuStateRegistry.SetActive(MenuStateRegistry.MAGIC_MENU, false);
            _currentCharacter = null;
        }

        public static OwnedCharacterData CurrentCharacter
        {
            get => _currentCharacter;
            set => _currentCharacter = value;
        }

        public static bool ShouldSuppress()
        {
            // Target selection always suppresses — it uses AbilityUseContentListController,
            // not AbilityWindowController, so the state machine check below is irrelevant
            if (_isTargetSelectionActive)
                return true;

            if (!IsSpellListActive)
                return false;

            try
            {
                var abilityController = UnityEngine.Object.FindObjectOfType<AbilityWindowController>();
                if (abilityController != null)
                {
                    int currentState = StateMachineHelper.ReadState(abilityController.Pointer, OFFSET_STATE_MACHINE);

                    if (currentState == STATE_COMMAND)
                    {
                        ResetState();
                        return false;
                    }

                    if (currentState == STATE_NONE)
                    {
                        ResetState();
                        return false;
                    }
                }

                return true;
            }
            catch // Menu controller not in expected state
            {
                ResetState();
                return false;
            }
        }

        public static void ResetState()
        {
            MenuStateRegistry.SetActive(MenuStateRegistry.MAGIC_MENU, false);
            _isTargetSelectionActive = false;
            _currentCharacter = null;
            _lastAnnouncedAbility = null;
        }

        public static void OnTargetSelectionActive()
        {
            FFI_ScreenReader.Core.FFI_ScreenReaderMod.ClearOtherMenuStates("Magic");
            // SetActive(MAGIC_MENU,false) fires the reset handler registered in the static ctor, which
            // also clears _isTargetSelectionActive. Run it first, then set the flag so it sticks.
            MenuStateRegistry.SetActive(MenuStateRegistry.MAGIC_MENU, false);
            _isTargetSelectionActive = true;
        }

        public static void OnTargetSelectionClosed()
        {
            _isTargetSelectionActive = false;
        }

        // Fallback mapping for conditions with empty/missing MesIdName.
        // Values are English keys into ModTextTranslator (localized at call time via T()).
        private static readonly Dictionary<int, string> ConditionTypeFallbacks = new Dictionary<int, string>
        {
            { 4, "Critical" },        // Dying (low HP)
            { 5, "KO" },              // UnableFight
            { 6, "Silence" },
            { 7, "Sleep" },
            { 8, "Paralysis" },
            { 9, "Blind" },
            { 10, "Poison" },
            { 11, "Stone" },           // Mineralization
            { 12, "Confusion" },
            { 16, "Slow" },
            { 17, "Stop" },
            { 32, "Aging" },
            { 34, "Zombie" },
            { 204, "Venom" },
            { 404, "Doom" },
            { 405, "Gradual Petrify" }, // SlowlyMineralization
            { 406, "Curse" },
        };

        public static string GetConditionName(Condition condition)
        {
            if (condition == null)
                return null;

            try
            {
                string mesId = condition.MesIdName;
                int condType = condition.ConditionType;

                // Primary: localize via MesIdName.
                // Reject "$"-prefixed return — MessageManager echoes the raw key when unresolved.
                if (!string.IsNullOrEmpty(mesId) && mesId != "None")
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedName = messageManager.GetMessage(mesId, false);
                        if (!string.IsNullOrWhiteSpace(localizedName) && !localizedName.StartsWith("$"))
                            return localizedName;
                    }
                }

                if (ConditionTypeFallbacks.TryGetValue(condType, out string fallback))
                    return T(fallback);
            }
            catch { } // IL2CPP message resolution may fail

            return null;
        }

        public static string GetSpellName(OwnedAbility ability)
        {
            if (ability == null)
                return null;

            try
            {
                string mesId = ability.MesIdName;
                if (!string.IsNullOrEmpty(mesId))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedName = messageManager.GetMessage(mesId, false);
                        if (!string.IsNullOrWhiteSpace(localizedName))
                            return TextUtils.StripIconMarkup(localizedName);
                    }
                }
            }
            catch { } // IL2CPP message resolution may fail

            return null;
        }

        public static string GetSpellDescription(OwnedAbility ability)
        {
            if (ability == null)
                return null;

            try
            {
                string mesId = ability.MesIdDescription;
                if (!string.IsNullOrEmpty(mesId))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedDesc = messageManager.GetMessage(mesId, false);
                        if (!string.IsNullOrWhiteSpace(localizedDesc))
                            return TextUtils.StripIconMarkup(localizedDesc);
                    }
                }
            }
            catch { } // IL2CPP message resolution may fail

            return null;
        }

        public static int GetSpellLevel(OwnedAbility ability)
        {
            if (ability == null)
                return 0;

            try
            {
                var abilityData = ability.Ability;
                if (abilityData != null)
                    return abilityData.AbilityLv;
            }
            catch { } // IL2CPP ability data may be unavailable

            return 0;
        }

        public static (int current, int max) GetChargesForLevel(OwnedCharacterData character, int level)
        {
            if (character == null || level <= 0 || level > 8)
                return (0, 0);

            try
            {
                var param = character.Parameter as PlayerCharacterParameter;
                if (param == null)
                    return (0, 0);

                int current = 0;
                var currentList = param.CurrentMpCountList;
                if (currentList != null && currentList.ContainsKey(level))
                    current = currentList[level];

                int max = 0;
                try
                {
                    max = param.ConfirmedMaxMpCount((AbilityLevelType)level);
                }
                catch // Fall back to base MP list
                {
                    var baseMaxList = param.BaseMaxMpCountList;
                    if (baseMaxList != null && baseMaxList.ContainsKey(level))
                        max = baseMaxList[level];
                }

                return (current, max);
            }
            catch { } // IL2CPP parameter data may be unavailable

            return (0, 0);
        }
    }
}
