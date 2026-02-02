using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

// FF1 Battle types
using BattleActData = Il2CppLast.Battle.BattleActData;
using BattleUnitData = Il2CppLast.Battle.BattleUnitData;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using BattleEnemyData = Il2CppLast.Battle.BattleEnemyData;
using BattleBasicFunction = Il2CppLast.Battle.Function.BattleBasicFunction;
using BattleConditionController = Il2CppLast.Battle.BattleConditionController;
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
                MelonLogger.Msg("[Battle Message] Applying battle message patches...");

                // Patch InitActionState for actor/action announcements
                PatchInitActionState(harmony);

                // Patch CreateDamageView for damage/healing announcements
                PatchCreateDamageView(harmony);

                // Patch Add for status effect announcements
                PatchConditionAdd(harmony);

                // Patch BattleCommandMessageController.SetMessage for system messages like "The party was defeated"
                PatchBattleCommandMessage(harmony);

                MelonLogger.Msg("[Battle Message] Battle message patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Battle Message] Error applying patches: {ex.Message}");
            }
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
                    MelonLogger.Msg("[Battle Message] Patched InitActionState successfully");
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
                            MelonLogger.Msg($"[Battle Message]   - {m.Name}");
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
                    MelonLogger.Msg("[Battle Message] Patched CreateDamageView successfully");
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
        /// Patch BattleConditionController.Add for status effect announcements.
        /// </summary>
        private static void PatchConditionAdd(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Use typeof() with the IL2CPP alias - more reliable than Type.GetType()
                var conditionControllerType = typeof(BattleConditionController);
                MelonLogger.Msg($"[Battle Message] BattleConditionController type: {conditionControllerType?.FullName ?? "null"}");

                if (conditionControllerType != null)
                {
                    // List all methods for debugging
                    var allMethods = conditionControllerType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
                    MelonLogger.Msg($"[Battle Message] Found {allMethods.Length} methods on BattleConditionController");

                    // The Add method is private: Add(BattleUnitData, int)
                    MethodInfo addMethod = null;
                    foreach (var method in allMethods)
                    {
                        if (method.Name == "Add")
                        {
                            var parameters = method.GetParameters();
                            MelonLogger.Msg($"[Battle Message] Found Add method with {parameters.Length} params");
                            if (parameters.Length == 2)
                            {
                                addMethod = method;
                                MelonLogger.Msg($"[Battle Message] Add params: {parameters[0].ParameterType.Name}, {parameters[1].ParameterType.Name}");
                                break;
                            }
                        }
                    }

                    if (addMethod != null)
                    {
                        var postfix = typeof(BattleMessagePatches).GetMethod(
                            nameof(ConditionAdd_Postfix),
                            BindingFlags.Public | BindingFlags.Static
                        );

                        harmony.Patch(addMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("[Battle Message] Patched BattleConditionController.Add successfully");
                    }
                    else
                    {
                        MelonLogger.Warning("[Battle Message] BattleConditionController.Add method not found");
                        // Try InterruptAddCondition as fallback
                        TryPatchInterruptAddCondition(harmony, conditionControllerType);
                    }
                }
                else
                {
                    MelonLogger.Warning("[Battle Message] BattleConditionController type not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error patching BattleConditionController.Add: {ex.Message}");
            }
        }

        /// <summary>
        /// Fallback: Patch InterruptAddCondition if Add method not found.
        /// </summary>
        private static void TryPatchInterruptAddCondition(HarmonyLib.Harmony harmony, Type controllerType)
        {
            try
            {
                // InterruptAddCondition(BattleUnitData, int) is public
                var allMethods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                foreach (var method in allMethods)
                {
                    if (method.Name == "InterruptAddCondition")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 2 && parameters[1].ParameterType == typeof(int))
                        {
                            var postfix = typeof(BattleMessagePatches).GetMethod(
                                nameof(ConditionAdd_Postfix),
                                BindingFlags.Public | BindingFlags.Static
                            );

                            harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                            MelonLogger.Msg("[Battle Message] Patched BattleConditionController.InterruptAddCondition (fallback)");
                            return;
                        }
                    }
                }
                MelonLogger.Warning("[Battle Message] InterruptAddCondition fallback also failed");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error patching InterruptAddCondition: {ex.Message}");
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
                    MelonLogger.Msg("[Battle Action] InStagingBattleActData is null");
                    return;
                }

                // Get actor name
                var actor = actData.AttackUnitData;
                string actorName = GetUnitName(actor);

                // Get action name
                string actionName = GetActionName(actData);

                string announcement;
                if (string.IsNullOrEmpty(actionName))
                {
                    // Fallback: just announce actor
                    announcement = actorName;
                }
                else if (actionName.ToLower() == "attack" || actionName.ToLower() == "fight")
                {
                    // For basic attacks: "Actor attacks"
                    announcement = $"{actorName} attacks";
                }
                else
                {
                    // For spells/items: "Actor: Spell/Item"
                    announcement = $"{actorName}: {actionName}";
                }

                // Use object-based deduplication so different enemies with the same name
                // attacking in succession are both announced
                if (AnnouncementDeduplicator.ShouldAnnounce("BattleAction", actData))
                {
                    MelonLogger.Msg($"[Battle Action] {announcement}");
                    FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error in InitActionState_Postfix: {ex.Message}");
            }
        }

        // HitType enum values (from dump.cs)
        private const int HITTYPE_MISS = 2;
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
                MelonLogger.Msg($"[Battle Damage] data={data != null}, value={value}, hitType={hitTypeValue}, isRecovery={isRecovery}");

                if (data == null) return;

                string targetName = GetUnitName(data);

                string message;
                if (hitTypeValue == HITTYPE_MISS || value == 0)
                {
                    message = $"{targetName}: Miss";
                }
                else if (hitTypeValue == HITTYPE_MP_RECOVERY)
                {
                    // MP RECOVERY (Ether, Turbo Ether, etc.)
                    message = $"{targetName}: Recovered {value} MP";
                    MelonLogger.Msg($"[Battle Damage] MP RECOVERY path for {targetName}");
                }
                else if (hitTypeValue == HITTYPE_MP_HIT)
                {
                    // MP DAMAGE (Osmose, Rasp, etc.)
                    message = $"{targetName}: {value} MP damage";
                    MelonLogger.Msg($"[Battle Damage] MP DAMAGE path for {targetName}");
                }
                else if (hitTypeValue == HITTYPE_RECOVERY || isRecovery)
                {
                    // HP RECOVERY (Cure, Potion, etc.)
                    message = $"{targetName}: Recovered {value} HP";
                    MelonLogger.Msg($"[Battle Damage] HP RECOVERY path for {targetName}");
                }
                else
                {
                    // HP DAMAGE
                    message = $"{targetName}: {value} damage";
                    MelonLogger.Msg($"[Battle Damage] HP DAMAGE path for {targetName}");
                }

                MelonLogger.Msg($"[Battle Damage] {message}");
                // Damage/healing doesn't interrupt - queues after action announcement
                FFI_ScreenReaderMod.SpeakText(message, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error in CreateDamageView_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for BattleConditionController.Add - announces status effects.
        /// Uses object-based deduplication so different enemies with the same name
        /// getting the same status are both announced.
        /// </summary>
        public static void ConditionAdd_Postfix(object __instance, BattleUnitData battleUnitData, int id)
        {
            try
            {
                if (battleUnitData == null) return;

                // Get condition name from ID
                string conditionName = GetConditionName(id);

                if (string.IsNullOrEmpty(conditionName))
                {
                    return;
                }

                // Use object-based deduplication so different enemies with same name
                // getting the same status are both announced
                if (!AnnouncementDeduplicator.ShouldAnnounce("BattleStatus", battleUnitData))
                {
                    return;
                }

                string targetName = GetUnitName(battleUnitData);
                string announcement = $"{targetName}: {conditionName}";

                MelonLogger.Msg($"[Battle Status] {announcement}");
                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error in ConditionAdd_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the display name for a battle unit (player or enemy).
        /// </summary>
        public static string GetUnitName(BattleUnitData data)
        {
            if (data == null) return "Unknown";

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

            return "Unknown";
        }

        /// <summary>
        /// Gets the display name for a condition from its ID.
        /// </summary>
        private static string GetConditionName(int id)
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
            catch
            {
                return null;
            }
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
                catch { }

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
                catch { }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error getting ability name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Reset state tracking (call at battle end).
        /// </summary>
        public static void ResetState()
        {
            AnnouncementDeduplicator.Reset("BattleAction", "BattleStatus");
            lastBattleCommandMessage = "";
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
                catch { }
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
                        MelonLogger.Msg("[Battle Message] Patched KeyInput.BattleCommandMessageController.SetMessage");
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
                        MelonLogger.Msg("[Battle Message] Patched Touch.BattleCommandMessageController.SetSystemMessage");
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

        private static string lastBattleCommandMessage = "";

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

                // Deduplicate
                if (message == lastBattleCommandMessage) return;
                lastBattleCommandMessage = message;

                // Clean up the message
                string cleanMessage = TextUtils.StripIconMarkup(message);
                cleanMessage = cleanMessage.Replace("\n", " ").Replace("\r", " ").Trim();
                while (cleanMessage.Contains("  "))
                    cleanMessage = cleanMessage.Replace("  ", " ");

                if (string.IsNullOrEmpty(cleanMessage)) return;

                // Use interrupt for defeat message
                bool isDefeatMessage = cleanMessage.Contains("defeated", StringComparison.OrdinalIgnoreCase);

                MelonLogger.Msg($"[Battle Command Message] {cleanMessage}");
                FFI_ScreenReaderMod.SpeakText(cleanMessage, interrupt: isDefeatMessage);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Message] Error in SetMessage_Postfix: {ex.Message}");
            }
        }
    }
}
