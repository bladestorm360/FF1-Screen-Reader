using System;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
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

        // True only while the EXP counter tone is actually playing.
        private static bool expCounterPlaying = false;
        private static bool monitorLoggedOnce = false;

        /// <summary>
        /// Stops the EXP counter tone if it is currently playing. Safe to call from any
        /// phase-init postfix or from ResetState; the flag ensures it only fires once.
        /// </summary>
        private static void StopExpCounterIfPlaying()
        {
            if (!expCounterPlaying) return;
            expCounterPlaying = false;
            SoundPlayer.StopExpCounter();
            MelonLogger.Msg("[Battle Result] EXP counter stopped");
        }

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(ResultMenuController);

                // Patch ShowPointsInit for gil and XP announcements (also starts the EXP counter tone)
                PatchMethod(harmony, controllerType, "ShowPointsInit", nameof(ShowPointsInit_Postfix));

                // Patch ShowGetItemsInit for item drop announcements
                PatchMethod(harmony, controllerType, "ShowGetItemsInit", nameof(ShowGetItemsInit_Postfix));

                // Patch ShowStatusUpInit for level up announcements
                PatchMethod(harmony, controllerType, "ShowStatusUpInit", nameof(ShowStatusUpInit_Postfix));

                // Additional post-points phases — each is a safety net that stops the EXP
                // counter tone if the animation monitor has not already stopped it.
                PatchMethod(harmony, controllerType, "ShowGetAbilitysInit", nameof(ShowGetAbilitysInit_Postfix));
                PatchMethod(harmony, controllerType, "ShowLevelUpAbilitysInit", nameof(ShowLevelUpAbilitysInit_Postfix));
                PatchMethod(harmony, controllerType, "EndWaitInit", nameof(EndWaitInit_Postfix));

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

                // Per-character XP announcements. Accumulate the party total so we only
                // start the EXP counter tone when EXP was actually gained.
                int totalExp = 0;
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
                                    totalExp += charExp;
                                    string expAnnouncement = string.Format(T("{0} gained {1} XP"), charName, charExp.ToString("N0"));
                                    FFI_ScreenReaderMod.SpeakText(expAnnouncement, interrupt: false);
                                }
                            }
                            catch { } // Character data may be partially torn down
                        }
                    }
                }
                catch { } // Result data may be partially available

                // Start the EXP counter tone if enabled and any EXP was gained. The tone tracks
                // the rolling EXP-bar animation; a monitor coroutine stops it when the tally
                // finishes, and the later phase-init safety nets stop it otherwise.
                if (PreferencesManager.ExpCounterEnabled && totalExp > 0)
                {
                    SoundPlayer.PlayExpCounter();
                    expCounterPlaying = true;
                    monitorLoggedOnce = false;
                    CoroutineManager.StartUntracked(MonitorExpCounterAnimation(__instance.Pointer));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Result] Error in ShowPointsInit_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Polls the unsafe pointer chain from ResultMenuController (KeyInput) to detect when the
        /// EXP counting animation finishes, then stops the counter tone. The FF1 KeyInput result
        /// object graph has the same field offsets as FF5:
        ///   instance -> +0x20 (pointController) -> +0x30 (characterListConteroller)
        ///     -> +0x20 (contentList, List._size at +0x18)
        ///     -> +0x30 (perormanceEndCount)
        /// Animation done when perormanceEndCount >= contentList.Count (with Count > 0).
        /// If the chain can't be read, the coroutine bails and the phase-init safety nets stop
        /// the tone instead, so it can never get stuck playing.
        /// </summary>
        private static IEnumerator MonitorExpCounterAnimation(IntPtr instancePtr)
        {
            var wait = new WaitForSeconds(0.1f);

            if (instancePtr == IntPtr.Zero)
            {
                MelonLogger.Warning("[Battle Result] MonitorExp: instancePtr is null");
                yield break;
            }

            IntPtr pointControllerPtr = Marshal.ReadIntPtr(instancePtr, 0x20);
            if (pointControllerPtr == IntPtr.Zero)
            {
                MelonLogger.Warning("[Battle Result] MonitorExp: pointController is null");
                yield break;
            }

            IntPtr charListCtrlPtr = Marshal.ReadIntPtr(pointControllerPtr, 0x30);
            if (charListCtrlPtr == IntPtr.Zero)
            {
                MelonLogger.Warning("[Battle Result] MonitorExp: characterListController is null");
                yield break;
            }

            IntPtr contentListPtr = Marshal.ReadIntPtr(charListCtrlPtr, 0x20);
            if (contentListPtr == IntPtr.Zero)
            {
                MelonLogger.Warning("[Battle Result] MonitorExp: contentList is null");
                yield break;
            }

            // contentList.Count (List._size) at contentListPtr + 0x18
            int contentCount = Marshal.ReadInt32(contentListPtr, 0x18);
            if (contentCount <= 0)
            {
                MelonLogger.Warning($"[Battle Result] MonitorExp: contentCount={contentCount}, aborting (safety nets will stop the tone)");
                yield break;
            }

            MelonLogger.Msg($"[Battle Result] MonitorExp: chain OK. contentCount={contentCount}");

            // Poll until the animation finishes or the counter was already stopped by a safety net.
            while (expCounterPlaying)
            {
                yield return wait;

                // Keep the Counter stream fed so the loop never drains between ticks.
                SoundPlayer.TopUpExpCounter();

                int endCount;
                try
                {
                    endCount = Marshal.ReadInt32(charListCtrlPtr, 0x30);
                }
                catch
                {
                    // Pointer became invalid -- bail out; safety nets will handle the stop.
                    yield break;
                }

                if (!monitorLoggedOnce)
                {
                    MelonLogger.Msg($"[Battle Result] MonitorExp: first poll endCount={endCount}/{contentCount}");
                    monitorLoggedOnce = true;
                }

                if (endCount >= contentCount)
                {
                    MelonLogger.Msg($"[Battle Result] MonitorExp: animation done (endCount={endCount} >= contentCount={contentCount})");
                    StopExpCounterIfPlaying();
                    yield break;
                }
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
                // Safety net: the EXP tally is finished by the time this phase begins.
                StopExpCounterIfPlaying();

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
                // Safety net: the EXP tally is finished by the time this phase begins.
                StopExpCounterIfPlaying();

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
        /// Postfix for ShowGetAbilitysInit — safety net that stops the EXP counter tone.
        /// </summary>
        public static void ShowGetAbilitysInit_Postfix()
        {
            StopExpCounterIfPlaying();
        }

        /// <summary>
        /// Postfix for ShowLevelUpAbilitysInit — safety net that stops the EXP counter tone.
        /// </summary>
        public static void ShowLevelUpAbilitysInit_Postfix()
        {
            StopExpCounterIfPlaying();
        }

        /// <summary>
        /// Postfix for EndWaitInit — the terminal results phase. Final safety net that
        /// guarantees the EXP counter tone is stopped before the results screen closes.
        /// </summary>
        public static void EndWaitInit_Postfix()
        {
            StopExpCounterIfPlaying();
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
        /// FF1 properties: BasePower, BaseAgility, BaseVitality, BaseIntelligence, BaseLuck,
        /// BaseAccuracyRate, BaseEvasionRate. Prefers the Confirmed*() methods because the
        /// auto-property getters throw under IL2CPP on some builds.
        /// </summary>
        private static string CalculateStatGains(
            Il2CppLast.Data.CharacterParameterBase before,
            Il2CppLast.Data.CharacterParameterBase after)
        {
            var gains = new List<string>();

            try
            {
                // HP - Confirmed*() method calls work reliably in IL2CPP; Base* property
                // accessors throw on the auto-property getter for many builds, so always
                // prefer the Confirmed*() abstract methods and only fall back to the property.
                int hpBefore = 0, hpAfter = 0;
                try { hpBefore = before.ConfirmedMaxHp(); } catch { try { hpBefore = before.BaseMaxHp; } catch { } }
                try { hpAfter = after.ConfirmedMaxHp(); } catch { try { hpAfter = after.BaseMaxHp; } catch { } }
                if (hpAfter > hpBefore) gains.Add(string.Format(T("HP +{0}"), hpAfter - hpBefore));

                // Strength (BasePower / ConfirmedPower)
                int strBefore = 0, strAfter = 0;
                try { strBefore = before.ConfirmedPower(); } catch { try { strBefore = before.BasePower; } catch { } }
                try { strAfter = after.ConfirmedPower(); } catch { try { strAfter = after.BasePower; } catch { } }
                if (strAfter > strBefore) gains.Add(string.Format(T("Strength +{0}"), strAfter - strBefore));

                // Agility (BaseAgility / ConfirmedAgility)
                int agiBefore = 0, agiAfter = 0;
                try { agiBefore = before.ConfirmedAgility(); } catch { try { agiBefore = before.BaseAgility; } catch { } }
                try { agiAfter = after.ConfirmedAgility(); } catch { try { agiAfter = after.BaseAgility; } catch { } }
                if (agiAfter > agiBefore) gains.Add(string.Format(T("Agility +{0}"), agiAfter - agiBefore));

                // Stamina (BaseVitality / ConfirmedVitality - displayed in-game as "Stamina")
                int vitBefore = 0, vitAfter = 0;
                try { vitBefore = before.ConfirmedVitality(); } catch { try { vitBefore = before.BaseVitality; } catch { } }
                try { vitAfter = after.ConfirmedVitality(); } catch { try { vitAfter = after.BaseVitality; } catch { } }
                if (vitAfter > vitBefore) gains.Add(string.Format(T("Stamina +{0}"), vitAfter - vitBefore));

                // Intellect (BaseIntelligence / ConfirmedIntelligence)
                int intBefore = 0, intAfter = 0;
                try { intBefore = before.ConfirmedIntelligence(); } catch { try { intBefore = before.BaseIntelligence; } catch { } }
                try { intAfter = after.ConfirmedIntelligence(); } catch { try { intAfter = after.BaseIntelligence; } catch { } }
                if (intAfter > intBefore) gains.Add(string.Format(T("Intellect +{0}"), intAfter - intBefore));

                // Luck (BaseLuck / ConfirmedLuck)
                int luckBefore = 0, luckAfter = 0;
                try { luckBefore = before.ConfirmedLuck(); } catch { try { luckBefore = before.BaseLuck; } catch { } }
                try { luckAfter = after.ConfirmedLuck(); } catch { try { luckAfter = after.BaseLuck; } catch { } }
                if (luckAfter > luckBefore) gains.Add(string.Format(T("Luck +{0}"), luckAfter - luckBefore));

                // Accuracy (BaseAccuracyRate / ConfirmedAccuracyRate(false))
                int accBefore = 0, accAfter = 0;
                try { accBefore = before.ConfirmedAccuracyRate(false); } catch { try { accBefore = before.BaseAccuracyRate; } catch { } }
                try { accAfter = after.ConfirmedAccuracyRate(false); } catch { try { accAfter = after.BaseAccuracyRate; } catch { } }
                if (accAfter > accBefore) gains.Add(string.Format(T("Accuracy +{0}"), accAfter - accBefore));

                // Evasion (BaseEvasionRate / ConfirmedEvasionRate(false))
                int evaBefore = 0, evaAfter = 0;
                try { evaBefore = before.ConfirmedEvasionRate(false); } catch { try { evaBefore = before.BaseEvasionRate; } catch { } }
                try { evaAfter = after.ConfirmedEvasionRate(false); } catch { try { evaAfter = after.BaseEvasionRate; } catch { } }
                if (evaAfter > evaBefore) gains.Add(string.Format(T("Evasion +{0}"), evaAfter - evaBefore));

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
            BattleCommandPatches.ResetState();
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
            // Hard stop any tone left over from a previous, abnormally-ended result screen.
            StopExpCounterIfPlaying();
        }
    }
}
