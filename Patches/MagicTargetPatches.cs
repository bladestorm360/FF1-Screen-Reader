using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

using AbilityUseContentListController = Il2CppSerial.FF1.UI.KeyInput.AbilityUseContentListController;
using ItemTargetSelectContentController = Il2CppLast.UI.KeyInput.ItemTargetSelectContentController;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Patches for magic menu target selection (AbilityUseContentListController).
    /// </summary>
    public static class MagicTargetPatches
    {
        private const int OFFSET_USE_CONTENT_LIST = IL2CppOffsets.MagicTarget.ContentList;
        private const int OFFSET_USE_SELECT_CURSOR = IL2CppOffsets.MagicTarget.SelectCursor;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            TryPatchTargetSetCursor(harmony);
            TryPatchTargetShow(harmony);
            TryPatchTargetClose(harmony);
        }

        private static void TryPatchTargetSetCursor(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityUseContentListController);

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
                    var postfix = typeof(MagicTargetPatches).GetMethod(nameof(TargetSetCursor_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setCursorMethod, postfix: new HarmonyMethod(postfix));
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

        private static void TryPatchTargetShow(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityUseContentListController);

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
                    var postfix = typeof(MagicTargetPatches).GetMethod(nameof(TargetShow_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(showMethod, postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch target Show: {ex.Message}");
            }
        }

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
                    var postfix = typeof(MagicTargetPatches).GetMethod(nameof(TargetClose_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(closeMethod, postfix: new HarmonyMethod(postfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch target Close: {ex.Message}");
            }
        }

        public static void TargetShow_Postfix(object __instance)
        {
            try
            {
                MagicMenuState.OnTargetSelectionActive();
            }
            catch { } // Target state change is best-effort
        }

        public static void TargetClose_Postfix(object __instance)
        {
            try
            {
                if (!MagicMenuState.IsTargetSelectionActive)
                    return;

                MagicMenuState.OnTargetSelectionClosed();
            }
            catch { } // Target close cleanup is best-effort
        }

        public static void TargetSetCursor_Postfix(object __instance, GameCursor targetCursor)
        {
            try
            {
                if (__instance == null || targetCursor == null)
                    return;

                var controller = __instance as AbilityUseContentListController;
                if (controller == null || !controller.gameObject.activeInHierarchy)
                    return;

                if (!MagicMenuState.IsTargetSelectionActive)
                    MagicMenuState.OnTargetSelectionActive();

                int index = targetCursor.Index;
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return;

                IntPtr contentListPtr = IL2CppFieldReader.ReadPointer(controllerPtr, OFFSET_USE_CONTENT_LIST);
                if (contentListPtr == IntPtr.Zero)
                    return;

                var contentList = new Il2CppSystem.Collections.Generic.List<ItemTargetSelectContentController>(contentListPtr);

                if (index < 0 || index >= contentList.Count)
                    return;

                var content = contentList[index];
                if (content == null)
                    return;

                var characterData = content.CurrentData;
                if (characterData == null)
                    return;

                AnnounceTargetCharacter(characterData);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in TargetSetCursor_Postfix: {ex.Message}");
            }
        }

        private static void AnnounceTargetCharacter(OwnedCharacterData characterData)
        {
            try
            {
                string charName = characterData.Name;
                if (string.IsNullOrWhiteSpace(charName))
                    return;

                string announcement = charName;

                try
                {
                    var parameter = characterData.Parameter;
                    if (parameter != null)
                    {
                        int currentHp = parameter.currentHP;
                        int maxHp = parameter.ConfirmedMaxHp();
                        announcement += $": HP: {currentHp}/{maxHp}";

                        var conditionList = parameter.CurrentConditionList;
                        if (conditionList != null && conditionList.Count > 0)
                        {
                            var statusNames = new System.Collections.Generic.List<string>();
                            foreach (var condition in conditionList)
                            {
                                string conditionName = MagicMenuState.GetConditionName(condition);
                                if (!string.IsNullOrWhiteSpace(conditionName))
                                    statusNames.Add(conditionName);
                            }

                            if (statusNames.Count > 0)
                                announcement += " " + string.Join(", ", statusNames);
                        }
                    }
                }
                catch (Exception paramEx)
                {
                    MelonLogger.Warning($"[Magic Target] Error getting character parameters: {paramEx.Message}");
                }

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Target] Error announcing character: {ex.Message}");
            }
        }
    }
}
