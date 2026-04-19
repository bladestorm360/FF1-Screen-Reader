using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

// FF1 Battle types - KeyInput namespace
using BattleFrequencyAbilityInfomationController = Il2CppSerial.FF1.UI.KeyInput.BattleFrequencyAbilityInfomationController;
using BattleAbilityInfomationContentController = Il2CppLast.UI.KeyInput.BattleAbilityInfomationContentController;
using OwnedAbility = Il2CppLast.Data.User.OwnedAbility;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using MessageManager = Il2CppLast.Management.MessageManager;
using PlayerCharacterParameter = Il2CppLast.Data.PlayerCharacterParameter;
using AbilityLevelType = Il2CppLast.Defaine.Master.AbilityLevelType;
using GameCursor = Il2CppLast.UI.Cursor;
using Il2CppInterop.Runtime;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Battle magic menu patches for FF1.
    /// FF1 uses spell slot/charges system (X/Y charges per spell level), NOT MP.
    /// </summary>
    public static class BattleMagicPatches
    {
        // Cache the current player for charge lookup
        public static BattlePlayerData CurrentPlayer { get; set; } = null;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(BattleFrequencyAbilityInfomationController);

                // Find SetCursor(Cursor, bool, WithinRangeType) - called on every cursor move
                // SelectContent doesn't have an index param, so we use SetCursor instead
                MethodInfo setCursorMethod = null;
                var methods = controllerType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                // Log all relevant methods for debugging
                foreach (var m in methods)
                {
                    if (m.Name == "SetCursor" || m.Name == "SelectContent")
                    {
                        var parameters = m.GetParameters();
                        var paramTypes = string.Join(", ", Array.ConvertAll(parameters, p => p.ParameterType.Name));

                        // Look for SetCursor(Cursor, bool, WithinRangeType)
                        if (m.Name == "SetCursor" && parameters.Length >= 1 &&
                            parameters[0].ParameterType.Name == "Cursor")
                        {
                            setCursorMethod = m;
                        }
                    }
                }

                if (setCursorMethod != null)
                {
                    var postfix = typeof(BattleMagicPatches)
                        .GetMethod(nameof(SetCursor_Postfix), BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(setCursorMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Battle Magic] SetCursor method not found - listing all methods:");
                    foreach (var m in methods)
                    {
                        var parms = string.Join(", ", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name));
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SetCursor - announces spell name, charges, and description.
        /// SetCursor(Cursor targetCursor, bool isForce, WithinRangeType type)
        /// Using positional parameters: __0 = cursor
        /// </summary>
        public static void SetCursor_Postfix(object __instance, object __0)
        {
            try
            {
                if (__instance == null || __0 == null)
                    return;

                // Get cursor and its index
                var cursor = __0 as GameCursor;
                if (cursor == null)
                    return;

                var controller = __instance as BattleFrequencyAbilityInfomationController;
                if (controller == null)
                    return;

                // Only announce if the magic menu controller is actually visible
                // This prevents "Empty" announcements during target selection
                if (!controller.gameObject.activeInHierarchy)
                    return;

                // Refresh selected player from controller — avoids stale slot counts
                // when a different character's turn begins (CurrentPlayer was cached once).
                try
                {
                    IntPtr controllerPtr = controller.Pointer;
                    if (controllerPtr != IntPtr.Zero)
                    {
                        IntPtr playerPtr = IL2CppFieldReader.ReadPointer(controllerPtr, OFFSET_SELECTED_PLAYER);
                        CurrentPlayer = playerPtr != IntPtr.Zero ? new BattlePlayerData(playerPtr) : null;
                    }
                }
                catch { CurrentPlayer = null; }

                // If command menu is active but user didn't select "Magic" (index 1),
                // this is a stale callback from exiting the magic menu - ignore it
                if (BattleCommandState.IsActive && BattleCommandState.LastSelectedCommandIndex != 1)
                    return;

                int index = cursor.Index;

                // NOTE: Do NOT set IsActive here - wait until AFTER validation succeeds

                // Try to get ability data - use the cursor index with dataList
                string announcement = TryGetAbilityAnnouncement(controller, index);

                if (string.IsNullOrEmpty(announcement))
                    return;

                // Use central deduplicator - skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.BATTLE_MAGIC, announcement))
                    return;

                // Set state AFTER successful validation - this is the key fix
                FFI_ScreenReaderMod.ClearOtherMenuStates("BattleMagic");
                BattleMagicMenuState.IsActive = true;
                BattleTargetState.SetTargetSelectionActive(false);

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error in SetCursor patch: {ex.Message}");
            }
        }

        // IL2CPP field offsets - use centralized offsets
        private const int OFFSET_DATA_LIST = IL2CppOffsets.BattleMagic.DataList;
        private const int OFFSET_CONTENT_LIST = IL2CppOffsets.BattleMagic.ContentList;

        /// <summary>
        /// Try to get ability announcement from controller.
        /// Uses dataList (offset 0x70) which has OwnedAbility items.
        /// Uses IL2CPP pointer-based access since reflection doesn't work for IL2CPP fields.
        /// </summary>
        private static string TryGetAbilityAnnouncement(BattleFrequencyAbilityInfomationController controller, int index)
        {
            try
            {
                // Method 1: Try dataList via IL2CPP pointer access - offset 0x70
                Il2CppSystem.Collections.Generic.List<OwnedAbility> dataList = null;
                try
                {
                    IntPtr controllerPtr = controller.Pointer;
                    if (controllerPtr != IntPtr.Zero)
                    {
                        IntPtr dataListPtr = IL2CppFieldReader.ReadPointer(controllerPtr, OFFSET_DATA_LIST);
                        if (dataListPtr != IntPtr.Zero)
                        {
                            dataList = new Il2CppSystem.Collections.Generic.List<OwnedAbility>(dataListPtr);
                        }
                    }
                }
                catch { } // IL2CPP pointer read may fail

                // Only process if dataList has items - if count is 0, magic menu isn't really populated
                // (contentList always has template entries that would cause false "Empty" announcements)
                if (dataList != null && dataList.Count > 0)
                {
                    if (index >= 0 && index < dataList.Count)
                    {
                        var ability = dataList[index];
                        if (ability != null)
                        {
                            string result = FormatAbilityAnnouncement(ability);
                            if (!string.IsNullOrEmpty(result))
                                return result;
                        }
                        // Slot exists but is empty/null - only announce if already in magic menu
                        // This prevents "Empty" during target selection when dataList has leftover data
                        if (BattleMagicMenuState.IsActive)
                        {
                            return T("Empty");
                        }
                        else
                        {
                            return null;
                        }
                    }
                    else if (index >= dataList.Count)
                    {
                        // Index beyond list - only announce empty if already in magic menu
                        if (BattleMagicMenuState.IsActive)
                        {
                            return T("Empty");
                        }
                        else
                        {
                            return null;
                        }
                    }
                }

                // If dataList is empty/null, don't fall through to contentList
                // contentList has template entries that would cause false announcements
                if (dataList == null || dataList.Count == 0)
                {
                    return null;
                }

                // Method 3: Find focused content controller
                var allContentControllers = UnityEngine.Object.FindObjectsOfType<BattleAbilityInfomationContentController>();
                if (allContentControllers != null && allContentControllers.Length > 0)
                {
                    foreach (var cc in allContentControllers)
                    {
                        if (cc == null || !cc.gameObject.activeInHierarchy)
                            continue;

                        try
                        {
                            // Check if this content controller is focused
                            bool isFocus = false;
                            try
                            {
                                var focusProp = cc.GetType().GetProperty("IsFocus",
                                    BindingFlags.Public | BindingFlags.Instance);
                                if (focusProp != null)
                                {
                                    isFocus = (bool)focusProp.GetValue(cc);
                                }
                            }
                            catch { } // IsFocus property may not exist

                            var data = cc.Data;
                            if (data != null && isFocus)
                            {
                                return FormatAbilityAnnouncement(data);
                            }
                        }
                        catch { } // Content controller data may be stale
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error getting ability announcement: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Format ability data into announcement string.
        /// Format: "Spell Name LvX: charges x/y. Description"
        /// </summary>
        private static string FormatAbilityAnnouncement(OwnedAbility ability)
        {
            try
            {
                // Get spell name via MesIdName
                string name = null;
                string mesIdName = ability.MesIdName;
                if (!string.IsNullOrEmpty(mesIdName))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        name = messageManager.GetMessage(mesIdName, false);
                    }
                }

                if (string.IsNullOrEmpty(name))
                    return null;

                name = TextUtils.StripIconMarkup(name);
                if (string.IsNullOrEmpty(name))
                    return null;

                string announcement = name;
                int spellLevel = 0;

                // Try to get spell level
                try
                {
                    var abilityData = ability.Ability;
                    if (abilityData != null)
                    {
                        spellLevel = abilityData.AbilityLv;
                        if (spellLevel > 0 && spellLevel <= 8)
                        {
                            announcement += $" Lv{spellLevel}";
                        }
                    }
                }
                catch { } // Ability data may not have level info

                // Try to get charges (format matches out-of-battle: "MP: current/max")
                if (spellLevel > 0 && spellLevel <= 8)
                {
                    try
                    {
                        var charges = GetChargesForLevel(spellLevel);
                        if (charges.max > 0)
                        {
                            announcement += $": MP: {charges.current}/{charges.max}";
                        }
                    }
                    catch { } // Charge lookup may fail in battle
                }

                // Try to get description
                try
                {
                    string mesIdDesc = ability.MesIdDescription;
                    if (!string.IsNullOrEmpty(mesIdDesc))
                    {
                        var messageManager = MessageManager.Instance;
                        if (messageManager != null)
                        {
                            string description = messageManager.GetMessage(mesIdDesc, false);
                            if (!string.IsNullOrWhiteSpace(description))
                            {
                                description = TextUtils.StripIconMarkup(description);
                                if (!string.IsNullOrWhiteSpace(description))
                                {
                                    announcement += ". " + description;
                                }
                            }
                        }
                    }
                }
                catch { } // Description not always available

                return announcement;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error formatting announcement: {ex.Message}");
                return null;
            }
        }

        // Offset - use centralized offsets
        private const int OFFSET_SELECTED_PLAYER = IL2CppOffsets.BattleMagic.SelectedPlayer;

        // IL2CPP offsets - use centralized offsets
        private const int OFFSET_BATTLE_UNIT_DATA_INFO = IL2CppOffsets.BattleUnit.BattleUnitDataInfo;
        private const int OFFSET_PARAMETER = IL2CppOffsets.BattleUnit.Parameter;

        /// <summary>
        /// Gets current and max charges for a given spell level.
        /// Uses BattleUnitDataInfo.Parameter path since ownedCharacterData is null in battle.
        /// </summary>
        private static (int current, int max) GetChargesForLevel(int level)
        {
            try
            {
                var player = CurrentPlayer;
                if (player == null)
                {
                    // Try to find from scene using pointer-based access
                    var controller = UnityEngine.Object.FindObjectOfType<BattleFrequencyAbilityInfomationController>();
                    if (controller != null)
                    {
                        try
                        {
                            IntPtr controllerPtr = controller.Pointer;
                            if (controllerPtr != IntPtr.Zero)
                            {
                                // Read selectedBattlePlayerData at offset 0x28
                                IntPtr playerPtr = IL2CppFieldReader.ReadPointer(controllerPtr, OFFSET_SELECTED_PLAYER);
                                if (playerPtr != IntPtr.Zero)
                                {
                                    player = new BattlePlayerData(playerPtr);
                                    CurrentPlayer = player;
                                }
                            }
                        }
                        catch { } // IL2CPP pointer read may fail
                    }
                }

                if (player == null)
                    return (0, 0);

                // Try ownedCharacterData first
                PlayerCharacterParameter param = null;
                var ownedCharData = player.ownedCharacterData;
                if (ownedCharData != null)
                {
                    param = ownedCharData.Parameter as PlayerCharacterParameter;
                }

                // Fallback: Use BattleUnitDataInfo.Parameter path (works in battle)
                if (param == null)
                {
                    try
                    {
                        IntPtr playerPtr = player.Pointer;
                        if (playerPtr != IntPtr.Zero)
                        {
                            // Read BattleUnitDataInfo at offset 0x28 (from BattleUnitData base class)
                            IntPtr battleUnitDataInfoPtr = IL2CppFieldReader.ReadPointer(playerPtr, OFFSET_BATTLE_UNIT_DATA_INFO);
                            if (battleUnitDataInfoPtr != IntPtr.Zero)
                            {
                                // Read Parameter at offset 0x10
                                IntPtr parameterPtr = IL2CppFieldReader.ReadPointer(battleUnitDataInfoPtr, OFFSET_PARAMETER);
                                if (parameterPtr != IntPtr.Zero)
                                {
                                    param = new PlayerCharacterParameter(parameterPtr);
                                }
                            }
                        }
                    }
                    catch { } // IL2CPP pointer chain may fail
                }

                if (param == null)
                    return (0, 0);

                // Get current charges
                int current = 0;
                var currentList = param.CurrentMpCountList;
                if (currentList != null && currentList.ContainsKey(level))
                {
                    current = currentList[level];
                }

                // Get max charges
                int max = 0;
                try
                {
                    max = param.ConfirmedMaxMpCount((AbilityLevelType)level);
                }
                catch
                { // ConfirmedMaxMpCount may fail; use base list
                    var baseMaxList = param.BaseMaxMpCountList;
                    if (baseMaxList != null && baseMaxList.ContainsKey(level))
                    {
                        max = baseMaxList[level];
                    }
                }

                return (current, max);
            }
            catch { return (0, 0); } // Charge data unavailable
        }

        /// <summary>
        /// Reset state (call at battle end).
        /// </summary>
        public static void ResetState()
        {
            AnnouncementDeduplicator.Reset(AnnouncementContexts.BATTLE_MAGIC);
            CurrentPlayer = null;
        }
    }
}
