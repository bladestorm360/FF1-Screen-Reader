using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using Il2CppLast.Management;
using Il2CppInterop.Runtime;

// Type aliases for IL2CPP types - FF1 specific
using AbilityCommandController = Il2CppSerial.FF1.UI.KeyInput.AbilityCommandController;
using AbilityContentListController = Il2CppSerial.FF1.UI.KeyInput.AbilityContentListController;
using AbilityWindowController = Il2CppSerial.FF1.UI.KeyInput.AbilityWindowController;
using AbilityCharaStatusController = Il2CppSerial.FF1.UI.KeyInput.AbilityCharaStatusController;
using AbilityUseContentListController = Il2CppSerial.FF1.UI.KeyInput.AbilityUseContentListController;
using BattleAbilityInfomationContentController = Il2CppLast.UI.KeyInput.BattleAbilityInfomationContentController;
using ItemTargetSelectContentController = Il2CppLast.UI.KeyInput.ItemTargetSelectContentController;
using OwnedAbility = Il2CppLast.Data.User.OwnedAbility;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using AbilityCommandId = Il2CppLast.Defaine.UI.AbilityCommandId;
using AbilityType = Il2CppLast.Defaine.Master.AbilityType;
using AbilityLevelType = Il2CppLast.Defaine.Master.AbilityLevelType;
using PlayerCharacterParameter = Il2CppLast.Data.PlayerCharacterParameter;
using Condition = Il2CppLast.Data.Master.Condition;
using GameCursor = Il2CppLast.UI.Cursor;
using WithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;
using OwnedItemData = Il2CppLast.Data.User.OwnedItemData;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// State tracker for magic menu with proper suppression pattern.
    /// </summary>
    public static class MagicMenuState
    {
        // Track if spell list has focus (SetFocus(true) was called)
        private static bool _isSpellListFocused = false;

        // Track if target selection is active
        private static bool _isTargetSelectionActive = false;

        // Track last announced spell ID to prevent duplicates
        private static int lastSpellId = -1;

        // Context key for target announcements
        private const string TARGET_CONTEXT = "MagicTarget.Select";

        // Cache the current character for charge lookup
        private static OwnedCharacterData _currentCharacter = null;

        // AbilityWindowController.State enum values (from dump.cs)
        private const int STATE_NONE = 0;
        private const int STATE_COMMAND = 4;

        // State machine offset - use centralized offsets
        private const int OFFSET_STATE_MACHINE = IL2CppOffsets.MenuStateMachine.Magic;

        /// <summary>
        /// True when spell list has focus (SetFocus(true) was called).
        /// Only suppresses GenericCursor when spell list is actually focused.
        /// </summary>
        public static bool IsSpellListActive
        {
            get => _isSpellListFocused;
        }

        /// <summary>
        /// True when target selection is active (after choosing a spell to use).
        /// </summary>
        public static bool IsTargetSelectionActive
        {
            get => _isTargetSelectionActive;
            set => _isTargetSelectionActive = value;
        }

        /// <summary>
        /// Called when SetFocus(true) is received - spell list gained focus.
        /// </summary>
        public static void OnSpellListFocused()
        {
            // Clear other menu states to prevent conflicts
            FFI_ScreenReader.Core.FFI_ScreenReaderMod.ClearOtherMenuStates("Magic");
            _isSpellListFocused = true;
            lastSpellId = -1; // Reset to announce first spell
        }

        /// <summary>
        /// Called when SetFocus(false) is received - spell list lost focus.
        /// </summary>
        public static void OnSpellListUnfocused()
        {
            _isSpellListFocused = false;
            lastSpellId = -1;
            _currentCharacter = null;
        }

        /// <summary>
        /// The current character for charge lookups.
        /// </summary>
        public static OwnedCharacterData CurrentCharacter
        {
            get => _currentCharacter;
            set => _currentCharacter = value;
        }

        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// Uses state machine check like EquipMenuState - if in STATE_COMMAND, don't suppress.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!_isSpellListFocused && !_isTargetSelectionActive)
                return false;

            try
            {
                // Check state machine - if in COMMAND state, don't suppress (let MenuTextDiscovery handle)
                var abilityController = UnityEngine.Object.FindObjectOfType<AbilityWindowController>();
                if (abilityController != null)
                {
                    int currentState = StateMachineHelper.ReadState(abilityController.Pointer, OFFSET_STATE_MACHINE);

                    // STATE_COMMAND means we're in command bar (Use/Forget) - let MenuTextDiscovery handle
                    if (currentState == STATE_COMMAND)
                    {
                        MelonLogger.Msg("[Magic Menu] STATE_COMMAND detected, clearing and not suppressing");
                        ResetState();
                        return false;
                    }

                    // STATE_NONE means menu closing
                    if (currentState == STATE_NONE)
                    {
                        MelonLogger.Msg("[Magic Menu] STATE_NONE detected, clearing");
                        ResetState();
                        return false;
                    }
                }

                // Spell list or target selection is active - suppress
                return true;
            }
            catch
            {
                ResetState();
                return false;
            }
        }

        /// <summary>
        /// Checks if spell should be announced (changed from last).
        /// </summary>
        public static bool ShouldAnnounceSpell(int spellId)
        {
            if (spellId == lastSpellId)
                return false;
            lastSpellId = spellId;
            return true;
        }

        /// <summary>
        /// Resets all tracking state.
        /// </summary>
        public static void ResetState()
        {
            _isSpellListFocused = false;
            _isTargetSelectionActive = false;
            lastSpellId = -1;
            AnnouncementDeduplicator.Reset(TARGET_CONTEXT);
            _currentCharacter = null;
        }

        /// <summary>
        /// Called when target selection becomes active.
        /// </summary>
        public static void OnTargetSelectionActive()
        {
            FFI_ScreenReader.Core.FFI_ScreenReaderMod.ClearOtherMenuStates("Magic");
            _isTargetSelectionActive = true;
            _isSpellListFocused = false; // Clear spell list state when entering target selection
            AnnouncementDeduplicator.Reset(TARGET_CONTEXT);
        }

        /// <summary>
        /// Called when target selection is closed.
        /// </summary>
        public static void OnTargetSelectionClosed()
        {
            _isTargetSelectionActive = false;
            AnnouncementDeduplicator.Reset(TARGET_CONTEXT);
        }

        /// <summary>
        /// Checks if the target announcement should be made (string-only deduplication).
        /// </summary>
        public static bool ShouldAnnounceTarget(string announcement) => AnnouncementDeduplicator.ShouldAnnounce(TARGET_CONTEXT, announcement);

        /// <summary>
        /// Gets a localized condition/status effect name from a Condition object.
        /// </summary>
        public static string GetConditionName(Condition condition)
        {
            if (condition == null)
                return null;

            try
            {
                string mesId = condition.MesIdName;
                if (!string.IsNullOrEmpty(mesId))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedName = messageManager.GetMessage(mesId, false);
                        if (!string.IsNullOrWhiteSpace(localizedName))
                            return localizedName;
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Gets localized spell name from OwnedAbility.
        /// </summary>
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
            catch { }

            return null;
        }

        /// <summary>
        /// Gets localized spell description from OwnedAbility.
        /// </summary>
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
            catch { }

            return null;
        }

        /// <summary>
        /// Gets spell level (1-8) from OwnedAbility.
        /// </summary>
        public static int GetSpellLevel(OwnedAbility ability)
        {
            if (ability == null)
                return 0;

            try
            {
                var abilityData = ability.Ability;
                if (abilityData != null)
                {
                    return abilityData.AbilityLv;
                }
            }
            catch { }

            return 0;
        }

        /// <summary>
        /// Gets current and max charges for a given spell level.
        /// FF1 uses per-level charges shared across all magic types.
        /// </summary>
        public static (int current, int max) GetChargesForLevel(OwnedCharacterData character, int level)
        {
            if (character == null || level <= 0 || level > 8)
                return (0, 0);

            try
            {
                var param = character.Parameter as PlayerCharacterParameter;
                if (param == null)
                    return (0, 0);

                // Get current charges from dictionary
                int current = 0;
                var currentList = param.CurrentMpCountList;
                if (currentList != null && currentList.ContainsKey(level))
                {
                    current = currentList[level];
                }

                // Get max charges using the method that includes bonuses
                int max = 0;
                try
                {
                    max = param.ConfirmedMaxMpCount((AbilityLevelType)level);
                }
                catch
                {
                    // Fallback to base max if ConfirmedMaxMpCount fails
                    var baseMaxList = param.BaseMaxMpCountList;
                    if (baseMaxList != null && baseMaxList.ContainsKey(level))
                    {
                        max = baseMaxList[level];
                    }
                }

                return (current, max);
            }
            catch { }

            return (0, 0);
        }
    }

    /// <summary>
    /// Patches for magic menu using manual Harmony patching.
    /// Uses SetCursor to detect navigation, then finds focused content by iterating.
    /// </summary>
    public static class MagicMenuPatches
    {
        private static bool isPatched = false;

        // Memory offsets - use centralized offsets
        private const int OFFSET_CONTENT_LIST = IL2CppOffsets.MagicMenu.ContentList;
        private const int OFFSET_SLOT_CONTENT_LIST = IL2CppOffsets.MagicMenu.SlotContentList;
        private const int OFFSET_ON_SELECTED = IL2CppOffsets.MagicMenu.OnSelected;
        private const int OFFSET_ON_SELECT = IL2CppOffsets.MagicMenu.OnSelect;
        private const int OFFSET_ON_CANCEL = IL2CppOffsets.MagicMenu.OnCancel;

        // State constants moved to MagicMenuState class

        /// <summary>
        /// Apply manual Harmony patches for magic menu.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                // Patch UpdateController to track when spell list is actively handling input
                TryPatchUpdateController(harmony);

                // Patch SetCursor for navigation detection (only announces when focused)
                TryPatchSetCursor(harmony);

                // Patch window controller to clear flag when transitioning to command menu
                TryPatchWindowController(harmony);

                // Patch SetActive to clear state when menu closes
                TryPatchSetActive(harmony);

                // Target selection patches (AbilityUseContentListController)
                TryPatchTargetSetCursor(harmony);
                TryPatchTargetShow(harmony);
                TryPatchTargetClose(harmony);

                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Magic Menu] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches AbilityContentListController.UpdateController to track when spell list is active.
        /// Called when spell list starts handling input.
        /// FF1 Signature: UpdateController() - no parameters
        /// </summary>
        private static void TryPatchUpdateController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityContentListController);

                // FF1: UpdateController() has no parameters
                var updateControllerMethod = controllerType.GetMethod("UpdateController",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);

                if (updateControllerMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(UpdateController_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateControllerMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched UpdateController (parameterless)");
                }
                else
                {
                    MelonLogger.Warning("[Magic Menu] UpdateController method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch UpdateController: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches AbilityContentListController.SetCursor - called during navigation.
        /// FF1 Signature: SetCursor(int index, Cursor targetCursor)
        /// </summary>
        private static void TryPatchSetCursor(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityContentListController);

                // FF1: SetCursor(int index, Cursor targetCursor)
                MethodInfo setCursorMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "SetCursor")
                    {
                        var parameters = method.GetParameters();
                        // FF1 signature: (int, Cursor)
                        if (parameters.Length == 2 &&
                            parameters[0].ParameterType == typeof(int) &&
                            parameters[1].ParameterType.Name == "Cursor")
                        {
                            setCursorMethod = method;
                            MelonLogger.Msg($"[Magic Menu] Found SetCursor(int, Cursor)");
                            break;
                        }
                    }
                }

                if (setCursorMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(SetCursor_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setCursorMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched SetCursor(int, Cursor)");
                }
                else
                {
                    MelonLogger.Warning("[Magic Menu] SetCursor(int, Cursor) method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch SetCursor: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches AbilityWindowController.SetNextState to detect state transitions.
        /// When transitioning to Command state, clear the spell list flag.
        /// </summary>
        private static void TryPatchWindowController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityWindowController);

                // Find SetNextState method (private method that takes State enum)
                MethodInfo setNextStateMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "SetNextState")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 1)
                        {
                            setNextStateMethod = method;
                            break;
                        }
                    }
                }

                if (setNextStateMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(SetNextState_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setNextStateMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched SetNextState");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch SetNextState: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches AbilityWindowController.SetActive to clear state when menu closes.
        /// This ensures the active state flag is properly reset when backing out to main menu.
        /// </summary>
        private static void TryPatchSetActive(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityWindowController);
                MethodInfo setActiveMethod = controllerType.GetMethod("SetActive",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new Type[] { typeof(bool) },
                    null);

                if (setActiveMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(SetActive_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setActiveMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched SetActive");
                }
                else
                {
                    MelonLogger.Warning("[Magic Menu] SetActive method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch SetActive: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for AbilityWindowController.SetActive - clears state when menu closes.
        /// </summary>
        public static void SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                MelonLogger.Msg("[Magic Menu] SetActive(false) - clearing state");
                MagicMenuState.ResetState();
            }
        }

        /// <summary>
        /// Postfix for AbilityWindowController.SetNextState - detects state transitions.
        /// Clears state when menu closes (None = 0).
        /// </summary>
        public static void SetNextState_Postfix(object __instance, int state)
        {
            try
            {
                // Menu closing - transition to None state (0)
                if (state == 0)
                {
                    MelonLogger.Msg("[Magic Menu] SetNextState(None) - clearing all state");
                    MagicMenuState.ResetState();
                }
            }
            catch { }
        }

        // Offsets - use centralized offsets
        private const int OFFSET_STATUS_CONTROLLER = IL2CppOffsets.AbilityWindow.StatusController;
        private const int OFFSET_TARGET_DATA = IL2CppOffsets.AbilityWindow.TargetData;

        /// <summary>
        /// Postfix for UpdateController - called when spell list is actively handling input.
        /// This fires every frame while the spell list is active, so we use it to set the active flag.
        /// FF1: No targetCharacterData in this controller - get character from AbilityCharaStatusController.
        /// </summary>
        public static void UpdateController_Postfix(object __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var controller = __instance as AbilityContentListController;
                if (controller == null || !controller.gameObject.activeInHierarchy)
                    return;

                // Spell list is actively handling input - enable spell reading
                if (!MagicMenuState.IsSpellListActive)
                {
                    MelonLogger.Msg("[Magic Menu] UpdateController - activating spell list state");
                    MagicMenuState.OnSpellListFocused();
                }

                // Get current character from AbilityCharaStatusController if available
                if (MagicMenuState.CurrentCharacter == null)
                {
                    try
                    {
                        var windowController = UnityEngine.Object.FindObjectOfType<AbilityWindowController>();
                        if (windowController != null)
                        {
                            IntPtr windowPtr = windowController.Pointer;
                            if (windowPtr != IntPtr.Zero)
                            {
                                unsafe
                                {
                                    // Get statusController at offset 0x50 (KeyInput version)
                                    IntPtr statusControllerPtr = *(IntPtr*)((byte*)windowPtr.ToPointer() + OFFSET_STATUS_CONTROLLER);
                                    if (statusControllerPtr != IntPtr.Zero)
                                    {
                                        // Get targetData at offset 0x48 from AbilityCharaStatusController
                                        IntPtr charPtr = *(IntPtr*)((byte*)statusControllerPtr.ToPointer() + OFFSET_TARGET_DATA);
                                        if (charPtr != IntPtr.Zero)
                                        {
                                            MagicMenuState.CurrentCharacter = new OwnedCharacterData(charPtr);
                                            MelonLogger.Msg($"[Magic Menu] Got character: {MagicMenuState.CurrentCharacter.Name}");
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[Magic Menu] Error getting character: {ex.Message}");
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Postfix for SetCursor - fires during navigation.
        /// FF1 Signature: SetCursor(int index, Cursor targetCursor)
        /// Uses index directly from the first parameter.
        /// </summary>
        public static void SetCursor_Postfix(object __instance, int index, GameCursor targetCursor)
        {
            try
            {
                // Only process if spell list is focused
                if (!MagicMenuState.IsSpellListActive)
                    return;

                if (__instance == null)
                    return;

                var controller = __instance as AbilityContentListController;
                if (controller == null || !controller.gameObject.activeInHierarchy)
                    return;

                MelonLogger.Msg($"[Magic Menu] SetCursor called, index: {index}");

                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return;

                // FF1 magic menu only has Use/Remove mode - no Learn mode in this controller
                // Read spells from contentList at offset 0x30
                AnnounceSpellAtIndex(controllerPtr, index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in SetCursor_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces spell at the given index from contentList (Use/Remove mode).
        /// FF1: contentList is at offset 0x30
        /// </summary>
        private static void AnnounceSpellAtIndex(IntPtr controllerPtr, int index)
        {
            try
            {
                // Read contentList pointer at offset 0x30 (FF1)
                IntPtr contentListPtr;
                unsafe
                {
                    contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_CONTENT_LIST);
                }

                if (contentListPtr == IntPtr.Zero)
                {
                    MelonLogger.Msg("[Magic Menu] contentList is null");
                    return;
                }

                var contentList = new Il2CppSystem.Collections.Generic.List<BattleAbilityInfomationContentController>(contentListPtr);

                if (index < 0 || index >= contentList.Count)
                {
                    MelonLogger.Msg($"[Magic Menu] Index {index} out of range (count={contentList.Count})");
                    return;
                }

                var contentController = contentList[index];
                if (contentController == null)
                {
                    // Empty slot
                    AnnounceEmpty();
                    return;
                }

                var ability = contentController.Data;
                if (ability == null)
                {
                    // Empty slot
                    AnnounceEmpty();
                    return;
                }

                AnnounceSpell(ability);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in AnnounceSpellAtIndex: {ex.Message}");
            }
        }

        // Note: FF1 doesn't have Learn mode in AbilityContentListController
        // Spell tomes are handled through a different mechanism

        /// <summary>
        /// Announces "Empty" for empty spell slots.
        /// </summary>
        private static void AnnounceEmpty()
        {
            if (MagicMenuState.ShouldAnnounceSpell(-1)) // -1 as ID for empty
            {
                MelonLogger.Msg("[Magic Menu] Announcing: Empty");
                FFI_ScreenReaderMod.SpeakText("Empty", interrupt: true);
            }
        }

        /// <summary>
        /// Announces a spell with name, level, charges, and description.
        /// Format: "Spell Name LV(x): MP: current/max: Description"
        /// </summary>
        private static void AnnounceSpell(OwnedAbility ability)
        {
            try
            {
                // Get spell ID for deduplication
                int spellId = 0;
                int spellLevel = 0;
                try
                {
                    var abilityData = ability.Ability;
                    if (abilityData != null)
                    {
                        spellId = abilityData.Id;
                        spellLevel = abilityData.AbilityLv;
                    }
                }
                catch
                {
                    return;
                }

                // Check if spell changed (ID-based deduplication)
                if (!MagicMenuState.ShouldAnnounceSpell(spellId))
                    return;

                // Get spell name
                string spellName = MagicMenuState.GetSpellName(ability);
                if (string.IsNullOrEmpty(spellName))
                    return;

                // Build announcement: "Spell Name LV(X): MP: current/max: Description"
                string announcement = spellName;

                // Add level prefix and charges if we have character data
                if (spellLevel > 0)
                {
                    announcement += $" LV{spellLevel}";

                    if (MagicMenuState.CurrentCharacter != null)
                    {
                        var (current, max) = MagicMenuState.GetChargesForLevel(MagicMenuState.CurrentCharacter, spellLevel);
                        if (max > 0)
                        {
                            announcement += $": MP: {current}/{max}";
                        }
                        else
                        {
                            MelonLogger.Msg($"[Magic Menu] No charges found for level {spellLevel}");
                        }
                    }
                    else
                    {
                        MelonLogger.Msg("[Magic Menu] CurrentCharacter is null, can't get charges");
                    }
                }

                // Add description
                string description = MagicMenuState.GetSpellDescription(ability);
                if (!string.IsNullOrEmpty(description))
                {
                    announcement += $": {description}";
                }

                MelonLogger.Msg($"[Magic Menu] Announcing: {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch { }
        }

        // ==================== Target Selection Patches ====================

        // Offsets - use centralized offsets
        private const int OFFSET_USE_CONTENT_LIST = IL2CppOffsets.MagicTarget.ContentList;
        private const int OFFSET_USE_SELECT_CURSOR = IL2CppOffsets.MagicTarget.SelectCursor;

        /// <summary>
        /// Patches AbilityUseContentListController.SetCursor to announce target selection.
        /// FF1 KeyInput signature: SetCursor(Cursor targetCursor)
        /// </summary>
        private static void TryPatchTargetSetCursor(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityUseContentListController);

                // Find SetCursor(Cursor) method
                MethodInfo setCursorMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "SetCursor")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 1 && parameters[0].ParameterType.Name == "Cursor")
                        {
                            setCursorMethod = method;
                            break;
                        }
                    }
                }

                if (setCursorMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(TargetSetCursor_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setCursorMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched AbilityUseContentListController.SetCursor");
                }
                else
                {
                    MelonLogger.Warning("[Magic Menu] AbilityUseContentListController.SetCursor not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch target SetCursor: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches AbilityUseContentListController.Show to detect when target selection opens.
        /// </summary>
        private static void TryPatchTargetShow(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityUseContentListController);

                // Find Show method
                MethodInfo showMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "Show")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 3)
                        {
                            showMethod = method;
                            break;
                        }
                    }
                }

                if (showMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(TargetShow_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(showMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched AbilityUseContentListController.Show");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch target Show: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches AbilityUseContentListController.Close to detect when target selection closes.
        /// </summary>
        private static void TryPatchTargetClose(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityUseContentListController);
                var closeMethod = controllerType.GetMethod("Close",
                    BindingFlags.Instance | BindingFlags.Public,
                    null, Type.EmptyTypes, null);

                if (closeMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(TargetClose_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(closeMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched AbilityUseContentListController.Close");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch target Close: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for AbilityUseContentListController.Show - activates target selection state.
        /// </summary>
        public static void TargetShow_Postfix(object __instance)
        {
            try
            {
                MelonLogger.Msg("[Magic Menu] Target selection Show() - activating target state");
                MagicMenuState.OnTargetSelectionActive();
            }
            catch { }
        }

        /// <summary>
        /// Postfix for AbilityUseContentListController.Close - deactivates target selection state.
        /// Only processes if target selection was actually active.
        /// </summary>
        public static void TargetClose_Postfix(object __instance)
        {
            try
            {
                // Only clear if target selection was actually active
                if (!MagicMenuState.IsTargetSelectionActive)
                    return;

                MelonLogger.Msg("[Magic Menu] Target selection Close() - clearing target state");
                MagicMenuState.OnTargetSelectionClosed();
            }
            catch { }
        }

        /// <summary>
        /// Postfix for AbilityUseContentListController.SetCursor - announces target character.
        /// Format: "Character Name: HP: current/max MP: current/max Status effects"
        /// </summary>
        public static void TargetSetCursor_Postfix(object __instance, GameCursor targetCursor)
        {
            try
            {
                if (__instance == null || targetCursor == null)
                    return;

                var controller = __instance as AbilityUseContentListController;
                if (controller == null || !controller.gameObject.activeInHierarchy)
                    return;

                // Activate target selection state if not already active
                if (!MagicMenuState.IsTargetSelectionActive)
                {
                    MagicMenuState.OnTargetSelectionActive();
                }

                int index = targetCursor.Index;
                MelonLogger.Msg($"[Magic Menu] Target SetCursor called, index: {index}");

                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return;

                // Read contentList at offset 0x38
                IntPtr contentListPtr;
                unsafe
                {
                    contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_USE_CONTENT_LIST);
                }

                if (contentListPtr == IntPtr.Zero)
                {
                    MelonLogger.Msg("[Magic Menu] Target contentList is null");
                    return;
                }

                var contentList = new Il2CppSystem.Collections.Generic.List<ItemTargetSelectContentController>(contentListPtr);

                if (index < 0 || index >= contentList.Count)
                {
                    MelonLogger.Msg($"[Magic Menu] Target index {index} out of range (count={contentList.Count})");
                    return;
                }

                var content = contentList[index];
                if (content == null)
                    return;

                // Get character data from content controller
                var characterData = content.CurrentData;
                if (characterData == null)
                {
                    MelonLogger.Msg("[Magic Menu] Target character data is null");
                    return;
                }

                AnnounceTargetCharacter(characterData);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in TargetSetCursor_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces a target character with HP and status effects.
        /// Format: "Character Name: HP: current/max Status"
        /// </summary>
        private static void AnnounceTargetCharacter(OwnedCharacterData characterData)
        {
            try
            {
                string charName = characterData.Name;
                if (string.IsNullOrWhiteSpace(charName))
                    return;

                string announcement = charName;

                // Add HP and status information
                try
                {
                    var parameter = characterData.Parameter;
                    if (parameter != null)
                    {
                        // Add HP
                        int currentHp = parameter.currentHP;
                        int maxHp = parameter.ConfirmedMaxHp();
                        announcement += $": HP: {currentHp}/{maxHp}";

                        // Add status conditions
                        var conditionList = parameter.CurrentConditionList;
                        if (conditionList != null && conditionList.Count > 0)
                        {
                            var statusNames = new System.Collections.Generic.List<string>();
                            foreach (var condition in conditionList)
                            {
                                string conditionName = MagicMenuState.GetConditionName(condition);
                                if (!string.IsNullOrWhiteSpace(conditionName))
                                {
                                    statusNames.Add(conditionName);
                                }
                            }

                            if (statusNames.Count > 0)
                            {
                                announcement += " " + string.Join(", ", statusNames);
                            }
                        }
                    }
                }
                catch (Exception paramEx)
                {
                    MelonLogger.Warning($"[Magic Target] Error getting character parameters: {paramEx.Message}");
                }

                // Skip duplicates
                if (!MagicMenuState.ShouldAnnounceTarget(announcement))
                    return;

                MelonLogger.Msg($"[Magic Target] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Target] Error announcing character: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Attribute-based patch for AbilityWindowController.SetActive.
    /// Clears magic menu state when window is closed.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppSerial.FF1.UI.KeyInput.AbilityWindowController), "SetActive", new Type[] { typeof(bool) })]
    public static class AbilityWindowController_SetActive_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isActive)
        {
            if (!isActive)
            {
                MelonLogger.Msg("[Magic Menu] SetActive(false) via attribute patch - clearing state");
                MagicMenuState.ResetState();
            }
        }
    }
}
