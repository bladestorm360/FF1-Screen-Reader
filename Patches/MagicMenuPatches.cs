using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

using AbilityContentListController = Il2CppSerial.FF1.UI.KeyInput.AbilityContentListController;
using AbilityWindowController = Il2CppSerial.FF1.UI.KeyInput.AbilityWindowController;
using BattleAbilityInfomationContentController = Il2CppLast.UI.KeyInput.BattleAbilityInfomationContentController;
using OwnedAbility = Il2CppLast.Data.User.OwnedAbility;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Patches for magic menu spell list using manual Harmony patching.
    /// Target selection patches are in MagicTargetPatches.
    /// </summary>
    public static class MagicMenuPatches
    {
        private static bool isPatched = false;

        private const int OFFSET_CONTENT_LIST = IL2CppOffsets.MagicMenu.ContentList;
        private const int OFFSET_SLOT_CONTENT_LIST = IL2CppOffsets.MagicMenu.SlotContentList;
        private const int OFFSET_ON_SELECTED = IL2CppOffsets.MagicMenu.OnSelected;
        private const int OFFSET_ON_SELECT = IL2CppOffsets.MagicMenu.OnSelect;
        private const int OFFSET_ON_CANCEL = IL2CppOffsets.MagicMenu.OnCancel;
        private const int OFFSET_STATUS_CONTROLLER = IL2CppOffsets.AbilityWindow.StatusController;
        private const int OFFSET_TARGET_DATA = IL2CppOffsets.AbilityWindow.TargetData;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchUpdateController(harmony);
                TryPatchSetCursor(harmony);
                TryPatchWindowController(harmony);
                TryPatchSetActive(harmony);

                // Target selection patches (separate class)
                MagicTargetPatches.ApplyPatches(harmony);

                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Magic Menu] Error applying patches: {ex.Message}");
            }
        }

        #region Patch Registration

        private static void TryPatchUpdateController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityContentListController);

                var updateControllerMethod = controllerType.GetMethod("UpdateController",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);

                if (updateControllerMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(UpdateController_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateControllerMethod, postfix: new HarmonyMethod(postfix));
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

        private static void TryPatchSetCursor(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityContentListController);

                MethodInfo setCursorMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "SetCursor")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 2 &&
                            parameters[0].ParameterType == typeof(int) &&
                            parameters[1].ParameterType.Name == "Cursor")
                        {
                            setCursorMethod = method;
                            break;
                        }
                    }
                }

                if (setCursorMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(SetCursor_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setCursorMethod, postfix: new HarmonyMethod(postfix));
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

        private static void TryPatchWindowController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityWindowController);

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
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch SetNextState: {ex.Message}");
            }
        }

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

        #endregion

        #region Postfixes

        public static void SetActive_Postfix(bool isActive)
        {
            if (!isActive)
                MagicMenuState.ResetState();
        }

        public static void SetNextState_Postfix(object __instance, int state)
        {
            try
            {
                if (state == 0)
                    MagicMenuState.ResetState();
            }
            catch { } // State reset is best-effort
        }

        public static void UpdateController_Postfix(object __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var controller = __instance as AbilityContentListController;
                if (controller == null || !controller.gameObject.activeInHierarchy)
                    return;

                if (!MagicMenuState.IsSpellListActive)
                    MagicMenuState.OnSpellListFocused();

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
                                IntPtr statusControllerPtr = IL2CppFieldReader.ReadPointer(windowPtr, OFFSET_STATUS_CONTROLLER);
                                if (statusControllerPtr != IntPtr.Zero)
                                {
                                    IntPtr charPtr = IL2CppFieldReader.ReadPointer(statusControllerPtr, OFFSET_TARGET_DATA);
                                    if (charPtr != IntPtr.Zero)
                                        MagicMenuState.CurrentCharacter = new OwnedCharacterData(charPtr);
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
            catch { } // IL2CPP field reads may fail transiently
        }

        public static void SetCursor_Postfix(object __instance, int index, GameCursor targetCursor)
        {
            try
            {
                if (!MagicMenuState.IsSpellListActive)
                    return;

                if (__instance == null)
                    return;

                var controller = __instance as AbilityContentListController;
                if (controller == null || !controller.gameObject.activeInHierarchy)
                    return;

                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return;

                AnnounceSpellAtIndex(controllerPtr, index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in SetCursor_Postfix: {ex.Message}");
            }
        }

        #endregion

        #region Spell Announcement

        private static void AnnounceSpellAtIndex(IntPtr controllerPtr, int index)
        {
            try
            {
                IntPtr contentListPtr = IL2CppFieldReader.ReadPointer(controllerPtr, OFFSET_CONTENT_LIST);
                if (contentListPtr == IntPtr.Zero)
                    return;

                var contentList = new Il2CppSystem.Collections.Generic.List<BattleAbilityInfomationContentController>(contentListPtr);

                if (index < 0 || index >= contentList.Count)
                    return;

                var contentController = contentList[index];
                if (contentController == null)
                {
                    AnnounceEmpty();
                    return;
                }

                var ability = contentController.Data;
                if (ability == null)
                {
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

        private static void AnnounceEmpty()
        {
            if (MagicMenuState.ShouldAnnounceSpell(-1))
                FFI_ScreenReaderMod.SpeakText(T("Empty"), interrupt: true);
        }

        private static void AnnounceSpell(OwnedAbility ability)
        {
            try
            {
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
                    return; // Ability data not yet loaded
                }

                if (!MagicMenuState.ShouldAnnounceSpell(spellId))
                    return;

                string spellName = MagicMenuState.GetSpellName(ability);
                if (string.IsNullOrEmpty(spellName))
                    return;

                string announcement = spellName;

                if (spellLevel > 0)
                {
                    announcement += $" LV{spellLevel}";

                    if (MagicMenuState.CurrentCharacter != null)
                    {
                        var (current, max) = MagicMenuState.GetChargesForLevel(MagicMenuState.CurrentCharacter, spellLevel);
                        if (max > 0)
                            announcement += $": MP: {current}/{max}";
                    }
                }

                string description = MagicMenuState.GetSpellDescription(ability);
                if (!string.IsNullOrEmpty(description))
                    announcement += $": {description}";

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch { } // Spell announcement is best-effort
        }

        #endregion
    }

    /// <summary>
    /// Attribute-based patch for AbilityWindowController.SetActive.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppSerial.FF1.UI.KeyInput.AbilityWindowController), "SetActive", new Type[] { typeof(bool) })]
    public static class AbilityWindowController_SetActive_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isActive)
        {
            if (!isActive)
                MagicMenuState.ResetState();
        }
    }
}
