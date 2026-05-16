using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Menus;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;
using Il2CppLast.Management;
using Il2CppLast.OutGame.Library;
using Il2CppLast.UI.Common.Library;
using Il2CppLast.Data.Master;
using Il2CppLast.UI.Common;
using LibraryInfoController_KeyInput = Il2CppLast.UI.KeyInput.LibraryInfoController;
using LibraryMenuController_KeyInput = Il2CppLast.UI.KeyInput.LibraryMenuController;
using LibraryMenuListController_KeyInput = Il2CppLast.UI.KeyInput.LibraryMenuListController;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Tracks bestiary navigation state within the detail view.
    /// </summary>
    public class BestiaryNavigationTracker
    {
        private static BestiaryNavigationTracker instance = null;
        public static BestiaryNavigationTracker Instance
        {
            get
            {
                if (instance == null)
                    instance = new BestiaryNavigationTracker();
                return instance;
            }
        }

        public bool IsNavigationActive { get; set; }
        public MonsterData CurrentMonsterData { get; set; }
        public LibraryInfoController_KeyInput ActiveController { get; set; }

        private BestiaryNavigationTracker() { Reset(); }

        public void Reset()
        {
            IsNavigationActive = false;
            CurrentMonsterData = null;
            ActiveController = null;
            BestiaryNavigationReader.Reset();
        }

        public bool ValidateState()
        {
            return IsNavigationActive &&
                   CurrentMonsterData != null &&
                   ActiveController != null &&
                   ActiveController.gameObject != null &&
                   ActiveController.gameObject.activeInHierarchy;
        }
    }

    /// <summary>
    /// Tracks the current bestiary scene state.
    /// </summary>
    public static class BestiaryStateTracker
    {
        public static int CurrentState { get; set; } = -1;
        public static bool SuppressNextListEntry { get; set; } = false;
        public static int FullMapIndex { get; set; } = 0;
        public static string CachedEntryName { get; set; } = null;
        public static List<string> CachedHabitatNames { get; set; } = null;
        // SubSceneManagerExtraLibrary.State: Init=0, List=1, Field=2, Dungeon=3, Info=4, ArTop=5, ArBattle=6, GotoTitle=7

        public static bool IsInBestiary => CurrentState >= 1 && CurrentState <= 6;
        public static bool IsInList => CurrentState == 1;
        public static bool IsInDetail => CurrentState == 4;
        public static bool IsInFormation => CurrentState == 5;
        public static bool IsInMap => CurrentState == 2;

        public static void ClearState()
        {
            CurrentState = -1;
            SuppressNextListEntry = false;
            FullMapIndex = 0;
            CachedEntryName = null;
            CachedHabitatNames = null;
            MenuStateRegistry.Reset(
                MenuStateRegistry.BESTIARY_LIST,
                MenuStateRegistry.BESTIARY_DETAIL,
                MenuStateRegistry.BESTIARY_FORMATION,
                MenuStateRegistry.BESTIARY_MAP);
            BestiaryNavigationTracker.Instance.Reset();
            BestiaryManualPatches.ResetUpdateControllerState();
        }
    }

    /// <summary>
    /// Patches for the Bestiary (Extra Library) extras menu.
    /// Uses manual Harmony patching to avoid silent failures from attribute-based patches.
    /// </summary>
    internal static class BestiaryManualPatches
    {
        private static int lastSelectState = -1;

        public static void ResetUpdateControllerState()
        {
            lastSelectState = 0;
        }

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch 1: SubSceneManagerExtraLibrary.ChangeState
                var changeStateMethod = AccessTools.Method(
                    typeof(SubSceneManagerExtraLibrary), "ChangeState");
                if (changeStateMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BestiaryManualPatches), nameof(ChangeState_Postfix));
                    harmony.Patch(changeStateMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Bestiary] SubSceneManagerExtraLibrary.ChangeState not found");
                }

                // Patch 2: LibraryMenuController.Show
                var showMethod = AccessTools.Method(
                    typeof(LibraryMenuController_KeyInput), "Show");
                if (showMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BestiaryManualPatches), nameof(LibraryMenuController_Show_Postfix));
                    harmony.Patch(showMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Bestiary] LibraryMenuController.Show not found");
                }

                // Patch 2b: LibraryMenuController.OnContentSelected
                var onContentSelectedMethod = AccessTools.Method(
                    typeof(LibraryMenuController_KeyInput), "OnContentSelected");
                if (onContentSelectedMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BestiaryManualPatches), nameof(LibraryMenuController_OnContentSelected_Postfix));
                    harmony.Patch(onContentSelectedMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Bestiary] LibraryMenuController.OnContentSelected not found");
                }

                // Patch 3: LibraryInfoController.SetData
                var setDataMethod = AccessTools.Method(
                    typeof(LibraryInfoController_KeyInput), "SetData");
                if (setDataMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BestiaryManualPatches), nameof(LibraryInfoController_SetData_Postfix));
                    harmony.Patch(setDataMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Bestiary] LibraryInfoController.SetData not found");
                }

                // Patch 4: ExtraLibraryInfo.OnNextPageButton / OnPreviousPageButton
                Type extraLibraryInfoType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        extraLibraryInfoType = asm.GetType("Il2CppLast.Scene.ExtraLibraryInfo");
                        if (extraLibraryInfoType != null) break;
                    }
                    catch { }
                }

                if (extraLibraryInfoType != null)
                {
                    var nextPageMethod = AccessTools.Method(extraLibraryInfoType, "OnNextPageButton");
                    if (nextPageMethod != null)
                    {
                        var postfix = AccessTools.Method(typeof(BestiaryManualPatches), nameof(OnNextPageButton_Postfix));
                        harmony.Patch(nextPageMethod, postfix: new HarmonyMethod(postfix));
                    }

                    var prevPageMethod = AccessTools.Method(extraLibraryInfoType, "OnPreviousPageButton");
                    if (prevPageMethod != null)
                    {
                        var postfix = AccessTools.Method(typeof(BestiaryManualPatches), nameof(OnPreviousPageButton_Postfix));
                        harmony.Patch(prevPageMethod, postfix: new HarmonyMethod(postfix));
                    }

                    // Patch 5: ExtraLibraryInfo.OnChangedMonster
                    var onChangedMonsterMethod = AccessTools.Method(extraLibraryInfoType, "OnChangedMonster");
                    if (onChangedMonsterMethod != null)
                    {
                        var postfix = AccessTools.Method(typeof(BestiaryManualPatches), nameof(OnChangedMonster_Postfix));
                        harmony.Patch(onChangedMonsterMethod, postfix: new HarmonyMethod(postfix));
                    }
                }

                // Patch 6: LibraryMenuController.UpdateController
                var updateControllerMethod = AccessTools.Method(
                    typeof(LibraryMenuController_KeyInput), "UpdateController");
                if (updateControllerMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BestiaryManualPatches), nameof(UpdateController_Postfix));
                    harmony.Patch(updateControllerMethod, postfix: new HarmonyMethod(postfix));
                }

                // Patch 7: ArBattleTopController.ChangeMonsterParty
                var changeMonsterPartyMethod = AccessTools.Method(
                    typeof(ArBattleTopController), "ChangeMonsterParty");
                if (changeMonsterPartyMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(BestiaryManualPatches), nameof(ChangeMonsterParty_Postfix));
                    harmony.Patch(changeMonsterPartyMethod, postfix: new HarmonyMethod(postfix));
                }

                // Patch 8: ExtraLibraryField.NextMap / PreviousMap
                Type extraLibraryFieldType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        extraLibraryFieldType = asm.GetType("Il2CppLast.Scene.ExtraLibraryField");
                        if (extraLibraryFieldType != null) break;
                    }
                    catch { }
                }

                if (extraLibraryFieldType != null)
                {
                    var nextMapMethod = AccessTools.Method(extraLibraryFieldType, "NextMap");
                    if (nextMapMethod != null)
                    {
                        var postfix = AccessTools.Method(typeof(BestiaryManualPatches), nameof(NextMap_Postfix));
                        harmony.Patch(nextMapMethod, postfix: new HarmonyMethod(postfix));
                    }

                    var prevMapMethod = AccessTools.Method(extraLibraryFieldType, "PreviousMap");
                    if (prevMapMethod != null)
                    {
                        var postfix = AccessTools.Method(typeof(BestiaryManualPatches), nameof(PreviousMap_Postfix));
                        harmony.Patch(prevMapMethod, postfix: new HarmonyMethod(postfix));
                    }
                }

                // Patch 9: MenuExtraLibraryInfo.OnChangedMonster (config menu bestiary)
                Type menuExtraLibraryInfoType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        menuExtraLibraryInfoType = asm.GetType("Il2CppLast.Scene.MenuExtraLibraryInfo");
                        if (menuExtraLibraryInfoType != null) break;
                    }
                    catch { }
                }

                if (menuExtraLibraryInfoType != null)
                {
                    var onChangedMonsterMethod = AccessTools.Method(menuExtraLibraryInfoType, "OnChangedMonster",
                        new Type[] { typeof(MonsterData) });
                    if (onChangedMonsterMethod != null)
                    {
                        var postfix = AccessTools.Method(typeof(BestiaryManualPatches), nameof(MenuExtraLibraryInfo_OnChangedMonster_Postfix));
                        harmony.Patch(onChangedMonsterMethod, postfix: new HarmonyMethod(postfix));
                    }
                }

                MelonLogger.Msg("[Bestiary] Patches applied");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Bestiary] Error applying patches: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 1: State transitions
        // ─────────────────────────────────────────────────────────────────────────

        public static void ChangeState_Postfix(SubSceneManagerExtraLibrary __instance, int state)
        {
            try
            {
                int previousState = BestiaryStateTracker.CurrentState;
                BestiaryStateTracker.CurrentState = state;

                // Clear all bestiary menu states first
                MenuStateRegistry.Reset(
                    MenuStateRegistry.BESTIARY_LIST,
                    MenuStateRegistry.BESTIARY_DETAIL,
                    MenuStateRegistry.BESTIARY_FORMATION,
                    MenuStateRegistry.BESTIARY_MAP);

                switch (state)
                {
                    case 1: // List
                        MenuStateRegistry.SetActive(MenuStateRegistry.BESTIARY_LIST, true);
                        if (previousState <= 0) // Entering bestiary from outside
                        {
                            BestiaryNavigationTracker.Instance.Reset();
                            BestiaryStateTracker.SuppressNextListEntry = true;
                            CoroutineManager.StartManaged(AnnounceListOpen());
                        }
                        else // Returning from detail/map/formation
                        {
                            string reannounce = null;
                            if (previousState == 2 && !string.IsNullOrEmpty(BestiaryStateTracker.CachedEntryName))
                            {
                                reannounce = string.Format(T("Map closed. {0}"), BestiaryStateTracker.CachedEntryName);
                                BestiaryStateTracker.CachedEntryName = null;
                                BestiaryStateTracker.CachedHabitatNames = null;
                            }
                            else
                            {
                                var data = BestiaryNavigationTracker.Instance.CurrentMonsterData;
                                if (data?.pictureBookData != null)
                                    reannounce = BestiaryReader.ReadListEntry(data.pictureBookData);
                            }
                            BestiaryNavigationTracker.Instance.Reset();
                            if (!string.IsNullOrEmpty(reannounce))
                                FFI_ScreenReaderMod.SpeakText(reannounce, true);
                        }
                        break;

                    case 2: // Field (Map)
                        MenuStateRegistry.SetActive(MenuStateRegistry.BESTIARY_MAP, true);
                        BestiaryStateTracker.FullMapIndex = 0;
                        var mapTracker = BestiaryNavigationTracker.Instance;
                        if (mapTracker.CurrentMonsterData != null)
                        {
                            if (mapTracker.CurrentMonsterData.pictureBookData != null)
                                BestiaryStateTracker.CachedEntryName = BestiaryReader.ReadListEntry(mapTracker.CurrentMonsterData.pictureBookData);
                            var habitatList = mapTracker.CurrentMonsterData.HabitatNameList;
                            if (habitatList != null && habitatList.Count > 0)
                            {
                                BestiaryStateTracker.CachedHabitatNames = new List<string>();
                                for (int i = 0; i < habitatList.Count; i++)
                                    BestiaryStateTracker.CachedHabitatNames.Add(habitatList[i] ?? "Unknown location");
                            }
                        }
                        CoroutineManager.StartManaged(AnnounceMapView());
                        break;

                    case 4: // Info (Detail)
                        MenuStateRegistry.SetActive(MenuStateRegistry.BESTIARY_DETAIL, true);
                        break;

                    case 5: // ArTop (Formation)
                        MenuStateRegistry.SetActive(MenuStateRegistry.BESTIARY_FORMATION, true);
                        CoroutineManager.StartManaged(AnnounceFormation());
                        break;

                    case 7: // GotoTitle — leaving bestiary
                        BestiaryStateTracker.ClearState();
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in ChangeState patch: {ex.Message}");
            }
        }

        internal static IEnumerator AnnounceListOpen()
        {
            yield return null;

            try
            {
                var client = PictureBookClient.Instance();
                if (client != null)
                {
                    var list = client.GetPictureBooks();
                    string summary = BestiaryReader.ReadEncounterSummary(list);
                    if (!string.IsNullOrEmpty(summary))
                    {
                        FFI_ScreenReaderMod.SpeakText(summary, interrupt: true);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error announcing list summary: {ex.Message}");
            }

            yield return null;
            yield return null;

            try
            {
                var listController = UnityEngine.Object.FindObjectOfType<LibraryMenuListController_KeyInput>();
                if (listController != null)
                {
                    var data = listController.GetCurrentContent();
                    if (data != null)
                    {
                        BestiaryNavigationTracker.Instance.CurrentMonsterData = data;
                        if (data.pictureBookData != null)
                        {
                            string entry = BestiaryReader.ReadListEntry(data.pictureBookData);
                            if (!string.IsNullOrEmpty(entry))
                                FFI_ScreenReaderMod.SpeakText(entry, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error announcing list open: {ex.Message}");
            }
            finally
            {
                BestiaryStateTracker.SuppressNextListEntry = false;
            }
        }

        private static IEnumerator AnnounceMapView()
        {
            yield return null;
            yield return null;

            try
            {
                var cached = BestiaryStateTracker.CachedHabitatNames;
                if (cached != null && cached.Count > 0)
                {
                    string announcement = string.Format(T("Map open: {0}"), cached[0]);
                    FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                }
                else
                {
                    FFI_ScreenReaderMod.SpeakText(T("Map open"), true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error announcing map: {ex.Message}");
            }
        }

        private static IEnumerator AnnounceFormation()
        {
            float elapsed = 0f;

            while (elapsed < 3f)
            {
                yield return null;
                elapsed += Time.deltaTime;

                try
                {
                    var controller = UnityEngine.Object.FindObjectOfType<ArBattleTopController>();
                    if (controller != null)
                    {
                        var partyList = controller.monsterPartyList;
                        if (partyList != null && partyList.Count > 0)
                        {
                            ReadCurrentFormation(controller);
                            yield break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Bestiary] Error polling formation: {ex.Message}");
                    break;
                }
            }

            FFI_ScreenReaderMod.SpeakText(T("Formation view"), true);
        }

        private static void ReadCurrentFormation(ArBattleTopController controller)
        {
            try
            {
                var partyList = controller.monsterPartyList;
                int partyIndex = controller.selectMonsterPartyIndex;

                if (partyList == null || partyList.Count == 0)
                {
                    FFI_ScreenReaderMod.SpeakText(T("No formations available"), true);
                    return;
                }

                if (partyIndex < 0 || partyIndex >= partyList.Count)
                    partyIndex = 0;

                var party = partyList[partyIndex];
                string announcement = BestiaryReader.ReadFormation(partyIndex, party);

                if (partyList.Count > 1)
                    announcement += $" ({partyIndex + 1} of {partyList.Count})";

                FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error reading formation data: {ex.Message}");
                FFI_ScreenReaderMod.SpeakText(T("Formation view"), true);
            }
        }

        public static void ReannounceFormation()
        {
            if (!BestiaryStateTracker.IsInFormation) return;

            try
            {
                var controller = UnityEngine.Object.FindObjectOfType<ArBattleTopController>();
                if (controller != null)
                {
                    ReadCurrentFormation(controller);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error re-announcing formation: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 2: List entry navigation
        // ─────────────────────────────────────────────────────────────────────────

        public static void LibraryMenuController_Show_Postfix(LibraryMenuController_KeyInput __instance, MonsterData selectData, bool isInit)
        {
            try
            {
                if (selectData == null) return;

                BestiaryNavigationTracker.Instance.CurrentMonsterData = selectData;

                if (!BestiaryStateTracker.IsInList) return;
                if (BestiaryStateTracker.SuppressNextListEntry) return;

                var pbData = selectData.pictureBookData;
                if (pbData == null) return;

                string entry = BestiaryReader.ReadListEntry(pbData);
                if (!string.IsNullOrEmpty(entry))
                {
                    FFI_ScreenReaderMod.SpeakText(entry, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in list Show patch: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 2b: List cursor movement
        // ─────────────────────────────────────────────────────────────────────────

        public static void LibraryMenuController_OnContentSelected_Postfix(int index, MonsterData monsterData)
        {
            try
            {
                if (monsterData == null) return;
                if (!BestiaryStateTracker.IsInList) return;

                BestiaryNavigationTracker.Instance.CurrentMonsterData = monsterData;

                if (BestiaryStateTracker.SuppressNextListEntry) return;

                var pbData = monsterData.pictureBookData;
                if (pbData == null) return;

                string entry = BestiaryReader.ReadListEntry(pbData);
                if (!string.IsNullOrEmpty(entry))
                {
                    FFI_ScreenReaderMod.SpeakText(entry, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in list OnContentSelected patch: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 3: Detail view
        // ─────────────────────────────────────────────────────────────────────────

        public static void LibraryInfoController_SetData_Postfix(LibraryInfoController_KeyInput __instance, MonsterData data)
        {
            try
            {
                if (__instance == null || data == null) return;

                GameObjectCache.Register(__instance);

                var tracker = BestiaryNavigationTracker.Instance;
                tracker.CurrentMonsterData = data;
                tracker.ActiveController = __instance;

                CoroutineManager.StartManaged(DelayedDetailAnnouncement(__instance, data));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in SetData patch: {ex.Message}");
            }
        }

        private static IEnumerator DelayedDetailAnnouncement(LibraryInfoController_KeyInput controller, MonsterData data)
        {
            yield return null;
            yield return null;

            try
            {
                if (controller == null || controller.gameObject == null || !controller.gameObject.activeInHierarchy)
                    yield break;

                var pbData = data.pictureBookData;
                string name = pbData != null && pbData.IsRelease ? pbData.MonsterName : "Unknown";
                string announcement = string.Format(T("{0}. Details"), name);

                FFI_ScreenReaderMod.SpeakText(announcement, true);

                BuildAndInitializeStatBuffer();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in delayed detail announcement: {ex.Message}");
            }
        }

        internal static void BuildAndInitializeStatBuffer()
        {
            try
            {
                var content = UnityEngine.Object.FindObjectOfType<LibraryInfoContent>();
                if (content == null)
                {
                    MelonLogger.Warning("[Bestiary] LibraryInfoContent not found");
                    return;
                }

                var tracker = BestiaryNavigationTracker.Instance;
                var entries = BestiaryReader.BuildStatBuffer(content, tracker.CurrentMonsterData);
                BestiaryNavigationReader.Initialize(entries);

                tracker.IsNavigationActive = entries.Count > 0;

                if (entries.Count > 0)
                    FFI_ScreenReaderMod.SpeakText(entries[0].ToString(), false);

                MenuStateRegistry.SetActive(MenuStateRegistry.BESTIARY_DETAIL, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error building stat buffer: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 4: Page turns in detail view
        // ─────────────────────────────────────────────────────────────────────────

        public static void OnNextPageButton_Postfix()
        {
            try
            {
                if (!BestiaryStateTracker.IsInDetail) return;
                CoroutineManager.StartManaged(PageRebuild());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in OnNextPageButton patch: {ex.Message}");
            }
        }

        public static void OnPreviousPageButton_Postfix()
        {
            try
            {
                if (!BestiaryStateTracker.IsInDetail) return;
                CoroutineManager.StartManaged(PageRebuild());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in OnPreviousPageButton patch: {ex.Message}");
            }
        }

        private static IEnumerator PageRebuild()
        {
            yield return null;
            yield return null;

            try
            {
                BuildAndInitializeStatBuffer();

                var tracker = BestiaryNavigationTracker.Instance;
                var data = tracker.CurrentMonsterData;
                string name = "Unknown";
                if (data?.pictureBookData != null && data.pictureBookData.IsRelease)
                    name = data.pictureBookData.MonsterName;

                FFI_ScreenReaderMod.SpeakText(string.Format(T("{0}. Page changed"), name), true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error rebuilding page: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 5: Monster switching in detail view
        // ─────────────────────────────────────────────────────────────────────────

        public static void OnChangedMonster_Postfix(MonsterData data)
        {
            try
            {
                if (data == null || !BestiaryStateTracker.IsInDetail) return;

                BestiaryNavigationTracker.Instance.CurrentMonsterData = data;

                CoroutineManager.StartManaged(DelayedMonsterChangeAnnouncement(data));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in OnChangedMonster patch: {ex.Message}");
            }
        }

        private static IEnumerator DelayedMonsterChangeAnnouncement(MonsterData data)
        {
            yield return null;
            yield return null;

            try
            {
                var pbData = data.pictureBookData;
                string name = pbData != null && pbData.IsRelease ? pbData.MonsterName : "Unknown";
                FFI_ScreenReaderMod.SpeakText(string.Format(T("{0}. Details"), name), true);

                BuildAndInitializeStatBuffer();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in monster change announcement: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 6: Map change (list view minimap)
        // ─────────────────────────────────────────────────────────────────────────

        public static void UpdateController_Postfix(LibraryMenuController_KeyInput __instance)
        {
            try
            {
                if (!BestiaryStateTracker.IsInList) return;

                int currentState = (int)__instance.selectState;

                if (currentState != lastSelectState && lastSelectState >= 0)
                {
                    if (currentState == 1) // EnlargedMap
                    {
                        var tracker = BestiaryNavigationTracker.Instance;
                        if (tracker.CurrentMonsterData?.pictureBookData != null)
                            BestiaryStateTracker.CachedEntryName = BestiaryReader.ReadListEntry(tracker.CurrentMonsterData.pictureBookData);

                        string mapInfo = T("Minimap open");
                        if (tracker.CurrentMonsterData != null)
                        {
                            string mapName = BestiaryReader.ReadMapName(tracker.CurrentMonsterData, 0);
                            if (!string.IsNullOrEmpty(mapName))
                                mapInfo = string.Format(T("Minimap open: {0}"), mapName);
                        }
                        FFI_ScreenReaderMod.SpeakText(mapInfo, true);
                    }
                    else if (currentState == 0) // MonsterList
                    {
                        string closeMsg = T("Minimap closed");
                        if (!string.IsNullOrEmpty(BestiaryStateTracker.CachedEntryName))
                            closeMsg += $". {BestiaryStateTracker.CachedEntryName}";
                        BestiaryStateTracker.CachedEntryName = null;
                        FFI_ScreenReaderMod.SpeakText(closeMsg, true);
                    }
                }
                lastSelectState = currentState;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in UpdateController patch: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 7: Formation rearrange
        // ─────────────────────────────────────────────────────────────────────────

        public static void ChangeMonsterParty_Postfix()
        {
            if (!BestiaryStateTracker.IsInFormation) return;
            CoroutineManager.StartManaged(DelayedReannounceFormation());
        }

        private static IEnumerator DelayedReannounceFormation()
        {
            yield return null;
            ReannounceFormation();
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 8: Full map cycling
        // ─────────────────────────────────────────────────────────────────────────

        public static void NextMap_Postfix()
        {
            if (!BestiaryStateTracker.IsInMap) return;
            CoroutineManager.StartManaged(AnnounceFullMapCycle(1));
        }

        public static void PreviousMap_Postfix()
        {
            if (!BestiaryStateTracker.IsInMap) return;
            CoroutineManager.StartManaged(AnnounceFullMapCycle(-1));
        }

        private static IEnumerator AnnounceFullMapCycle(int direction)
        {
            yield return null;

            try
            {
                var cached = BestiaryStateTracker.CachedHabitatNames;
                if (cached == null || cached.Count == 0) yield break;

                int count = cached.Count;
                BestiaryStateTracker.FullMapIndex += direction;
                if (BestiaryStateTracker.FullMapIndex >= count)
                    BestiaryStateTracker.FullMapIndex = 0;
                else if (BestiaryStateTracker.FullMapIndex < 0)
                    BestiaryStateTracker.FullMapIndex = count - 1;

                string mapName = (BestiaryStateTracker.FullMapIndex < cached.Count)
                    ? cached[BestiaryStateTracker.FullMapIndex]
                    : null;
                if (!string.IsNullOrEmpty(mapName))
                    FFI_ScreenReaderMod.SpeakText(mapName, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error announcing map cycle: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 9: Config menu bestiary monster switching
        // ─────────────────────────────────────────────────────────────────────────

        public static void MenuExtraLibraryInfo_OnChangedMonster_Postfix(MonsterData data)
        {
            try
            {
                if (data == null || !BestiaryStateTracker.IsInDetail) return;

                BestiaryNavigationTracker.Instance.CurrentMonsterData = data;

                CoroutineManager.StartManaged(DelayedConfigMonsterChangeAnnouncement(data));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in config OnChangedMonster patch: {ex.Message}");
            }
        }

        private static IEnumerator DelayedConfigMonsterChangeAnnouncement(MonsterData data)
        {
            yield return null;
            yield return null;

            try
            {
                var pbData = data.pictureBookData;
                string name = pbData != null && pbData.IsRelease ? pbData.MonsterName : "Unknown";
                FFI_ScreenReaderMod.SpeakText(string.Format(T("{0}. Details"), name), true);

                BuildAndInitializeStatBuffer();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Bestiary] Error in config monster change announcement: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Config menu bestiary state handler
        // ─────────────────────────────────────────────────────────────────────────

        internal static class ConfigBestiaryStateHandler
        {
            private static int _previousState = -1;
            public static bool WasInConfigBestiary { get; private set; } = false;

            public static void HandleStateChange(int mainGameState)
            {
                try
                {
                    int previousBestiaryState = BestiaryStateTracker.CurrentState;

                    if (mainGameState == 17) // MenuLibraryUi = list
                    {
                        WasInConfigBestiary = true;
                        BestiaryStateTracker.CurrentState = 1;

                        MenuStateRegistry.Reset(
                            MenuStateRegistry.BESTIARY_LIST,
                            MenuStateRegistry.BESTIARY_DETAIL);
                        MenuStateRegistry.SetActive(MenuStateRegistry.BESTIARY_LIST, true);

                        if (previousBestiaryState <= 0)
                        {
                            BestiaryNavigationTracker.Instance.Reset();
                            BestiaryStateTracker.SuppressNextListEntry = true;
                            CoroutineManager.StartManaged(AnnounceListOpen());
                        }
                        else if (previousBestiaryState == 4)
                        {
                            BestiaryNavigationTracker.Instance.Reset();

                            var listController = UnityEngine.Object.FindObjectOfType<LibraryMenuListController_KeyInput>();
                            if (listController != null)
                            {
                                var data = listController.GetCurrentContent();
                                if (data != null)
                                {
                                    BestiaryNavigationTracker.Instance.CurrentMonsterData = data;
                                    if (data.pictureBookData != null)
                                    {
                                        string entry = BestiaryReader.ReadListEntry(data.pictureBookData);
                                        if (!string.IsNullOrEmpty(entry))
                                            FFI_ScreenReaderMod.SpeakText(entry, true);
                                    }
                                }
                            }
                        }
                    }
                    else if (mainGameState == 18) // MenuLibraryInfo = detail
                    {
                        WasInConfigBestiary = true;
                        BestiaryStateTracker.CurrentState = 4;

                        MenuStateRegistry.Reset(
                            MenuStateRegistry.BESTIARY_LIST,
                            MenuStateRegistry.BESTIARY_DETAIL);
                        MenuStateRegistry.SetActive(MenuStateRegistry.BESTIARY_DETAIL, true);

                        // Title-screen branch announces + builds the navigable stat buffer
                        // via LibraryInfoController.SetData postfix. The config branch goes
                        // through a different controller that the mod doesn't hook, so on
                        // fresh entry (not a list→detail re-entry within the same session)
                        // we trigger the same announcement manually so the player isn't
                        // dropped silently into the detail view.
                        if (previousBestiaryState != 4)
                        {
                            var data = BestiaryNavigationTracker.Instance.CurrentMonsterData;
                            if (data != null)
                                CoroutineManager.StartManaged(DelayedConfigMonsterChangeAnnouncement(data));
                        }
                    }

                    _previousState = mainGameState;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Bestiary] Error in ConfigBestiaryStateHandler: {ex.Message}");
                }
            }

            public static void HandleExit()
            {
                try
                {
                    WasInConfigBestiary = false;
                    _previousState = -1;
                    BestiaryStateTracker.ClearState();
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Bestiary] Error in ConfigBestiaryStateHandler exit: {ex.Message}");
                }
            }
        }
    }
}
