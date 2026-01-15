using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

// FF1 Battle types - KeyInput namespace
using ResultMenuController = Il2CppLast.UI.KeyInput.ResultMenuController;
using BattleResultData = Il2CppLast.Data.BattleResultData;
using BattleResultCharacterData = Il2CppLast.Data.BattleResultData.BattleResultCharacterData;
using DropItemData = Il2CppLast.Data.DropItemData;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using ContentUtitlity = Il2CppLast.Systems.ContentUtitlity;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Battle result screen patches (victory).
    /// Announces gil, XP, item drops, and level ups.
    /// </summary>
    public static class BattleResultPatches
    {
        private static bool announcedPoints = false;
        private static bool announcedItems = false;
        private static HashSet<string> announcedLevelUps = new HashSet<string>();

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[Battle Result] Applying battle result patches...");

                var controllerType = typeof(ResultMenuController);

                // Patch ShowPointsInit for gil and XP announcements
                PatchMethod(harmony, controllerType, "ShowPointsInit", nameof(ShowPointsInit_Postfix));

                // Patch ShowGetItemsInit for item drop announcements
                PatchMethod(harmony, controllerType, "ShowGetItemsInit", nameof(ShowGetItemsInit_Postfix));

                // Patch ShowStatusUpInit for level up announcements
                PatchMethod(harmony, controllerType, "ShowStatusUpInit", nameof(ShowStatusUpInit_Postfix));

                MelonLogger.Msg("[Battle Result] Battle result patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Battle Result] Error applying patches: {ex.Message}");
            }
        }

        private static void PatchMethod(HarmonyLib.Harmony harmony, Type controllerType, string methodName, string postfixMethodName)
        {
            try
            {
                // Use AccessTools.Method which handles IL2CPP better than direct GetMethod
                var method = AccessTools.Method(controllerType, methodName);

                // Fallback: Try with BindingFlags if AccessTools didn't find it
                if (method == null)
                {
                    method = controllerType.GetMethod(
                        methodName,
                        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                    );
                }

                if (method != null)
                {
                    var postfix = typeof(BattleResultPatches).GetMethod(
                        postfixMethodName,
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg($"[Battle Result] Patched {methodName} successfully");
                }
                else
                {
                    // Log available methods to help debug
                    MelonLogger.Warning($"[Battle Result] {methodName} method not found");
                    LogAvailableMethods(controllerType);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Result] Error patching {methodName}: {ex.Message}");
            }
        }

        private static bool loggedMethods = false;
        private static void LogAvailableMethods(Type controllerType)
        {
            if (loggedMethods) return;
            loggedMethods = true;

            try
            {
                var methods = controllerType.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly
                );
                MelonLogger.Msg($"[Battle Result] Available methods on {controllerType.Name}:");
                foreach (var m in methods)
                {
                    if (m.Name.Contains("Show") || m.Name.Contains("Init") || m.Name.Contains("Points") || m.Name.Contains("Item") || m.Name.Contains("Status"))
                    {
                        MelonLogger.Msg($"[Battle Result]   - {m.Name}({string.Join(", ", System.Linq.Enumerable.Select(m.GetParameters(), p => p.ParameterType.Name))})");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Result] Error logging methods: {ex.Message}");
            }
        }

        // Offset for targetData field in ResultMenuController (FF1 KeyInput version)
        private const int OFFSET_TARGET_DATA = 0x60;

        /// <summary>
        /// Postfix for ShowPointsInit - announces gil and XP.
        /// Also clears all battle state flags.
        /// Uses IL2CPP property accessors for correct value reading.
        /// </summary>
        public static void ShowPointsInit_Postfix(ResultMenuController __instance)
        {
            try
            {
                if (announcedPoints) return;
                announcedPoints = true;

                // Clear all battle state flags
                ClearAllBattleStates();

                // Get battle result data via reflection (IL2CPP property accessor)
                BattleResultData resultData = GetTargetData(__instance);
                if (resultData == null)
                {
                    MelonLogger.Warning("[Battle Result] Could not get result data via reflection");
                    return;
                }

                MelonLogger.Msg("[Battle Result] Got result data via reflection");

                // Read gil using IL2CPP property accessor
                try
                {
                    int gil = resultData.GetGil;
                    string gilAnnouncement = $"Gained {gil:N0} gil";
                    MelonLogger.Msg($"[Battle Result] {gilAnnouncement}");
                    FFI_ScreenReaderMod.SpeakText(gilAnnouncement, interrupt: true);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Battle Result] Error reading gil: {ex.Message}");
                }

                // Per-character XP announcements
                try
                {
                    var characterList = resultData.CharacterList;
                    if (characterList != null)
                    {
                        int count = characterList.Count;
                        for (int i = 0; i < count; i++)
                        {
                            try
                            {
                                var charResult = characterList[i];
                                if (charResult == null) continue;

                                var afterData = charResult.AfterData;
                                if (afterData == null) continue;

                                string charName = afterData.Name;
                                if (string.IsNullOrEmpty(charName)) continue;

                                int charExp = charResult.GetExp;
                                if (charExp > 0)
                                {
                                    string expAnnouncement = $"{charName} gained {charExp:N0} XP";
                                    FFI_ScreenReaderMod.SpeakText(expAnnouncement, interrupt: false);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Result] Error in ShowPointsInit_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the targetData field via reflection.
        /// Returns the BattleResultData object with working property accessors.
        /// </summary>
        private static BattleResultData GetTargetData(ResultMenuController controller)
        {
            try
            {
                // Try reflection first - this gives us the proper IL2CPP wrapper
                var field = controller.GetType().GetField("targetData",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (field != null)
                {
                    var result = field.GetValue(controller) as BattleResultData;
                    if (result != null)
                    {
                        MelonLogger.Msg("[Battle Result] Got targetData via field reflection");
                        return result;
                    }
                }

                // Try property access
                var prop = controller.GetType().GetProperty("targetData",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                if (prop != null)
                {
                    var result = prop.GetValue(controller) as BattleResultData;
                    if (result != null)
                    {
                        MelonLogger.Msg("[Battle Result] Got targetData via property reflection");
                        return result;
                    }
                }

                // Fallback: try pointer access and create wrapper
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr != IntPtr.Zero)
                {
                    unsafe
                    {
                        IntPtr dataPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_TARGET_DATA);
                        if (dataPtr != IntPtr.Zero)
                        {
                            MelonLogger.Msg("[Battle Result] Got targetData via pointer access");
                            return new BattleResultData(dataPtr);
                        }
                    }
                }

                MelonLogger.Warning("[Battle Result] All methods to get targetData failed");
                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Result] Error getting targetData: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Postfix for ShowGetItemsInit - announces item drops.
        /// </summary>
        public static void ShowGetItemsInit_Postfix(ResultMenuController __instance)
        {
            try
            {
                if (announcedItems) return;
                announcedItems = true;

                BattleResultData resultData = GetTargetData(__instance);
                if (resultData == null) return;

                var itemList = resultData.ItemList;
                if (itemList == null) return;

                int count = itemList.Count;
                if (count == 0) return;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var dropItem = itemList[i];
                        if (dropItem == null) continue;

                        string itemName = GetDropItemName(dropItem);
                        if (string.IsNullOrEmpty(itemName)) continue;

                        // Strip icon markup
                        itemName = TextUtils.StripIconMarkup(itemName);
                        if (string.IsNullOrEmpty(itemName)) continue;

                        int dropCount = dropItem.DropValue;
                        string announcement = dropCount > 1
                            ? $"Found {itemName} x{dropCount}"
                            : $"Found {itemName}";

                        FFI_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Result] Error in ShowGetItemsInit_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ShowStatusUpInit - announces level ups.
        /// </summary>
        public static void ShowStatusUpInit_Postfix(ResultMenuController __instance)
        {
            try
            {
                BattleResultData resultData = GetTargetData(__instance);
                if (resultData == null) return;

                var characterList = resultData.CharacterList;
                if (characterList == null) return;

                int count = characterList.Count;
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var charResult = characterList[i];
                        if (charResult == null) continue;

                        // Check if this character leveled up
                        if (!charResult.IsLevelUp) continue;

                        var afterData = charResult.AfterData;
                        if (afterData == null) continue;

                        string charName = afterData.Name;
                        if (string.IsNullOrEmpty(charName)) continue;

                        // Prevent duplicate announcements
                        string key = $"{charName}_levelup";
                        if (announcedLevelUps.Contains(key)) continue;
                        announcedLevelUps.Add(key);

                        // Get new level
                        int newLevel = 0;
                        var afterParam = afterData.Parameter;
                        if (afterParam != null)
                        {
                            try { newLevel = afterParam.ConfirmedLevel(); }
                            catch { newLevel = afterParam.BaseLevel; }
                        }

                        // Build announcement
                        var parts = new List<string>();
                        parts.Add($"{charName} leveled up to {newLevel}");

                        // Calculate stat gains if we have before data
                        var beforeData = charResult.BeforData; // Note: typo in game code
                        MelonLogger.Msg($"[Battle Result] Level up: beforeData={beforeData != null}, beforeParam={beforeData?.Parameter != null}, afterParam={afterParam != null}");

                        if (beforeData?.Parameter != null && afterParam != null)
                        {
                            string statGains = CalculateStatGains(beforeData.Parameter, afterParam);
                            MelonLogger.Msg($"[Battle Result] Stat gains result: '{statGains ?? "null"}'");
                            if (!string.IsNullOrEmpty(statGains))
                            {
                                parts.Add(statGains);
                            }
                        }
                        else
                        {
                            MelonLogger.Msg("[Battle Result] Cannot calculate stat gains - missing before/after data");
                        }

                        string announcement = string.Join(", ", parts);
                        MelonLogger.Msg($"[Battle Result] Final announcement: {announcement}");
                        FFI_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Result] Error in ShowStatusUpInit_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Get item name from DropItemData.
        /// </summary>
        private static string GetDropItemName(DropItemData dropItem)
        {
            try
            {
                // Get content ID and use ContentUtitlity + MessageManager
                int contentId = dropItem.ContentId;
                if (contentId > 0)
                {
                    // Get message ID first, then resolve to actual text
                    string mesId = ContentUtitlity.GetMesIdItemName(contentId);
                    if (!string.IsNullOrEmpty(mesId))
                    {
                        var messageManager = Il2CppLast.Management.MessageManager.Instance;
                        if (messageManager != null)
                        {
                            return messageManager.GetMessage(mesId, false);
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Calculate stat gains between before and after parameter data.
        /// FF1 uses different property names than FF3 (BasePower, BaseVitality, etc.)
        /// </summary>
        private static string CalculateStatGains(
            Il2CppLast.Data.CharacterParameterBase before,
            Il2CppLast.Data.CharacterParameterBase after)
        {
            var gains = new List<string>();

            try
            {
                MelonLogger.Msg($"[Battle Result] CalculateStatGains: before={before != null}, after={after != null}");

                // HP
                int hpBefore = 0, hpAfter = 0;
                try { hpBefore = before.ConfirmedMaxHp(); } catch { try { hpBefore = before.BaseMaxHp; } catch { } }
                try { hpAfter = after.ConfirmedMaxHp(); } catch { try { hpAfter = after.BaseMaxHp; } catch { } }
                MelonLogger.Msg($"[Battle Result] HP: {hpBefore} -> {hpAfter}");
                if (hpAfter > hpBefore) gains.Add($"HP +{hpAfter - hpBefore}");

                // Strength (FF1 uses BasePower)
                int strBefore = 0, strAfter = 0;
                try { strBefore = before.BasePower; MelonLogger.Msg($"[Battle Result] Str before: {strBefore}"); }
                catch (Exception ex) { MelonLogger.Msg($"[Battle Result] Str before failed: {ex.Message}"); }
                try { strAfter = after.BasePower; MelonLogger.Msg($"[Battle Result] Str after: {strAfter}"); }
                catch (Exception ex) { MelonLogger.Msg($"[Battle Result] Str after failed: {ex.Message}"); }
                if (strAfter > strBefore) gains.Add($"Strength +{strAfter - strBefore}");

                // Agility (FF1 uses BaseAgility)
                int agiBefore = 0, agiAfter = 0;
                try { agiBefore = before.BaseAgility; } catch { }
                try { agiAfter = after.BaseAgility; } catch { }
                MelonLogger.Msg($"[Battle Result] Agi: {agiBefore} -> {agiAfter}");
                if (agiAfter > agiBefore) gains.Add($"Agility +{agiAfter - agiBefore}");

                // Stamina (displayed as "Stamina" in-game, property is BaseVitality)
                int vitBefore = 0, vitAfter = 0;
                try { vitBefore = before.BaseVitality; } catch { }
                try { vitAfter = after.BaseVitality; } catch { }
                MelonLogger.Msg($"[Battle Result] Vit: {vitBefore} -> {vitAfter}");
                if (vitAfter > vitBefore) gains.Add($"Stamina +{vitAfter - vitBefore}");

                // Intellect (displayed as "Intellect" in-game, property is BaseIntelligence)
                int intBefore = 0, intAfter = 0;
                try { intBefore = before.BaseIntelligence; } catch { }
                try { intAfter = after.BaseIntelligence; } catch { }
                MelonLogger.Msg($"[Battle Result] Int: {intBefore} -> {intAfter}");
                if (intAfter > intBefore) gains.Add($"Intellect +{intAfter - intBefore}");

                // Luck (FF1 uses BaseLuck - same name)
                int luckBefore = 0, luckAfter = 0;
                try { luckBefore = before.BaseLuck; } catch { }
                try { luckAfter = after.BaseLuck; } catch { }
                MelonLogger.Msg($"[Battle Result] Luck: {luckBefore} -> {luckAfter}");
                if (luckAfter > luckBefore) gains.Add($"Luck +{luckAfter - luckBefore}");

                MelonLogger.Msg($"[Battle Result] Total stat gains: {gains.Count}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Result] Error in CalculateStatGains: {ex.Message}");
            }

            return gains.Count > 0 ? string.Join(", ", gains) : null;
        }

        /// <summary>
        /// Clear all battle state flags.
        /// </summary>
        private static void ClearAllBattleStates()
        {
            BattleCommandState.ResetState();
            BattleTargetState.ResetState();
            BattleItemMenuState.ResetState();
            BattleMagicMenuState.ResetState();
            BattleMessagePatches.ResetState();
            BattleCommandPatches.ResetState();
            BattleItemPatches.ResetState();
            BattleMagicPatches.ResetState();
        }

        /// <summary>
        /// Reset state (call at battle start).
        /// </summary>
        public static void ResetState()
        {
            announcedPoints = false;
            announcedItems = false;
            announcedLevelUps.Clear();
        }
    }
}
