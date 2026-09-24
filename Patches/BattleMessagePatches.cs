using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

// FF1 Battle types
using BattleActData = Il2CppLast.Battle.BattleActData;
using BattleUnitData = Il2CppLast.Battle.BattleUnitData;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using BattleEnemyData = Il2CppLast.Battle.BattleEnemyData;
using BattleBasicFunction = Il2CppLast.Battle.Function.BattleBasicFunction;
using BattleConditionController = Il2CppLast.Battle.BattleConditionController;
using BattleConditionFunction = Il2CppLast.Battle.BattleConditionFunction;
using Condition = Il2CppLast.Data.Master.Condition;
using HitType = Il2CppLast.Systems.HitType;
using MessageManager = Il2CppLast.Management.MessageManager;
using OwnedItemData = Il2CppLast.Data.User.OwnedItemData;
using Ability = Il2CppLast.Data.Master.Ability;
using ContentUtitlity = Il2CppLast.Systems.ContentUtitlity;
using BattleActExection = Il2CppLast.Battle.BattleActExection;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Battle message patches for action announcements, damage/healing, and status effects.
    /// </summary>
    public static class BattleMessagePatches
    {
        /// <summary>
        /// Apply manual patches for battle messages.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch InitActionState for actor/action announcements
                PatchInitActionState(harmony);

                // Patch CreateDamageView for damage/healing announcements
                PatchCreateDamageView(harmony);

                // BattleConditionController.Add is patched via declarative [HarmonyPatch] attribute
                // on BattleConditionController_Add_Patch class (at bottom of file)

                // Status removal ("X: Poison removed") — manual patch, see BattleConditionRemovalPatches.
                BattleConditionRemovalPatches.ApplyPatches(harmony);

                // Patch BattleCommandMessageController.SetMessage for system messages like "The party was defeated"
                PatchBattleCommandMessage(harmony);

                // Capture the multi-hit "×N" multiplier for the damage announce (Multi-hit Damage setting).
                PatchHitCount(harmony);

            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Battle Message] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix on Last.UI.DamageViewUIManager.CreateHitCount(int hitCountValue, ...) to capture the
        /// on-screen "×N" multi-hit multiplier so the damage announce can prepend it (e.g. "14x1552 damage").
        /// </summary>
        private static void PatchHitCount(HarmonyLib.Harmony harmony)
        {
            try
            {
                var type = FindType("Il2CppLast.UI.DamageViewUIManager");
                if (type == null)
                {
                    MelonLogger.Warning("[Battle Message] DamageViewUIManager type not found");
                    return;
                }

                MethodInfo method = null;
                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (m.Name == "CreateHitCount")
                    {
                        var ps = m.GetParameters();
                        if (ps.Length >= 1 && ps[0].ParameterType == typeof(int)) { method = m; break; }
                    }
                }
                if (method == null)
                {
                    MelonLogger.Warning("[Battle Message] CreateHitCount(int,...) not found");
                    return;
                }

                var postfix = typeof(BattleMessagePatches).GetMethod(
                    nameof(CreateHitCount_Postfix), BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error patching CreateHitCount: {ex.Message}");
            }
        }

        // Multi-hit multiplier captured from DamageViewUIManager.CreateHitCount, which fires just before
        // the matching CreateDamageView. Consumed (and reset to 1) by CreateDamageView_Postfix.
        private static int _pendingHitCount = 1;
        // Frame on which the hit count was captured. Used to reject a stale count (e.g. from an
        // evaded/unconsumed hit) so it can't leak into the next unrelated attack's announcement.
        private static int _pendingHitCountFrame = -1;

        /// <summary>Captures the hit-count multiplier (__0 = hitCountValue) for the next damage view.</summary>
        public static void CreateHitCount_Postfix(int __0)
        {
            _pendingHitCount = __0;
            _pendingHitCountFrame = UnityEngine.Time.frameCount;
        }

        /// <summary>
        /// Patch BattleActExection.InitActionState to announce actor and action.
        /// </summary>
        private static void PatchInitActionState(HarmonyLib.Harmony harmony)
        {
            try
            {
                var battleActExectionType = typeof(BattleActExection);

                // Use AccessTools for better IL2CPP compatibility
                var initActionStateMethod = AccessTools.Method(battleActExectionType, "InitActionState");

                if (initActionStateMethod == null)
                {
                    // Fallback to reflection with all binding flags
                    initActionStateMethod = battleActExectionType.GetMethod(
                        "InitActionState",
                        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public
                    );
                }

                if (initActionStateMethod != null)
                {
                    var postfix = typeof(BattleMessagePatches).GetMethod(
                        nameof(InitActionState_Postfix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(initActionStateMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    // Debug: dump available methods
                    MelonLogger.Warning("[Battle Message] InitActionState method not found, dumping methods:");
                    var methods = battleActExectionType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    foreach (var m in methods)
                    {
                        if (m.Name.Contains("Init") || m.Name.Contains("Action"))
                        {
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error patching InitActionState: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch BattleBasicFunction.CreateDamageView for damage and healing announcements.
        /// </summary>
        private static void PatchCreateDamageView(HarmonyLib.Harmony harmony)
        {
            try
            {
                var battleBasicFunctionType = typeof(BattleBasicFunction);

                // Find CreateDamageView(BattleUnitData, int, HitType, bool)
                var createDamageViewMethod = battleBasicFunctionType.GetMethod(
                    "CreateDamageView",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new Type[] { typeof(BattleUnitData), typeof(int), typeof(HitType), typeof(bool) },
                    null
                );

                if (createDamageViewMethod != null)
                {
                    var postfix = typeof(BattleMessagePatches).GetMethod(
                        nameof(CreateDamageView_Postfix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(createDamageViewMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Battle Message] CreateDamageView method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error patching CreateDamageView: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for InitActionState - announces actor and action.
        /// Format: "Actor attacks" or "Actor: Spell Name"
        /// </summary>
        public static void InitActionState_Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;

                var actExection = __instance as BattleActExection;
                if (actExection == null) return;

                // Get the current action data
                var actData = actExection.InStagingBattleActData;
                if (actData == null)
                {
                    return;
                }

                // Get actor name
                var actor = actData.AttackUnitData;
                string actorName = GetUnitName(actor);

                // Get action name
                string actionName = GetActionName(actData);

                // Consistent "Actor: Action" for EVERY action (basic attack included), matching the
                // command-menu wording: "Gordan: Attack", "Talerous: Defend", "Emma: Cure", "X: {item}".
                string announcement = string.IsNullOrEmpty(actionName)
                    ? actorName                       // fallback: actor only when the action name is unknown
                    : $"{actorName}: {actionName}";

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error in InitActionState_Postfix: {ex.Message}");
            }
        }

        // HitType enum values (from dump.cs)
        private const int HITTYPE_MISS = 2;
        private const int HITTYPE_ZERO = 3;          // a genuine 0-value result (see value-0 note below)
        private const int HITTYPE_RECOVERY = 4;      // HP recovery
        private const int HITTYPE_MP_HIT = 5;        // MP damage
        private const int HITTYPE_MP_RECOVERY = 6;   // MP recovery

        /// <summary>
        /// Postfix for CreateDamageView - announces damage or healing.
        /// Handles HP damage/recovery and MP damage/recovery.
        /// </summary>
        public static void CreateDamageView_Postfix(BattleUnitData data, int value, HitType hitType, bool isRecovery)
        {
            try
            {
                int hitTypeValue = (int)hitType;

                if (data == null) return;

                string targetName = GetUnitName(data);

                // Consume the multi-hit count captured by CreateHitCount (it fires just before this view).
                // Only honor it when captured on this frame (or the previous one) — an evaded or
                // otherwise unconsumed hit count must not leak into the next unrelated attack.
                // Reset to 1 so a later damage with no fresh hit count defaults to single.
                int hitCount = (UnityEngine.Time.frameCount - _pendingHitCountFrame <= 1) ? _pendingHitCount : 1;
                _pendingHitCount = 1;

                string message;
                if (hitTypeValue == HITTYPE_MISS)
                {
                    message = string.Format(T("{0}: Miss"), targetName);
                }
                else if (value == 0)
                {
                    // Value-0 views, settled offline from FF1's calc code (docs/debug.md, 2026-09-23):
                    //  - buff/debuff spells (AddConditionFunction -> CalcExecuteFF1.AddConditionExection)
                    //    emit value 0 with HitType Hit (or Miss when resisted); the condition itself is
                    //    announced by the BattleConditionController.Add postfix, so Hit stays silent here.
                    //    FF1 status cures (RecoveryConditionFunction) also emit value 0 + Hit; the cure
                    //    itself is announced by BattleConditionRemovalPatches ("X: Poison removed").
                    //  - HitType Zero comes only from a genuine 0 result (DamageAggregater.CheckUndead:
                    //    healing an undead target for 0; MagicAbsorptionFunction: 0 MP drained).
                    //  - HitType RecoveryCondition (7) is never produced by FF1's calc, so it has no branch.
                    if (hitTypeValue == HITTYPE_ZERO)
                        message = string.Format(T("{0}: {1} damage"), targetName, 0);
                    else
                        return;
                }
                else if (hitTypeValue == HITTYPE_MP_RECOVERY)
                {
                    // MP RECOVERY (Ether, Turbo Ether, etc.)
                    message = string.Format(T("{0}: Recovered {1} MP"), targetName, value);
                }
                else if (hitTypeValue == HITTYPE_MP_HIT)
                {
                    // MP DAMAGE (Osmose, Rasp, etc.)
                    message = string.Format(T("{0}: {1} MP damage"), targetName, value);
                }
                else if (hitTypeValue == HITTYPE_RECOVERY || isRecovery)
                {
                    // HP RECOVERY (Cure, Potion, etc.)
                    message = string.Format(T("{0}: Recovered {1} HP"), targetName, value);
                }
                else
                {
                    // HP DAMAGE — optionally prepend the multi-hit "{N}x" multiplier (kept terse; the
                    // " damage" suffix stays so damage/recovery/drain remain distinguishable).
                    message = (PreferencesManager.DamageDisplay == 1 && hitCount > 1)
                        ? string.Format(T("{0}: {1}x{2} damage"), targetName, hitCount, value)
                        : string.Format(T("{0}: {1} damage"), targetName, value);
                }

                // Damage/healing doesn't interrupt - queues after action announcement
                FFI_ScreenReaderMod.SpeakText(message, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error in CreateDamageView_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the display name for a battle unit (player or enemy).
        /// </summary>
        public static string GetUnitName(BattleUnitData data)
        {
            if (data == null) return T("Unknown");

            try
            {
                // Try player data first
                var playerData = data.TryCast<BattlePlayerData>();
                if (playerData?.ownedCharacterData != null)
                {
                    return playerData.ownedCharacterData.Name;
                }

                // Try enemy data
                var enemyData = data.TryCast<BattleEnemyData>();
                if (enemyData != null)
                {
                    string mesIdName = enemyData.GetMesIdName();
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                    {
                        string localizedName = messageManager.GetMessage(mesIdName);
                        if (!string.IsNullOrEmpty(localizedName))
                        {
                            return localizedName;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error getting unit name: {ex.Message}");
            }

            return T("Unknown");
        }

        /// <summary>
        /// Gets the display name for a condition from its ID.
        /// </summary>
        internal static string GetConditionName(int id)
        {
            try
            {
                // Look up condition in master data
                var masterManager = Il2CppLast.Data.Master.MasterManager.Instance;
                if (masterManager == null) return null;

                var conditionList = masterManager.GetList<Il2CppLast.Data.Master.Condition>();
                if (conditionList == null || !conditionList.ContainsKey(id)) return null;

                var condition = conditionList[id];
                if (condition == null) return null;

                string mesIdName = condition.MesIdName;
                if (string.IsNullOrEmpty(mesIdName) || mesIdName == "None") return null;

                var messageManager = MessageManager.Instance;
                if (messageManager == null) return null;

                return TextUtils.StripIconMarkup(messageManager.GetMessage(mesIdName));
            }
            catch { return null; } // Master data lookup may fail
        }

        /// <summary>
        /// Gets the action name for a battle action (item, spell, or command).
        /// Returns the actual item/spell name, not generic "Item" or "Magic".
        /// </summary>
        public static string GetActionName(BattleActData battleActData)
        {
            try
            {
                // Try to get item name first (for Item command)
                var itemList = battleActData.itemList;
                if (itemList != null && itemList.Count > 0)
                {
                    var ownedItem = itemList[0];
                    if (ownedItem != null)
                    {
                        string itemName = GetItemName(ownedItem);
                        if (!string.IsNullOrEmpty(itemName))
                        {
                            return itemName;
                        }
                    }
                }

                // Try to get the ability name (spells, skills)
                var abilityList = battleActData.abilityList;
                if (abilityList != null && abilityList.Count > 0)
                {
                    var ability = abilityList[0];
                    if (ability != null)
                    {
                        string abilityName = GetAbilityName(ability);
                        if (!string.IsNullOrEmpty(abilityName))
                        {
                            return abilityName;
                        }
                    }
                }

                // Fall back to command name (Attack, Defend, etc.)
                var command = battleActData.Command;
                if (command != null)
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string commandMesId = command.MesIdName;
                        if (!string.IsNullOrEmpty(commandMesId))
                        {
                            string localizedName = messageManager.GetMessage(commandMesId);
                            if (!string.IsNullOrEmpty(localizedName))
                            {
                                return TextUtils.StripIconMarkup(localizedName);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error getting action name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Gets the localized name of an item.
        /// </summary>
        private static string GetItemName(OwnedItemData ownedItem)
        {
            try
            {
                string itemName = ownedItem.Name;
                if (!string.IsNullOrEmpty(itemName))
                {
                    // Strip icon markup (e.g., "<ic_Potion>")
                    itemName = TextUtils.StripIconMarkup(itemName);
                    if (!string.IsNullOrEmpty(itemName))
                    {
                        return itemName;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error getting item name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Gets the localized name of an ability/spell.
        /// </summary>
        private static string GetAbilityName(Ability ability)
        {
            try
            {
                // Use ContentUtitlity to get ability name if available
                try
                {
                    string name = ContentUtitlity.GetAbilityName(ability);
                    if (!string.IsNullOrEmpty(name))
                    {
                        return TextUtils.StripIconMarkup(name);
                    }
                }
                catch { } // IL2CPP method may not be available

                // Fallback: Use ContentUtitlity.GetMesIdAbilityName + MessageManager
                try
                {
                    string mesIdName = ContentUtitlity.GetMesIdAbilityName(ability);
                    if (!string.IsNullOrEmpty(mesIdName))
                    {
                        var messageManager = MessageManager.Instance;
                        if (messageManager != null)
                        {
                            string localizedName = messageManager.GetMessage(mesIdName, false);
                            if (!string.IsNullOrEmpty(localizedName))
                            {
                                return TextUtils.StripIconMarkup(localizedName);
                            }
                        }
                    }
                }
                catch { } // Fallback lookup may fail too
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error getting ability name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Finds a type by name across all loaded assemblies.
        /// </summary>
        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.FullName == fullName)
                        {
                            return type;
                        }
                    }
                }
                catch { } // Assembly may not expose all types
            }
            return null;
        }

        /// <summary>
        /// Patch BattleCommandMessageController.SetMessage for system messages like "The party was defeated".
        /// </summary>
        private static void PatchBattleCommandMessage(HarmonyLib.Harmony harmony)
        {
            try
            {
                // KeyInput version (primary)
                var keyInputType = FindType("Il2CppLast.UI.KeyInput.BattleCommandMessageController");
                if (keyInputType != null)
                {
                    var setMessageMethod = AccessTools.Method(keyInputType, "SetMessage");
                    if (setMessageMethod != null)
                    {
                        var postfix = typeof(BattleMessagePatches).GetMethod(
                            nameof(SetMessage_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(setMessageMethod, postfix: new HarmonyMethod(postfix));
                    }
                    else
                    {
                        MelonLogger.Warning("[Battle Message] KeyInput.BattleCommandMessageController.SetMessage method not found");
                    }
                }
                else
                {
                    MelonLogger.Warning("[Battle Message] KeyInput.BattleCommandMessageController type not found");
                }

                // Touch version (SetSystemMessage)
                var touchType = FindType("Il2CppLast.UI.Touch.BattleCommandMessageController");
                if (touchType != null)
                {
                    var setSystemMsgMethod = AccessTools.Method(touchType, "SetSystemMessage");
                    if (setSystemMsgMethod != null)
                    {
                        var postfix = typeof(BattleMessagePatches).GetMethod(
                            nameof(SetMessage_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(setSystemMsgMethod, postfix: new HarmonyMethod(postfix));
                    }
                    else
                    {
                        MelonLogger.Warning("[Battle Message] Touch.BattleCommandMessageController.SetSystemMessage method not found");
                    }
                }
                else
                {
                    MelonLogger.Warning("[Battle Message] Touch.BattleCommandMessageController type not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error patching BattleCommandMessageController: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for BattleCommandMessageController.SetMessage/SetSystemMessage.
        /// Announces battle messages including "The party was defeated".
        /// </summary>
        public static void SetMessage_Postfix(object __0)
        {
            try
            {
                // __0 is the message string (using __0 to avoid IL2CPP string param crash)
                string message = __0?.ToString();
                if (string.IsNullOrEmpty(message)) return;

                // Clean up the message
                string cleanMessage = TextUtils.StripIconMarkup(message);
                cleanMessage = cleanMessage.Replace("\n", " ").Replace("\r", " ").Trim();
                while (cleanMessage.Contains("  "))
                    cleanMessage = cleanMessage.Replace("  ", " ");

                if (string.IsNullOrEmpty(cleanMessage)) return;

                // Use interrupt for defeat message
                bool isDefeatMessage = cleanMessage.Contains("defeated", StringComparison.OrdinalIgnoreCase);

                FFI_ScreenReaderMod.SpeakText(cleanMessage, interrupt: isDefeatMessage);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error in SetMessage_Postfix: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Declarative patch for BattleConditionController.Add — announces status effects (KO, Poison, etc.)
    /// when applied during battle. Matches FF3 pattern exactly.
    /// </summary>
    [HarmonyPatch(typeof(BattleConditionController), nameof(BattleConditionController.Add))]
    internal static class BattleConditionController_Add_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BattleUnitData battleUnitData, int id)
        {
            try
            {
                if (battleUnitData == null) return;

                // Get the condition from the unit's confirmed list (FF3 pattern)
                Condition confirmed = null;
                try
                {
                    var unitDataInfo = battleUnitData.BattleUnitDataInfo;
                    if (unitDataInfo?.Parameter != null)
                    {
                        var confirmedList = unitDataInfo.Parameter.ConfirmedConditionList();
                        if (confirmedList != null)
                        {
                            foreach (var condition in confirmedList)
                            {
                                if (condition != null && condition.Id == id)
                                {
                                    confirmed = condition;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { } // IL2CPP condition list access may fail

                string conditionName = ResolveConditionName(confirmed, id);
                if (string.IsNullOrEmpty(conditionName)) return;

                string targetName = BattleMessagePatches.GetUnitName(battleUnitData);
                string announcement = $"{targetName}: {conditionName}";

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Status] Error in Add postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Condition display name, shared by the Add ("X: Poison") and removal ("X: Poison removed")
        /// announcements so both use the same wording: the condition object's own name first, then the
        /// master-data lookup by id. Null for hidden/internal conditions (no name) — callers stay silent.
        /// </summary>
        internal static string ResolveConditionName(Condition condition, int id)
        {
            string name = condition != null ? MagicMenuState.GetConditionName(condition) : null;
            if (string.IsNullOrEmpty(name))
                name = BattleMessagePatches.GetConditionName(id);
            return name;
        }
    }

    /// <summary>
    /// Announces status removal ("{unit}: {condition} removed") for cures, natural wear-off and revive.
    ///
    /// Hook (FF1, settled offline — docs/debug.md "Round 2"): the private predicate
    /// <c>BattleConditionController.&lt;&gt;c__DisplayClass45_0.&lt;RemoveFunction&gt;b__0(BattleConditionFunction f)</c>
    /// (RVA 0x3EC600, unique body: <c>f.condition.Id == id</c>). Its only caller is the removal loop of
    /// <c>RemoveConditionFunction</c> (0x3DCC70), which runs when a condition has left the unit's
    /// ConfirmedConditionList and removes that condition's BattleConditionFunction — the mirror of
    /// <c>AddConditionFunction → Add</c>, which drives the "X: Poison" announcement. Every removal source
    /// (item/spell cures via RecoveryConditionFunction.UpdateParameter, ConditionUntilType wear-off in
    /// Recovery, Cancellation/ConflictCondition, InterruptRemoveCondition → Remove, battle-end cleanup)
    /// ends in that loop. <c>BattleConditionController.Remove</c> itself is NOT used: in FF1 it is only
    /// reached from InterruptRemoveCondition and BattleEndRecoveryCondition, so it misses cures and wear-off.
    /// A removal can only be announced for a function Add created, so a status that never applied (negated
    /// before the sync) never produces "X: Poison removed". The predicate runs only inside the removal loop
    /// (a real removal), never per frame, unlike RemoveConditionFunction itself (called every frame).
    ///
    /// Silent for: battle-end cleanup (BattleEndRecoveryCondition scope), statuses cleared because the unit
    /// is out of the fight (KO / Stone — game's own ConditionUtility.IsOutBattle), a condition that is still
    /// present (stacked copy), conditions without a name, and a repeat of the same (unit, condition) in a frame.
    /// </summary>
    internal static class BattleConditionRemovalPatches
    {
        // BattleConditionFunction fields (dump.cs TypeDefIndex 9706)
        private const int OFFSET_FUNCTION_UNIT = 0x10;       // <BattleUnitData>k__BackingField
        private const int OFFSET_FUNCTION_CONDITION = 0x30;  // public Condition condition

        private const int CONDITION_TYPE_KO = 5;

        // True while BattleEndRecoveryCondition runs (victory / escape cleanup).
        private static bool _battleEndCleanup;

        // Same-frame (unit, condition) dedup.
        private static int _dedupFrame = -1;
        private static readonly HashSet<(IntPtr, int)> _spokenThisFrame = new HashSet<(IntPtr, int)>();

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MethodInfo predicate = null;
                foreach (var nested in typeof(BattleConditionController).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                {
                    foreach (var m in nested.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    {
                        if (!m.Name.Contains("RemoveFunction") || m.ReturnType != typeof(bool)) continue;
                        var ps = m.GetParameters();
                        if (ps.Length == 1 && ps[0].ParameterType.Name == "BattleConditionFunction")
                        {
                            predicate = m;
                            break;
                        }
                    }
                    if (predicate != null) break;
                }

                if (predicate != null)
                {
                    harmony.Patch(predicate, postfix: new HarmonyMethod(
                        typeof(BattleConditionRemovalPatches).GetMethod(nameof(RemoveFunctionPredicate_Postfix), BindingFlags.Public | BindingFlags.Static)));
                }
                else
                {
                    MelonLogger.Warning("[Battle Status] RemoveFunction predicate not found — status removal will not be announced");
                }

                var battleEnd = AccessTools.Method(typeof(BattleConditionController), "BattleEndRecoveryCondition");
                if (battleEnd != null)
                {
                    harmony.Patch(battleEnd,
                        prefix: new HarmonyMethod(typeof(BattleConditionRemovalPatches).GetMethod(nameof(BattleEndRecoveryCondition_Prefix), BindingFlags.Public | BindingFlags.Static)),
                        postfix: new HarmonyMethod(typeof(BattleConditionRemovalPatches).GetMethod(nameof(BattleEndRecoveryCondition_Postfix), BindingFlags.Public | BindingFlags.Static)));
                }
                else
                {
                    MelonLogger.Warning("[Battle Status] BattleEndRecoveryCondition not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Status] Error patching status removal: {ex.Message}");
            }
        }

        public static void BattleEndRecoveryCondition_Prefix() => _battleEndCleanup = true;
        public static void BattleEndRecoveryCondition_Postfix() => _battleEndCleanup = false;

        /// <summary>Called on battle start so a missed postfix can never silence the next battle.</summary>
        internal static void ResetState()
        {
            _battleEndCleanup = false;
            _spokenThisFrame.Clear();
            _dedupFrame = -1;
        }

        /// <summary>
        /// Postfix on the RemoveFunction predicate. __0 = the BattleConditionFunction being tested;
        /// __result = true for the function RemoveConditionFunction is about to remove.
        /// </summary>
        public static void RemoveFunctionPredicate_Postfix(BattleConditionFunction __0, bool __result)
        {
            try
            {
                if (!__result || __0 == null || _battleEndCleanup) return;

                IntPtr fnPtr = __0.Pointer;
                if (fnPtr == IntPtr.Zero) return;
                IntPtr unitPtr = IL2CppFieldReader.ReadPointerSafe(fnPtr, OFFSET_FUNCTION_UNIT);
                IntPtr conditionPtr = IL2CppFieldReader.ReadPointerSafe(fnPtr, OFFSET_FUNCTION_CONDITION);
                if (unitPtr == IntPtr.Zero || conditionPtr == IntPtr.Zero) return;

                var unit = new BattleUnitData(unitPtr);
                var condition = new Condition(conditionPtr);
                int id = condition.Id;

                var param = unit.BattleUnitDataInfo?.Parameter;
                if (param != null)
                {
                    // Statuses cleared because the unit dropped out of the fight (KO / Stone): silent.
                    // A revive removes KO itself, so the unit is back in the fight and "KO removed" speaks.
                    if (Il2CppLast.Systems.ConditionUtility.IsOutBattle(param)) return;

                    var confirmedList = param.ConfirmedConditionList();
                    if (confirmedList != null)
                    {
                        foreach (var c in confirmedList)
                        {
                            if (c == null) continue;
                            if (c.Id == id) return;                               // still present (stacked copy)
                            if (c.ConditionType == CONDITION_TYPE_KO) return;     // KO clearing other statuses
                        }
                    }
                }

                int frame = UnityEngine.Time.frameCount;
                if (frame != _dedupFrame)
                {
                    _dedupFrame = frame;
                    _spokenThisFrame.Clear();
                }
                if (!_spokenThisFrame.Add((unitPtr, id))) return;

                string conditionName = BattleConditionController_Add_Patch.ResolveConditionName(condition, id);
                if (string.IsNullOrEmpty(conditionName)) return;

                string targetName = BattleMessagePatches.GetUnitName(unit);
                FFI_ScreenReaderMod.SpeakText(string.Format(T("{0}: {1} removed"), targetName, conditionName), interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Status] Error in removal postfix: {ex.Message}");
            }
        }
    }
}
