using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

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
                var controllerType = typeof(ResultMenuController);

                // Patch ShowPointsInit for gil and XP announcements
                PatchMethod(harmony, controllerType, "ShowPointsInit", nameof(ShowPointsInit_Postfix));

                // Patch ShowGetItemsInit for item drop announcements
                PatchMethod(harmony, controllerType, "ShowGetItemsInit", nameof(ShowGetItemsInit_Postfix));

                // Patch ShowStatusUpInit for level up announcements
                PatchMethod(harmony, controllerType, "ShowStatusUpInit", nameof(ShowStatusUpInit_Postfix));

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
                foreach (var m in methods)
                {
                    if (m.Name.Contains("Show") || m.Name.Contains("Init") || m.Name.Contains("Points") || m.Name.Contains("Item") || m.Name.Contains("Status"))
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Result] Error logging methods: {ex.Message}");
            }
        }

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

                // Read gil using IL2CPP property accessor
                try
                {
                    int gil = resultData.GetGil;
                    string gilAnnouncement = string.Format(T("Gained {0} gil"), gil.ToString("N0"));
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
                                    string expAnnouncement = string.Format(T("{0} gained {1} XP"), charName, charExp.ToString("N0"));
                                    FFI_ScreenReaderMod.SpeakText(expAnnouncement, interrupt: false);
                                }
                            }
                            catch { } // Character data may be partially torn down
                        }
                    }
                }
                catch { } // Result data may be partially available
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
                        return result;
                    }
                }

                // Fallback: try pointer access and create wrapper
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr != IntPtr.Zero)
                {
                    IntPtr dataPtr = IL2CppFieldReader.ReadPointer(controllerPtr, IL2CppOffsets.BattleResult.TargetData);
                    if (dataPtr != IntPtr.Zero)
                    {
                        return new BattleResultData(dataPtr);
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
                            ? string.Format(T("Found {0} x{1}"), itemName, dropCount)
                            : string.Format(T("Found {0}"), itemName);

                        FFI_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                    }
                    catch { } // Drop item data may be partially available
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
                            catch { newLevel = afterParam.BaseLevel; } // Fall back to base level
                        }

                        // Build announcement
                        var parts = new List<string>();
                        parts.Add(string.Format(T("{0} leveled up to {1}"), charName, newLevel));

                        // Calculate stat gains if we have before data
                        var beforeData = charResult.BeforData; // Note: typo in game code

                        if (beforeData?.Parameter != null && afterParam != null)
                        {
                            string statGains = CalculateStatGains(beforeData.Parameter, afterParam);
                            if (!string.IsNullOrEmpty(statGains))
                            {
                                parts.Add(statGains);
                            }
                        }
                        string announcement = string.Join(", ", parts);
                        FFI_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                    }
                    catch { } // Character data may be partially torn down
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
            catch { } // IL2CPP content resolution may fail

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
                // HP
                int hpBefore = 0, hpAfter = 0;
                try { hpBefore = before.ConfirmedMaxHp(); } catch { try { hpBefore = before.BaseMaxHp; } catch { } } // IL2CPP accessor may fail
                try { hpAfter = after.ConfirmedMaxHp(); } catch { try { hpAfter = after.BaseMaxHp; } catch { } } // IL2CPP accessor may fail
                if (hpAfter > hpBefore) gains.Add(string.Format(T("HP +{0}"), hpAfter - hpBefore));

                // Strength (FF1 uses BasePower)
                int strBefore = 0, strAfter = 0;
                try { strBefore = before.BasePower; }
                catch { } // IL2CPP accessor may fail
                try { strAfter = after.BasePower; }
                catch { } // IL2CPP accessor may fail
                if (strAfter > strBefore) gains.Add(string.Format(T("Strength +{0}"), strAfter - strBefore));

                // Agility (FF1 uses BaseAgility)
                int agiBefore = 0, agiAfter = 0;
                try { agiBefore = before.BaseAgility; } catch { } // IL2CPP accessor may fail
                try { agiAfter = after.BaseAgility; } catch { } // IL2CPP accessor may fail
                if (agiAfter > agiBefore) gains.Add(string.Format(T("Agility +{0}"), agiAfter - agiBefore));

                // Stamina (displayed as "Stamina" in-game, property is BaseVitality)
                int vitBefore = 0, vitAfter = 0;
                try { vitBefore = before.BaseVitality; } catch { } // IL2CPP accessor may fail
                try { vitAfter = after.BaseVitality; } catch { } // IL2CPP accessor may fail
                if (vitAfter > vitBefore) gains.Add(string.Format(T("Stamina +{0}"), vitAfter - vitBefore));

                // Intellect (displayed as "Intellect" in-game, property is BaseIntelligence)
                int intBefore = 0, intAfter = 0;
                try { intBefore = before.BaseIntelligence; } catch { } // IL2CPP accessor may fail
                try { intAfter = after.BaseIntelligence; } catch { } // IL2CPP accessor may fail
                if (intAfter > intBefore) gains.Add(string.Format(T("Intellect +{0}"), intAfter - intBefore));

                // Luck (FF1 uses BaseLuck - same name)
                int luckBefore = 0, luckAfter = 0;
                try { luckBefore = before.BaseLuck; } catch { } // IL2CPP accessor may fail
                try { luckAfter = after.BaseLuck; } catch { } // IL2CPP accessor may fail
                if (luckAfter > luckBefore) gains.Add(string.Format(T("Luck +{0}"), luckAfter - luckBefore));

            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Result] Error in CalculateStatGains: {ex.Message}");
            }

            return gains.Count > 0 ? string.Join(", ", gains) : null;
        }

        /// <summary>
        /// Clear all battle state flags (internal use).
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
        /// Clear all battle menu flags (public alias for PopupPatches).
        /// Called when returning to title screen to ensure clean state.
        /// </summary>
        public static void ClearAllBattleMenuFlags()
        {
            ClearAllBattleStates();
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
