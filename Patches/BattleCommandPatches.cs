using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

// FF1 Battle types - KeyInput namespace
using BattleCommandSelectController = Il2CppLast.UI.KeyInput.BattleCommandSelectController;
using BattleTargetSelectController = Il2CppLast.UI.KeyInput.BattleTargetSelectController;
using BattleCommandSelectContentController = Il2CppLast.UI.KeyInput.BattleCommandSelectContentController;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using BattleEnemyData = Il2CppLast.Battle.BattleEnemyData;
using MessageManager = Il2CppLast.Management.MessageManager;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Battle command and target selection patches.
    /// Announces character turns, commands, and targets.
    /// </summary>
    public static class BattleCommandPatches
    {
        private static int lastCharacterId = -1;

        // Cached reference to avoid FindObjectOfType on every call
        private static BattleTargetSelectController cachedTargetController = null;

        /// <summary>
        /// Apply battle command patches.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[Battle Command] Applying battle command patches...");

                // Patch SetCommandData for turn announcements
                PatchSetCommandData(harmony);

                // Patch SetCursor for command selection
                PatchSetCursor(harmony);

                // Patch target selection
                PatchTargetSelection(harmony);

                MelonLogger.Msg("[Battle Command] Battle command patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Battle Command] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch SetCommandData for turn announcements.
        /// </summary>
        private static void PatchSetCommandData(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(BattleCommandSelectController);
                MethodInfo method = null;

                // Find SetCommandData by iterating methods
                var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                foreach (var m in methods)
                {
                    if (m.Name == "SetCommandData")
                    {
                        var parameters = m.GetParameters();
                        // Looking for SetCommandData(OwnedCharacterData, IEnumerable, IEnumerable)
                        if (parameters.Length == 3 && parameters[0].ParameterType.Name == "OwnedCharacterData")
                        {
                            method = m;
                            MelonLogger.Msg($"[Battle Command] Found SetCommandData with OwnedCharacterData parameter");
                            break;
                        }
                    }
                }

                if (method != null)
                {
                    var postfix = typeof(BattleCommandPatches).GetMethod(
                        nameof(SetCommandData_Postfix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Battle Command] Patched SetCommandData successfully");
                }
                else
                {
                    MelonLogger.Warning("[Battle Command] SetCommandData method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Command] Error patching SetCommandData: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch SetCursor for command navigation.
        /// </summary>
        private static void PatchSetCursor(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(BattleCommandSelectController);

                // Debug: Log all methods on this type
                MelonLogger.Msg($"[Battle Command] Dumping methods on {controllerType.Name}:");
                var allMethods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var m in allMethods)
                {
                    var parms = string.Join(", ", Array.ConvertAll(m.GetParameters(), p => p.ParameterType.Name));
                    MelonLogger.Msg($"[Battle Command]   - {m.Name}({parms})");
                }

                // Try AccessTools first (Harmony's method finder)
                var method = AccessTools.Method(controllerType, "SetCursor", new Type[] { typeof(int) });

                if (method == null)
                {
                    // Try without parameter specification
                    method = AccessTools.Method(controllerType, "SetCursor");
                }

                if (method != null)
                {
                    var postfix = typeof(BattleCommandPatches).GetMethod(
                        nameof(SetCursor_Postfix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Battle Command] Patched SetCursor successfully");
                }
                else
                {
                    MelonLogger.Warning("[Battle Command] SetCursor method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Command] Error patching SetCursor: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch target selection methods - patches SelectContent directly like FF3.
        /// </summary>
        private static void PatchTargetSelection(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(BattleTargetSelectController);
                bool patchedEnemy = false;
                bool patchedPlayer = false;

                // Find SelectContent methods by iterating (IL2CPP needs this approach)
                var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                foreach (var m in methods)
                {
                    if (m.Name == "SelectContent")
                    {
                        var parameters = m.GetParameters();
                        if (parameters.Length >= 2 && parameters[1].ParameterType == typeof(int))
                        {
                            // Check if it's the enemy or player version based on first parameter
                            var firstParamType = parameters[0].ParameterType;
                            string fullTypeName = firstParamType.FullName ?? "";
                            string typeString = firstParamType.ToString();

                            // For generic types, check the generic arguments
                            string genericArgName = "";
                            if (firstParamType.IsGenericType)
                            {
                                var genericArgs = firstParamType.GetGenericArguments();
                                if (genericArgs.Length > 0)
                                {
                                    genericArgName = genericArgs[0].Name;
                                }
                            }

                            MelonLogger.Msg($"[Battle Target] Found SelectContent - FullName: {fullTypeName}, ToString: {typeString}, GenericArg: {genericArgName}");

                            // Check using generic argument name or type string
                            bool isEnemy = genericArgName.Contains("BattleEnemyData") || typeString.Contains("BattleEnemyData") || fullTypeName.Contains("BattleEnemyData");
                            bool isPlayer = genericArgName.Contains("BattlePlayerData") || typeString.Contains("BattlePlayerData") || fullTypeName.Contains("BattlePlayerData");

                            if (isEnemy && !patchedEnemy)
                            {
                                // Patch enemy version
                                var postfix = typeof(BattleCommandPatches).GetMethod(
                                    nameof(SelectContent_Enemy_Postfix),
                                    BindingFlags.Public | BindingFlags.Static
                                );
                                harmony.Patch(m, postfix: new HarmonyMethod(postfix));
                                MelonLogger.Msg("[Battle Target] Patched SelectContent(Enemy) successfully");
                                patchedEnemy = true;
                            }
                            else if (isPlayer && !patchedPlayer)
                            {
                                // Patch player version
                                var postfix = typeof(BattleCommandPatches).GetMethod(
                                    nameof(SelectContent_Player_Postfix),
                                    BindingFlags.Public | BindingFlags.Static
                                );
                                harmony.Patch(m, postfix: new HarmonyMethod(postfix));
                                MelonLogger.Msg("[Battle Target] Patched SelectContent(Player) successfully");
                                patchedPlayer = true;
                            }
                        }
                    }
                }

                if (!patchedEnemy && !patchedPlayer)
                {
                    MelonLogger.Warning("[Battle Target] No SelectContent methods found, trying state machine methods...");

                    // Fallback to state machine methods
                    var enemysInitMethod = AccessTools.Method(controllerType, "EnemysInit");
                    if (enemysInitMethod != null)
                    {
                        var postfix = typeof(BattleCommandPatches).GetMethod(
                            nameof(EnemysInit_Postfix),
                            BindingFlags.Public | BindingFlags.Static
                        );
                        harmony.Patch(enemysInitMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("[Battle Target] Patched EnemysInit as fallback");
                    }
                }
                else
                {
                    MelonLogger.Msg($"[Battle Target] Patched: Enemy={patchedEnemy}, Player={patchedPlayer}");
                }

                // Patch ShowWindow to track when target selection window is shown/hidden
                PatchShowWindow(harmony, controllerType);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Command] Error patching target selection: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches ShowWindow to track when target selection window is shown/hidden.
        /// This is critical for clearing IsTargetSelectionActive when backing out.
        /// </summary>
        private static void PatchShowWindow(HarmonyLib.Harmony harmony, Type controllerType)
        {
            try
            {
                var showWindowMethod = controllerType.GetMethod("ShowWindow",
                    BindingFlags.Public | BindingFlags.Instance);

                if (showWindowMethod != null)
                {
                    var prefix = typeof(BattleCommandPatches).GetMethod(
                        nameof(ShowWindow_Prefix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(showWindowMethod, prefix: new HarmonyMethod(prefix));
                    MelonLogger.Msg("[Battle Target] Patched ShowWindow successfully");
                }
                else
                {
                    MelonLogger.Warning("[Battle Target] ShowWindow method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Target] Error patching ShowWindow: {ex.Message}");
            }
        }

        /// <summary>
        /// Prefix for ShowWindow - tracks when target selection window is shown/hidden.
        /// When isShow=false, clears the target selection active state.
        /// </summary>
        public static void ShowWindow_Prefix(object __instance, bool isShow)
        {
            try
            {
                MelonLogger.Msg($"[Battle Target] ShowWindow Prefix: isShow={isShow}");
                BattleTargetState.SetTargetSelectionActive(isShow);

                if (!isShow)
                {
                    // Clear cached controller reference when hiding
                    cachedTargetController = null;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Target] Error in ShowWindow_Prefix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SetCommandData - announces character turn.
        /// </summary>
        public static void SetCommandData_Postfix(BattleCommandSelectController __instance, OwnedCharacterData data)
        {
            try
            {
                if (data == null) return;

                int characterId = data.Id;
                if (characterId == lastCharacterId) return;
                lastCharacterId = characterId;

                string characterName = data.Name;
                if (string.IsNullOrEmpty(characterName)) return;

                // Reset tracking for new turn
                ResetTargetTracking();
                AnnouncementDeduplicator.Reset("BattleCmd.Command");

                // Set battle command state active and clear other menu states
                BattleCommandState.IsActive = true;
                BattleCommandState.LastSelectedCommandIndex = -1;
                BattleMagicMenuState.IsActive = false;
                BattleItemMenuState.IsActive = false;

                string announcement = $"{characterName}'s turn";
                MelonLogger.Msg($"[Battle Turn] {announcement}");
                // Turn announcements interrupt
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Command] Error in SetCommandData_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if target selection is actually active by looking at the controller's gameObject.
        /// This is more reliable than relying on ShowWindow being called.
        /// Optimized to skip expensive checks when flag is already false.
        /// </summary>
        public static bool CheckAndUpdateTargetSelectionActive()
        {
            try
            {
                // Fast path: if flag is false, only do expensive check occasionally
                // The flag gets set to true by SelectContent patches, so we trust that
                if (!BattleTargetState.IsTargetSelectionActive)
                {
                    return false;
                }

                // Flag is true - verify it's still actually active
                // Try cached reference first
                if (cachedTargetController == null || cachedTargetController.gameObject == null)
                {
                    cachedTargetController = UnityEngine.Object.FindObjectOfType<BattleTargetSelectController>();
                }

                if (cachedTargetController == null)
                {
                    MelonLogger.Msg("[Battle Target] Controller not found, resetting flag to false");
                    BattleTargetState.SetTargetSelectionActive(false);
                    return false;
                }

                // Check if the controller has active children (view is shown)
                bool isActuallyActive = false;
                var children = cachedTargetController.GetComponentsInChildren<UnityEngine.Transform>(false);
                foreach (var child in children)
                {
                    if (child != null && child.gameObject != cachedTargetController.gameObject)
                    {
                        isActuallyActive = true;
                        break;
                    }
                }

                if (!isActuallyActive)
                {
                    MelonLogger.Msg($"[Battle Target] State mismatch detected: flag=True, actual=False");
                    BattleTargetState.SetTargetSelectionActive(false);
                }

                return BattleTargetState.IsTargetSelectionActive;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Target] Error checking target selection state: {ex.Message}");
                return BattleTargetState.IsTargetSelectionActive;
            }
        }

        /// <summary>
        /// Clears the cached target controller reference.
        /// </summary>
        public static void ClearCachedTargetController()
        {
            cachedTargetController = null;
        }

        /// <summary>
        /// Postfix for SetCursor - announces selected command.
        /// </summary>
        public static void SetCursor_Postfix(BattleCommandSelectController __instance, int index)
        {
            try
            {
                if (__instance == null) return;

                // Set battle command state active and clear other menu states
                BattleCommandState.IsActive = true;
                BattleCommandState.LastSelectedCommandIndex = index;
                BattleMagicMenuState.IsActive = false;
                BattleItemMenuState.IsActive = false;

                // Actively check target selection state (more reliable than just reading the flag)
                bool targetActive = CheckAndUpdateTargetSelectionActive();

                // Log EVERY SetCursor call to understand what's happening
                MelonLogger.Msg($"[Battle Command] SetCursor called: index={index}, TargetActive={targetActive}");

                // SUPPRESSION: If targeting is active, do not announce commands
                if (targetActive)
                {
                    MelonLogger.Msg($"[Battle Command] SUPPRESSED - target selection active");
                    return;
                }

                // Use central deduplicator - skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce("BattleCmd.Command", index))
                {
                    MelonLogger.Msg($"[Battle Command] SUPPRESSED - duplicate index");
                    return;
                }

                // Try direct property access first (IL2CPP exposes private fields as properties)
                Il2CppSystem.Collections.Generic.List<BattleCommandSelectContentController> contentList = null;
                try
                {
                    contentList = __instance.contentList;
                    MelonLogger.Msg($"[Battle Command] Direct contentList access: {(contentList != null ? contentList.Count.ToString() + " items" : "null")}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"[Battle Command] Direct access failed: {ex.Message}, trying reflection...");

                    // Fallback to reflection (contentList at offset 0x50 for KeyInput)
                    try
                    {
                        var field = __instance.GetType().GetField("contentList",
                            BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field != null)
                        {
                            contentList = field.GetValue(__instance) as Il2CppSystem.Collections.Generic.List<BattleCommandSelectContentController>;
                            MelonLogger.Msg($"[Battle Command] Reflection contentList: {(contentList != null ? contentList.Count.ToString() + " items" : "null")}");
                        }
                        else
                        {
                            MelonLogger.Msg("[Battle Command] contentList field not found via reflection");
                        }
                    }
                    catch (Exception ex2)
                    {
                        MelonLogger.Warning($"[Battle Command] Reflection also failed: {ex2.Message}");
                    }
                }

                if (contentList == null || contentList.Count == 0)
                {
                    MelonLogger.Msg("[Battle Command] contentList is null or empty, cannot announce");
                    return;
                }
                if (index < 0 || index >= contentList.Count)
                {
                    MelonLogger.Msg($"[Battle Command] index {index} out of range (count={contentList.Count})");
                    return;
                }

                var contentController = contentList[index];
                if (contentController == null) return;

                // Get TargetCommand property
                var targetCommand = contentController.TargetCommand;
                if (targetCommand == null) return;

                string mesIdName = targetCommand.MesIdName;
                if (string.IsNullOrWhiteSpace(mesIdName)) return;

                var messageManager = MessageManager.Instance;
                if (messageManager == null) return;

                string commandName = messageManager.GetMessage(mesIdName);
                if (string.IsNullOrWhiteSpace(commandName)) return;

                commandName = TextUtils.StripIconMarkup(commandName);

                MelonLogger.Msg($"[Battle Command] {commandName}");
                // Command selection doesn't interrupt - queues after turn announcement
                FFI_ScreenReaderMod.SpeakText(commandName, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Command] Error in SetCursor_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SelectContent(Player) - announces player target.
        /// Uses IL2CPP TryCast for proper collection handling.
        /// </summary>
        public static void SelectContent_Player_Postfix(object __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<BattlePlayerData> list, int index)
        {
            try
            {
                MelonLogger.Msg($"[Battle Target] SelectContent(Player) called, index={index}");

                // Set target selection active
                BattleTargetState.SetTargetSelectionActive(true);

                // Use central deduplicator
                if (!AnnouncementDeduplicator.ShouldAnnounce("BattleCmd.Player", index)) return;
                AnnouncementDeduplicator.Reset("BattleCmd.Enemy");

                // Use TryCast to convert IL2CPP IEnumerable to List
                var playerList = list.TryCast<Il2CppSystem.Collections.Generic.List<BattlePlayerData>>();
                if (playerList == null || playerList.Count == 0)
                {
                    MelonLogger.Msg("[Battle Target] Could not cast player list");
                    return;
                }

                if (index < 0 || index >= playerList.Count) return;

                var selectedPlayer = playerList[index];
                if (selectedPlayer == null) return;

                string name = "Unknown";
                int currentHp = 0, maxHp = 0;

                var ownedCharData = selectedPlayer.ownedCharacterData;
                if (ownedCharData != null)
                {
                    name = ownedCharData.Name ?? "Unknown";
                    var charParam = ownedCharData.Parameter;
                    if (charParam != null)
                    {
                        try { maxHp = charParam.ConfirmedMaxHp(); } catch { }
                    }
                }

                var battleInfo = selectedPlayer.BattleUnitDataInfo;
                if (battleInfo?.Parameter != null)
                {
                    currentHp = battleInfo.Parameter.CurrentHP;
                    if (maxHp == 0)
                    {
                        try { maxHp = battleInfo.Parameter.ConfirmedMaxHp(); }
                        catch { maxHp = battleInfo.Parameter.BaseMaxHp; }
                    }
                }

                string announcement = $"{name}: HP {currentHp}/{maxHp}";
                MelonLogger.Msg($"[Battle Target] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Target] Error in SelectContent_Player_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SelectContent(Enemy) - announces enemy target.
        /// Uses IL2CPP TryCast for proper collection handling.
        /// </summary>
        public static void SelectContent_Enemy_Postfix(object __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<BattleEnemyData> list, int index)
        {
            try
            {
                MelonLogger.Msg($"[Battle Target] SelectContent(Enemy) called, index={index}");

                // Set target selection active
                BattleTargetState.SetTargetSelectionActive(true);

                // Use central deduplicator
                if (!AnnouncementDeduplicator.ShouldAnnounce("BattleCmd.Enemy", index)) return;
                AnnouncementDeduplicator.Reset("BattleCmd.Player");

                // Use TryCast to convert IL2CPP IEnumerable to List
                var enemyList = list.TryCast<Il2CppSystem.Collections.Generic.List<BattleEnemyData>>();
                if (enemyList == null || enemyList.Count == 0)
                {
                    MelonLogger.Msg("[Battle Target] Could not cast enemy list");
                    return;
                }

                if (index < 0 || index >= enemyList.Count) return;

                var selectedEnemy = enemyList[index];
                if (selectedEnemy == null) return;

                string name = "Unknown";
                int currentHp = 0, maxHp = 0;

                try
                {
                    string mesIdName = selectedEnemy.GetMesIdName();
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                    {
                        string localizedName = messageManager.GetMessage(mesIdName);
                        if (!string.IsNullOrEmpty(localizedName))
                        {
                            name = localizedName;
                        }
                    }
                }
                catch { }

                var battleInfo = selectedEnemy.BattleUnitDataInfo;
                if (battleInfo?.Parameter != null)
                {
                    currentHp = battleInfo.Parameter.CurrentHP;
                    try { maxHp = battleInfo.Parameter.ConfirmedMaxHp(); }
                    catch { maxHp = battleInfo.Parameter.BaseMaxHp; }
                }

                // Check for multiple enemies with same name
                string announcement = name;
                int sameNameCount = 0;
                int positionInGroup = 0;

                for (int i = 0; i < enemyList.Count; i++)
                {
                    var enemy = enemyList[i];
                    if (enemy != null)
                    {
                        try
                        {
                            string enemyMesId = enemy.GetMesIdName();
                            var messageManager = MessageManager.Instance;
                            if (!string.IsNullOrEmpty(enemyMesId) && messageManager != null)
                            {
                                string enemyName = messageManager.GetMessage(enemyMesId);
                                if (enemyName == name)
                                {
                                    sameNameCount++;
                                    if (i < index) positionInGroup++;
                                }
                            }
                        }
                        catch { }
                    }
                }

                if (sameNameCount > 1)
                {
                    char letter = (char)('A' + positionInGroup);
                    announcement += $" {letter}";
                }

                // Apply enemy HP display mode setting
                int hpMode = FFI_ScreenReaderMod.EnemyHPDisplay;
                switch (hpMode)
                {
                    case 0: // Numbers (default)
                        announcement += $": HP {currentHp}/{maxHp}";
                        break;
                    case 1: // Percentage
                        int pct = maxHp > 0 ? (currentHp * 100 / maxHp) : 0;
                        announcement += $": {pct}%";
                        break;
                    case 2: // Hidden
                        // No HP appended
                        break;
                }

                MelonLogger.Msg($"[Battle Target] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Target] Error in SelectContent_Enemy_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset target tracking indices.
        /// </summary>
        public static void ResetTargetTracking()
        {
            AnnouncementDeduplicator.Reset("BattleCmd.Player", "BattleCmd.Enemy");
        }

        /// <summary>
        /// Reset all state (call at battle end).
        /// </summary>
        public static void ResetState()
        {
            lastCharacterId = -1;
            AnnouncementDeduplicator.Reset("BattleCmd.Command", "BattleCmd.Player", "BattleCmd.Enemy");
        }

        /// <summary>
        /// Postfix for EnemysInit - fallback for initial enemy target.
        /// Only used if SelectContent patches fail.
        /// </summary>
        public static void EnemysInit_Postfix(BattleTargetSelectController __instance)
        {
            try
            {
                BattleTargetState.SetTargetSelectionActive(true);
                MelonLogger.Msg("[Battle Target] EnemysInit called (fallback)");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Target] Error in EnemysInit_Postfix: {ex.Message}");
            }
        }
    }
}
