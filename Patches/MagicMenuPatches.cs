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

        // One-shot: set when the spell list state is (re)entered (UseListInit/ForgetInit), consumed by the
        // per-frame UpdateController reader once the list is built. Announces the focused spell when the
        // list (re)gains focus — on initial entry AND on returning from a spell's target — since SetCursor
        // only fires on movement and never speaks the focused slot on (re)entry.
        private static bool pendingInitialSpellAnnounce;
        // Frames the consume has waited for the list to be ready; caps the self-retry.
        private static int initialSpellAnnounceFrames;

        private const int OFFSET_SELECT_CURSOR = IL2CppOffsets.MagicMenu.SelectCursor;
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

                // State-entry hooks for the spell list: UseListInit (Use) / ForgetInit (Forget) fire when
                // the window enters that state — on initial entry AND when returning from the target — so
                // they arm the focused-spell announce for both. No-arg private methods (real RVAs), so no
                // enum-param binding issue.
                PatchWindowInit(harmony, controllerType, "UseListInit", nameof(UseListInit_Postfix));
                PatchWindowInit(harmony, controllerType, "ForgetInit", nameof(ForgetInit_Postfix));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch SetNextState: {ex.Message}");
            }
        }

        /// <summary>Patches a no-arg private method on the window controller with the given postfix.</summary>
        private static void PatchWindowInit(HarmonyLib.Harmony harmony, Type controllerType, string methodName, string postfixName)
        {
            try
            {
                var method = HarmonyLib.AccessTools.Method(controllerType, methodName);
                if (method == null)
                {
                    MelonLogger.Warning($"[Magic Menu] {methodName} not found");
                    return;
                }
                var postfix = typeof(MagicMenuPatches).GetMethod(postfixName, BindingFlags.Public | BindingFlags.Static);
                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch {methodName}: {ex.Message}");
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
                // AbilityWindowController.State: None=0, UseList=1, UseTarget=2, Forget=3, Command=4, Popup=5.
                // (Arming is done by UseListInit/ForgetInit, not here — SetNextState's State enum param does
                // not bind reliably to int.)
                if (state == 0)
                {
                    MagicMenuState.ResetState();
                    pendingInitialSpellAnnounce = false;
                }
            }
            catch { } // State reset is best-effort
        }

        /// <summary>Postfix for AbilityWindowController.UseListInit — entering the Use spell list.</summary>
        public static void UseListInit_Postfix() => ArmInitialSpellAnnounce();

        /// <summary>Postfix for AbilityWindowController.ForgetInit — entering the Forget spell list.</summary>
        public static void ForgetInit_Postfix() => ArmInitialSpellAnnounce();

        /// <summary>Arms the one-shot focused-spell announce; the UpdateController consume speaks it.</summary>
        private static void ArmInitialSpellAnnounce()
        {
            pendingInitialSpellAnnounce = true;
            initialSpellAnnounceFrames = 0;
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

                // Initial-focus announce: once the spell list is built, speak the focused spell once.
                // UpdateController runs every frame, so this self-retries until the list is genuinely ready
                // (populated + the focused slot has a real ability) and only then consumes the one-shot.
                if (pendingInitialSpellAnnounce)
                {
                    initialSpellAnnounceFrames++;
                    bool spoke = TryAnnounceInitialSpell(controller);
                    if (spoke || initialSpellAnnounceFrames > 30)
                        pendingInitialSpellAnnounce = false;
                }
            }
            catch { } // IL2CPP field reads may fail transiently
        }

        /// <summary>
        /// Announces the focused spell IF the list is genuinely ready (menu open, contentList populated,
        /// focused index in range, slot + ability non-null). Returns true only when it actually spoke, so
        /// the caller keeps retrying until the list finishes building.
        /// </summary>
        private static bool TryAnnounceInitialSpell(AbilityContentListController controller)
        {
            try
            {
                IntPtr ptr = controller.Pointer;
                if (ptr == IntPtr.Zero) return false;

                // Gate on the menu actually being open so this never reads during a map/asset load.
                var menuManager = Il2CppLast.UI.MenuManager.Instance;
                if (menuManager == null || !menuManager.IsOpen) return false;

                IntPtr contentListPtr = IL2CppFieldReader.ReadPointer(ptr, OFFSET_CONTENT_LIST);
                if (contentListPtr == IntPtr.Zero) return false;

                var contentList = new Il2CppSystem.Collections.Generic.List<BattleAbilityInfomationContentController>(contentListPtr);
                if (contentList.Count == 0) return false;

                int index = ReadSelectCursorIndex(controller);
                if (index < 0 || index >= contentList.Count) return false;

                var cc = contentList[index];
                if (cc == null) return false;
                var ability = cc.Data;
                if (ability == null) return false;

                // Force the (re)entry announce even if the focused spell equals the last one announced
                // (e.g. a cursor-settle SetCursor fired first on a popup back-out) — the consume is the
                // authoritative re-entry source.
                _lastSpellAnnouncement = null;
                return AnnounceSpell(ability, index, contentList.Count);
            }
            catch { return false; }
        }

        /// <summary>Reads the spell list controller's focused index from its selectCursor, or -1.</summary>
        private static int ReadSelectCursorIndex(AbilityContentListController controller)
        {
            try
            {
                IntPtr ptr = controller.Pointer;
                if (ptr == IntPtr.Zero) return -1;
                IntPtr cursorPtr = IL2CppFieldReader.ReadPointer(ptr, OFFSET_SELECT_CURSOR);
                if (cursorPtr == IntPtr.Zero) return -1;
                return new GameCursor(cursorPtr).Index;
            }
            catch { return -1; }
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

                int count = contentList.Count;
                var contentController = contentList[index];
                if (contentController == null)
                {
                    AnnounceEmpty(index, count);
                    return;
                }

                var ability = contentController.Data;
                if (ability == null)
                {
                    AnnounceEmpty(index, count);
                    return;
                }

                AnnounceSpell(ability, index, count);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in AnnounceSpellAtIndex: {ex.Message}");
            }
        }

        private static void AnnounceEmpty(int index, int count)
        {
            FFI_ScreenReaderMod.SpeakText(MenuPosition.Format(T("Empty"), index, count), interrupt: true);
        }

        // Last spell announcement string, to dedup the same spell being spoken twice. On the Forget-popup
        // back-out BOTH the cursor-settle SetCursor AND the re-announce consume reach AnnounceSpell for the
        // same slot; the settling fires first and its string equals the prior navigation announce, so it is
        // the one suppressed. Navigation always produces a different string. The consume clears this so a
        // genuine (re)entry always re-announces. (Same last-announced-value pattern as ShopPatches.)
        private static string _lastSpellAnnouncement;

        private static bool AnnounceSpell(OwnedAbility ability, int index, int count)
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
                    return false; // Ability data not yet loaded
                }

                // Cache for I key lookup
                MagicMenuState.LastAnnouncedAbility = ability;

                string spellName = MagicMenuState.GetSpellName(ability);
                if (string.IsNullOrEmpty(spellName))
                    return false;

                string baseAnnouncement = spellName;

                if (spellLevel > 0)
                {
                    baseAnnouncement += $" LV{spellLevel}";

                    if (MagicMenuState.CurrentCharacter != null)
                    {
                        var (current, max) = MagicMenuState.GetChargesForLevel(MagicMenuState.CurrentCharacter, spellLevel);
                        if (max > 0)
                            baseAnnouncement += $": MP: {current}/{max}";
                    }
                }

                // AutoDetail: append description on focus when enabled
                string announcement = baseAnnouncement;
                if (FFI_ScreenReaderMod.AutoDetailEnabled)
                {
                    try
                    {
                        string description = MagicMenuState.GetSpellDescription(ability);
                        if (!string.IsNullOrWhiteSpace(description))
                            announcement = $"{baseAnnouncement}: {description}";
                    }
                    catch { }
                }

                // Position last — after any AutoDetail description.
                announcement = MenuPosition.Format(announcement, index, count);

                if (announcement == _lastSpellAnnouncement)
                    return false; // identical to the last announce (e.g. cursor-settle re-fire) — skip
                _lastSpellAnnouncement = announcement;

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                return true;
            }
            catch { return false; } // Spell announcement is best-effort
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
