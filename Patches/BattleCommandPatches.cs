using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

// FF1 Battle types - KeyInput namespace
using BattleCommandSelectController = Il2CppLast.UI.KeyInput.BattleCommandSelectController;
using BattleTargetSelectController = Il2CppLast.UI.KeyInput.BattleTargetSelectController;
using BattleCommandSelectContentController = Il2CppLast.UI.KeyInput.BattleCommandSelectContentController;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using BattleEnemyData = Il2CppLast.Battle.BattleEnemyData;
using MessageManager = Il2CppLast.Management.MessageManager;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Battle command and target selection patches.
    /// Announces character turns, commands, and targets.
    /// </summary>
    public static class BattleCommandPatches
    {
        private static int lastCharacterId = -1;

        // Command-announce window. Opened by the SetCommandData postfix ("X's turn"); closed by the
        // SetCommandData prefix AND by ShowWindow(false) (the actor's commit/teardown — where the
        // spurious "Attack" bursts fire). SetCursor only announces while this is true.
        private static bool commandTurnReady;

        // Message id of the last command actually announced, for dedup. Keyed on the command IDENTITY,
        // not the cursor index: the command menu has two pages (Normal: Attack/Magic/Item; Extra:
        // Defend/Flee) and left/right switches page while the index stays the same — an index-based
        // dedup would wrongly suppress those switches. The burst repeats the same command id. Reset
        // each turn so the first command always announces.
        private static string lastAnnouncedCmdMesId;

        // Cached reference to avoid FindObjectOfType on every call
        private static BattleTargetSelectController cachedTargetController = null;

        /// <summary>
        /// Apply battle command patches.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch SetCommandData for turn announcements
                PatchSetCommandData(harmony);

                // Patch SetCursor for command selection
                PatchSetCursor(harmony);

                // Patch target selection
                PatchTargetSelection(harmony);

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
                            break;
                        }
                    }
                }

                if (method != null)
                {
                    var prefix = typeof(BattleCommandPatches).GetMethod(
                        nameof(SetCommandData_Prefix),
                        BindingFlags.Public | BindingFlags.Static
                    );
                    var postfix = typeof(BattleCommandPatches).GetMethod(
                        nameof(SetCommandData_Postfix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(method, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
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
                                patchedPlayer = true;
                            }
                        }
                    }
                }

                if (!patchedEnemy && !patchedPlayer)
                {
                    MelonLogger.Warning("[Battle Target] No SelectContent methods found - cursor-move target announcements unavailable");
                }

                // Patch ShowWindow: tracks show/hide and resets the per-session flags.
                PatchShowWindow(harmony, controllerType);

                // EnemysUpdate/PlayerUpdate: the state-machine update for each single-target state, which
                // fires for EVERY single-target open (including plain Attack, which never calls ShowWindow
                // or SetActiveCursor) and identifies the side directly. Reads the initial focus once.
                PatchTargetMethod(harmony, controllerType, "EnemysUpdate", nameof(EnemysUpdate_Postfix));
                PatchTargetMethod(harmony, controllerType, "PlayerUpdate", nameof(PlayerUpdate_Postfix));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Command] Error patching target selection: {ex.Message}");
            }
        }

        /// <summary>Patches a named method on the target controller with the given postfix (best-effort).</summary>
        private static void PatchTargetMethod(HarmonyLib.Harmony harmony, Type controllerType, string methodName, string postfixName)
        {
            try
            {
                var method = AccessTools.Method(controllerType, methodName);
                if (method == null)
                {
                    MelonLogger.Warning($"[Battle Target] {methodName} not found");
                    return;
                }
                var postfix = typeof(BattleCommandPatches).GetMethod(postfixName, BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Target] Error patching {methodName}: {ex.Message}");
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
        /// Prefix for ShowWindow - tracks show/hide and bounds a target session (re-arms the one-shot
        /// initial-focus reader). The read itself is driven by EnemysUpdate/PlayerUpdate, which fire for
        /// Attack too — ShowWindow does not.
        /// </summary>
        public static void ShowWindow_Prefix(object __instance, bool isShow)
        {
            try
            {
                BattleTargetState.SetTargetSelectionActive(isShow);
                MelonLogger.Msg($"[DIAG tgt] ShowWindow isShow={isShow}");

                if (isShow)
                {
                    // Open = a new target session — re-arm the one-shot initial-focus reader.
                    defaultTargetAnnounced = false;
                    targetReadDiagLogged = false;
                }
                else
                {
                    // Hide = the actor committed / target torn down. Close the command-announce window
                    // (suppresses the handoff "Attack" bursts). Do NOT re-arm the initial-focus reader on
                    // close: re-arming there lets EnemysUpdate re-announce the target after the window
                    // closed (the stray "Goblin A" re-read seen in the logs).
                    commandTurnReady = false;
                    cachedTargetController = null;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Target] Error in ShowWindow_Prefix: {ex.Message}");
            }
        }

        // One-shot guard: the per-frame Update trigger reads the initial focus once per target session
        // (re-armed by ShowWindow open/close and each SetCommandData). Set true after a successful read.
        private static bool defaultTargetAnnounced;

        // DIAG one-shot: logs the first ReadInitialTarget call per session (even on failure) so an Attack
        // target reveals whether EnemysUpdate fires and what it reads. Reset with defaultTargetAnnounced.
        private static bool targetReadDiagLogged;

        /// <summary>Postfix for EnemysUpdate - announces the initially-focused enemy target on open.</summary>
        public static void EnemysUpdate_Postfix(object __instance) => ReadInitialTarget(__instance, isEnemy: true);

        /// <summary>Postfix for PlayerUpdate - announces the initially-focused ally target on open.</summary>
        public static void PlayerUpdate_Postfix(object __instance) => ReadInitialTarget(__instance, isEnemy: false);

        /// <summary>
        /// Reads the focused target once when a single-target state opens. EnemysUpdate/PlayerUpdate run
        /// every frame while in their state and identify the side directly, so this needs no state read and
        /// self-retries (returns without arming the flag) until the cursor + list are ready. After the read
        /// the per-frame call is a single bool check. All-target states use other Update methods, so they
        /// never reach here.
        /// </summary>
        private static void ReadInitialTarget(object instance, bool isEnemy)
        {
            try
            {
                var controller = instance as BattleTargetSelectController;

                // DIAG (once per session): log the first Update read regardless of outcome, so an Attack
                // target shows whether EnemysUpdate fires + why it might be silent.
                if (!targetReadDiagLogged)
                {
                    targetReadDiagLogged = true;
                    try
                    {
                        bool dact = controller != null && controller.gameObject != null && controller.gameObject.activeInHierarchy;
                        IntPtr dptr = controller != null ? controller.Pointer : IntPtr.Zero;
                        int didx = -1, e38 = -1, e90 = -1, p30 = -1, p88 = -1;
                        if (dptr != IntPtr.Zero)
                        {
                            IntPtr dc = IL2CppFieldReader.ReadPointer(dptr, IL2CppOffsets.BattleTarget.SelectCursor);
                            if (dc != IntPtr.Zero) didx = new GameCursor(dc).Index;
                            var a = ReadEnemyList(dptr, IL2CppOffsets.BattleTarget.EnemyDataList); e38 = a != null ? a.Count : -1;
                            var b = ReadEnemyList(dptr, IL2CppOffsets.BattleTarget.TargetEnamyList); e90 = b != null ? b.Count : -1;
                            var c = ReadPlayerList(dptr, IL2CppOffsets.BattleTarget.PlayerDataList); p30 = c != null ? c.Count : -1;
                            var d = ReadPlayerList(dptr, IL2CppOffsets.BattleTarget.TargetPlayerList); p88 = d != null ? d.Count : -1;
                        }
                        MelonLogger.Msg($"[DIAG tgt] update kind={(isEnemy ? "enemy" : "player")} announced={defaultTargetAnnounced} active={dact} idx={didx} e38={e38} e90={e90} p30={p30} p88={p88}");
                    }
                    catch (Exception dex) { MelonLogger.Warning($"[DIAG tgt] update log error: {dex.Message}"); }
                }

                if (defaultTargetAnnounced) return;
                if (controller == null || controller.gameObject == null)
                    return;

                // Attack's targeting path presents an INACTIVE controller (magic/item present an active
                // one), so do NOT require activeInHierarchy — these Update methods only run while genuinely
                // targeting, and the list/cursor validity below gates the announce. If THIS controller is
                // inactive, prefer an active instance (in case it's a stale leftover) and fall back to it.
                if (!controller.gameObject.activeInHierarchy)
                {
                    var active = UnityEngine.Object.FindObjectOfType<BattleTargetSelectController>();
                    if (active != null && active.gameObject != null && active.gameObject.activeInHierarchy)
                        controller = active;
                }

                IntPtr ptr = controller.Pointer;
                if (ptr == IntPtr.Zero) return;

                IntPtr cursorPtr = IL2CppFieldReader.ReadPointer(ptr, IL2CppOffsets.BattleTarget.SelectCursor);
                if (cursorPtr == IntPtr.Zero) return;
                int index = new GameCursor(cursorPtr).Index;
                if (index < 0) return;

                if (isEnemy)
                {
                    var list = ReadEnemyList(ptr, IL2CppOffsets.BattleTarget.EnemyDataList);   // 0x38
                    if (list == null || list.Count == 0) list = ReadEnemyList(ptr, IL2CppOffsets.BattleTarget.TargetEnamyList); // 0x90
                    if (list == null || index >= list.Count) return; // not ready — next Update frame retries
                    defaultTargetAnnounced = true;
                    MelonLogger.Msg($"[DIAG tgt] initial kind=enemy idx={index} n={list.Count}");
                    AnnounceEnemyTarget(list, index);
                }
                else
                {
                    var list = ReadPlayerList(ptr, IL2CppOffsets.BattleTarget.PlayerDataList);  // 0x30
                    if (list == null || list.Count == 0) list = ReadPlayerList(ptr, IL2CppOffsets.BattleTarget.TargetPlayerList); // 0x88
                    if (list == null || index >= list.Count) return; // not ready — next Update frame retries
                    defaultTargetAnnounced = true;
                    MelonLogger.Msg($"[DIAG tgt] initial kind=player idx={index} n={list.Count}");
                    AnnouncePlayerTarget(list, index);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Target] Error reading initial target: {ex.Message}");
            }
        }

        /// <summary>Reads a target-list field as List&lt;BattleEnemyData&gt; (null if absent / not a List).</summary>
        private static Il2CppSystem.Collections.Generic.List<BattleEnemyData> ReadEnemyList(IntPtr ctrlPtr, int offset)
        {
            IntPtr p = IL2CppFieldReader.ReadPointer(ctrlPtr, offset);
            return p != IntPtr.Zero ? new Il2CppSystem.Object(p).TryCast<Il2CppSystem.Collections.Generic.List<BattleEnemyData>>() : null;
        }

        /// <summary>Reads a target-list field as List&lt;BattlePlayerData&gt; (null if absent / not a List).</summary>
        private static Il2CppSystem.Collections.Generic.List<BattlePlayerData> ReadPlayerList(IntPtr ctrlPtr, int offset)
        {
            IntPtr p = IL2CppFieldReader.ReadPointer(ctrlPtr, offset);
            return p != IntPtr.Zero ? new Il2CppSystem.Object(p).TryCast<Il2CppSystem.Collections.Generic.List<BattlePlayerData>>() : null;
        }

        /// <summary>
        /// Prefix for SetCommandData - closes the command-announce window before the body runs, so the
        /// cursor resets it fires during the actor handoff are suppressed by SetCursor_Postfix.
        /// </summary>
        public static void SetCommandData_Prefix()
        {
            commandTurnReady = false;
            lastAnnouncedCmdMesId = null;
            MelonLogger.Msg("[DIAG cmd] SetCommandData ENTER");
        }

        /// <summary>
        /// Postfix for SetCommandData - announces character turn and opens the command-announce window.
        /// </summary>
        public static void SetCommandData_Postfix(BattleCommandSelectController __instance, OwnedCharacterData data)
        {
            try
            {
                if (data == null) return;

                // Open the window before any early-return below: a same-character re-entry (e.g. after
                // canceling target back to the command menu) is still that actor's input turn.
                commandTurnReady = true;
                // Re-arm the one-shot initial-target reader so this turn's target announces.
                defaultTargetAnnounced = false;
                targetReadDiagLogged = false;
                MelonLogger.Msg("[DIAG cmd] SetCommandData EXIT");

                int characterId = data.Id;
                // Always track the current actor (used by AnnounceCharacterStatus / H key),
                // even when the character ID matches the last announced turn — re-entry
                // into SetCommandData for the same character still means they are the
                // currently active actor.
                BattleCommandState.CurrentActor = data;

                if (characterId == lastCharacterId) return;
                lastCharacterId = characterId;

                string characterName = data.Name;
                if (string.IsNullOrEmpty(characterName)) return;

                // Set battle command state active and clear other menu states
                BattleCommandState.IsActive = true;
                BattleCommandState.LastSelectedCommandIndex = -1;
                BattleMagicMenuState.IsActive = false;
                BattleItemMenuState.IsActive = false;

                string announcement = $"{characterName}'s turn";
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

                // The cursor resets to index 0 (Attack) one frame before the command
                // menu's gameObject goes inactive at end-of-turn. Without this guard we
                // speak "Attack" in that window every turn transition.
                if (!__instance.gameObject.activeInHierarchy) return;

                // Turn-window gate: only announce between a turn's "X's turn" (SetCommandData postfix)
                // and the next SetCommandData prefix. The handoff resets fire before the next turn's
                // postfix (commandTurnReady=false) and are suppressed; the real command + navigation
                // happen while it is true. (State-machine gating was proven useless — bursts run in
                // the same Normal state as real input.)
                int commandState = StateMachineHelper.ReadState(__instance.Pointer, IL2CppOffsets.BattleCommand.StateMachine);
                MelonLogger.Msg($"[DIAG cmd] idx={index} ready={commandTurnReady} last={lastAnnouncedCmdMesId} state={commandState}");
                if (!commandTurnReady)
                    return;

                // Set battle command state active and clear other menu states
                BattleCommandState.IsActive = true;
                BattleCommandState.LastSelectedCommandIndex = index;
                BattleMagicMenuState.IsActive = false;
                BattleItemMenuState.IsActive = false;

                // Actively check target selection state (more reliable than just reading the flag)
                bool targetActive = CheckAndUpdateTargetSelectionActive();

                // SUPPRESSION: If targeting is active, do not announce commands
                if (targetActive)
                {
                    return;
                }

                AnnounceCommandAt(__instance, GetCommandContentList(__instance), index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Command] Error in SetCursor_Postfix: {ex.Message}");
            }
        }

        /// <summary>Gets the command controller's content list (typed property, reflection fallback).</summary>
        private static Il2CppSystem.Collections.Generic.List<BattleCommandSelectContentController> GetCommandContentList(BattleCommandSelectController controller)
        {
            try { return controller.contentList; }
            catch
            {
                try
                {
                    var field = controller.GetType().GetField("contentList", BindingFlags.NonPublic | BindingFlags.Instance);
                    return field != null ? field.GetValue(controller) as Il2CppSystem.Collections.Generic.List<BattleCommandSelectContentController> : null;
                }
                catch { return null; }
            }
        }

        /// <summary>
        /// Announces the command at contentList[index] (name + active-slot "(X of Y)"), deduped by command
        /// id via lastAnnouncedCmdMesId. Shared by SetCursor (navigation) and the focus reader (re-appear).
        /// </summary>
        private static void AnnounceCommandAt(BattleCommandSelectController controller,
            Il2CppSystem.Collections.Generic.List<BattleCommandSelectContentController> contentList, int index)
        {
            if (contentList == null || contentList.Count == 0) return;
            if (index < 0 || index >= contentList.Count) return;

            var contentController = contentList[index];
            if (contentController == null) return;
            var targetCommand = contentController.TargetCommand;
            if (targetCommand == null) return;
            string mesIdName = targetCommand.MesIdName;
            if (string.IsNullOrWhiteSpace(mesIdName)) return;

            // Dedup on command identity (a page switch keeps the index but changes the id, so it announces).
            if (mesIdName == lastAnnouncedCmdMesId) return;

            var messageManager = MessageManager.Instance;
            if (messageManager == null) return;
            string commandName = messageManager.GetMessage(mesIdName);
            if (string.IsNullOrWhiteSpace(commandName)) return;
            commandName = TextUtils.StripIconMarkup(commandName);

            // Count active commands on the current page for "(X of Y)" (contentList is a fixed slot list;
            // count only populated AND active slots so a stale Extra-page leftover doesn't inflate it).
            int visibleCount = 0;
            for (int i = 0; i < contentList.Count; i++)
            {
                try
                {
                    var cc = contentList[i];
                    if (cc == null) continue;
                    if (cc.TargetCommand != null && cc.gameObject != null && cc.gameObject.activeInHierarchy)
                        visibleCount++;
                }
                catch { }
            }
            if (visibleCount <= 0) visibleCount = contentList.Count;

            commandName = MenuPosition.Format(commandName, index, visibleCount);
            lastAnnouncedCmdMesId = mesIdName;
            // Command selection doesn't interrupt - queues after turn announcement
            FFI_ScreenReaderMod.SpeakText(commandName, interrupt: false);
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
                // Set target selection active
                BattleTargetState.SetTargetSelectionActive(true);

                // Use TryCast to convert IL2CPP IEnumerable to List
                var playerList = list.TryCast<Il2CppSystem.Collections.Generic.List<BattlePlayerData>>();
                LogSelectContentFields("player", __instance, list.Pointer, index);
                AnnouncePlayerTarget(playerList, index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Target] Error in SelectContent_Player_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces an ally target by list + index. Shared by the SelectContent cursor-move
        /// postfix and the on-open initial-focus reader (PlayerInit).
        /// </summary>
        public static void AnnouncePlayerTarget(
            Il2CppSystem.Collections.Generic.List<BattlePlayerData> playerList, int index)
        {
            if (playerList == null || playerList.Count == 0)
                return;

            if (index < 0 || index >= playerList.Count) return;

            var selectedPlayer = playerList[index];
            if (selectedPlayer == null) return;

            {
                string name = T("Unknown");
                int currentHp = 0, maxHp = 0;

                var ownedCharData = selectedPlayer.ownedCharacterData;
                if (ownedCharData != null)
                {
                    name = ownedCharData.Name ?? T("Unknown");
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

                string announcement = string.Format(T("{0}: HP {1}/{2}"), name, currentHp, maxHp);

                string statusSuffix = BuildStatusSuffix(battleInfo?.Parameter);
                if (!string.IsNullOrEmpty(statusSuffix))
                    announcement += statusSuffix;

                announcement = MenuPosition.Format(announcement, index, playerList.Count);

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
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
                // Set target selection active
                BattleTargetState.SetTargetSelectionActive(true);

                // Use TryCast to convert IL2CPP IEnumerable to List
                var enemyList = list.TryCast<Il2CppSystem.Collections.Generic.List<BattleEnemyData>>();
                LogSelectContentFields("enemy", __instance, list.Pointer, index);
                AnnounceEnemyTarget(enemyList, index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Target] Error in SelectContent_Enemy_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces an enemy target by list + index. Shared by the SelectContent cursor-move
        /// postfix and the on-open initial-focus reader (EnemysInit).
        /// </summary>
        public static void AnnounceEnemyTarget(
            Il2CppSystem.Collections.Generic.List<BattleEnemyData> enemyList, int index)
        {
            if (enemyList == null || enemyList.Count == 0)
                return;

            if (index < 0 || index >= enemyList.Count) return;

            var selectedEnemy = enemyList[index];
            if (selectedEnemy == null) return;

            {
                string name = T("Unknown");
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
                int hpMode = PreferencesManager.EnemyHPDisplay;
                switch (hpMode)
                {
                    case 0: // Numbers (default)
                        announcement += string.Format(T(": HP {0}/{1}"), currentHp, maxHp);
                        break;
                    case 1: // Percentage
                        int pct = maxHp > 0 ? (currentHp * 100 / maxHp) : 0;
                        announcement += $": {pct}%";
                        break;
                    case 2: // Hidden
                        // No HP appended
                        break;
                }

                string statusSuffix = BuildStatusSuffix(battleInfo?.Parameter);
                if (!string.IsNullOrEmpty(statusSuffix))
                    announcement += statusSuffix;

                announcement = MenuPosition.Format(announcement, index, enemyList.Count);

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
        }

        /// <summary>
        /// DIAG: logs the authoritative SelectContent list pointer alongside the controller's candidate
        /// list fields (0x30/0x38/0x90) + the cursor index, so one cursor move reveals exactly which field
        /// holds the List the game indexes and confirms selectCursor=0xC0.
        /// </summary>
        private static void LogSelectContentFields(string kind, object instance, IntPtr listPtr, int index)
        {
            try
            {
                var ctrl = instance as BattleTargetSelectController;
                if (ctrl == null) return;
                IntPtr c = ctrl.Pointer;
                IntPtr cur = IL2CppFieldReader.ReadPointer(c, IL2CppOffsets.BattleTarget.SelectCursor);
                int curIdx = cur != IntPtr.Zero ? new GameCursor(cur).Index : -1;
                MelonLogger.Msg($"[DIAG sc] kind={kind} idx={index} listPtr={listPtr.ToInt64():X} "
                    + $"f30={IL2CppFieldReader.ReadPointer(c, 0x30).ToInt64():X} "
                    + $"f38={IL2CppFieldReader.ReadPointer(c, 0x38).ToInt64():X} "
                    + $"f90={IL2CppFieldReader.ReadPointer(c, 0x90).ToInt64():X} cur={curIdx}");
            }
            catch { }
        }

        /// <summary>
        /// Build ", Poison, Sleep" suffix from a battle unit's CurrentConditionList.
        /// Returns empty string when no conditions or parameter is null.
        /// </summary>
        private static string BuildStatusSuffix(Il2CppLast.Data.CharacterParameterBase parameter)
        {
            if (parameter == null) return string.Empty;
            try
            {
                var conditionList = parameter.CurrentConditionList;
                if (conditionList == null || conditionList.Count == 0) return string.Empty;

                var names = new System.Collections.Generic.List<string>();
                foreach (var condition in conditionList)
                {
                    string n = MagicMenuState.GetConditionName(condition);
                    if (!string.IsNullOrWhiteSpace(n)) names.Add(n);
                }
                if (names.Count == 0) return string.Empty;
                return ", " + string.Join(", ", names);
            }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Reset all state (call at battle end).
        /// </summary>
        public static void ResetState()
        {
            lastCharacterId = -1;
            commandTurnReady = false;
            lastAnnouncedCmdMesId = null;
            defaultTargetAnnounced = false;
            targetReadDiagLogged = false;
            BattleCommandState.CurrentActor = null;
        }
    }
}
