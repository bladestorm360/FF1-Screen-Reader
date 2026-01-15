using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Battle menu state tracking classes.
    /// These provide infrastructure for suppressing MenuTextDiscovery during battle.
    /// Actual battle announcement patches will be added when battle speech is implemented.
    /// </summary>

    /// <summary>
    /// Tracks battle command menu state (Attack, Magic, Item, Defend, Run).
    /// </summary>
    public static class BattleCommandState
    {
        /// <summary>
        /// True when battle command selection is active.
        /// </summary>
        public static bool IsActive { get; set; } = false;

        /// <summary>
        /// The last selected command index (0=Attack, 1=Magic, 2=Items, 3=Defend/Run).
        /// Used to detect stale callbacks from magic/item menus after backing out.
        /// </summary>
        public static int LastSelectedCommandIndex { get; set; } = -1;

        private static string lastAnnouncement = "";
        private static float lastAnnouncementTime = 0f;

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// Simply returns IsActive - battle command state is cleared explicitly on battle end.
        /// </summary>
        public static bool ShouldSuppress()
        {
            return IsActive;
        }

        /// <summary>
        /// Check if announcement should be made (deduplication).
        /// </summary>
        public static bool ShouldAnnounce(string announcement)
        {
            float currentTime = Time.time;
            if (announcement == lastAnnouncement && (currentTime - lastAnnouncementTime) < 0.1f)
                return false;

            lastAnnouncement = announcement;
            lastAnnouncementTime = currentTime;
            return true;
        }

        /// <summary>
        /// Clears battle command state.
        /// </summary>
        public static void ClearState()
        {
            IsActive = false;
            LastSelectedCommandIndex = -1;
            lastAnnouncement = "";
            lastAnnouncementTime = 0f;
        }

        /// <summary>
        /// Alias for ClearState() for consistency with other patches.
        /// </summary>
        public static void ResetState() => ClearState();
    }

    /// <summary>
    /// Tracks battle target selection state (enemy/ally targeting).
    /// </summary>
    public static class BattleTargetState
    {
        /// <summary>
        /// True when target selection is active.
        /// </summary>
        public static bool IsTargetSelectionActive { get; set; } = false;

        private static string lastAnnouncement = "";
        private static float lastAnnouncementTime = 0f;

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// </summary>
        public static bool ShouldSuppress()
        {
            return IsTargetSelectionActive;
        }

        /// <summary>
        /// Sets target selection active state.
        /// </summary>
        public static void SetTargetSelectionActive(bool active)
        {
            MelonLogger.Msg($"[Battle Target] SetTargetSelectionActive: {active}");
            IsTargetSelectionActive = active;
            if (active)
            {
                // Clear magic menu state when entering target selection
                // Prevents leftover dataList from causing "Empty" announcements
                BattleMagicMenuState.IsActive = false;
                BattleItemMenuState.IsActive = false;
            }
            else
            {
                lastAnnouncement = "";
                lastAnnouncementTime = 0f;
            }
        }

        /// <summary>
        /// Check if announcement should be made (deduplication).
        /// </summary>
        public static bool ShouldAnnounce(string announcement)
        {
            float currentTime = Time.time;
            if (announcement == lastAnnouncement && (currentTime - lastAnnouncementTime) < 0.1f)
                return false;

            lastAnnouncement = announcement;
            lastAnnouncementTime = currentTime;
            return true;
        }

        /// <summary>
        /// Clears target selection state.
        /// </summary>
        public static void ClearState()
        {
            IsTargetSelectionActive = false;
            lastAnnouncement = "";
            lastAnnouncementTime = 0f;
        }

        /// <summary>
        /// Alias for ClearState() for consistency with other patches.
        /// </summary>
        public static void ResetState() => ClearState();
    }

    /// <summary>
    /// Tracks battle item menu state.
    /// </summary>
    public static class BattleItemMenuState
    {
        /// <summary>
        /// True when battle item selection is active.
        /// </summary>
        public static bool IsActive { get; set; } = false;

        private static string lastAnnouncement = "";
        private static float lastAnnouncementTime = 0f;

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// </summary>
        public static bool ShouldSuppress()
        {
            return IsActive;
        }

        /// <summary>
        /// Check if announcement should be made (deduplication).
        /// </summary>
        public static bool ShouldAnnounce(string announcement)
        {
            float currentTime = Time.time;
            if (announcement == lastAnnouncement && (currentTime - lastAnnouncementTime) < 0.1f)
                return false;

            lastAnnouncement = announcement;
            lastAnnouncementTime = currentTime;
            return true;
        }

        /// <summary>
        /// Resets battle item menu state.
        /// </summary>
        public static void Reset()
        {
            IsActive = false;
            lastAnnouncement = "";
            lastAnnouncementTime = 0f;
        }

        /// <summary>
        /// Alias for Reset() for consistency with other patches.
        /// </summary>
        public static void ResetState() => Reset();
    }

    /// <summary>
    /// Tracks battle magic menu state.
    /// </summary>
    public static class BattleMagicMenuState
    {
        /// <summary>
        /// True when battle magic selection is active.
        /// </summary>
        public static bool IsActive { get; set; } = false;

        private static string lastAnnouncement = "";
        private static float lastAnnouncementTime = 0f;

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// </summary>
        public static bool ShouldSuppress()
        {
            return IsActive;
        }

        /// <summary>
        /// Check if announcement should be made (deduplication).
        /// </summary>
        public static bool ShouldAnnounce(string announcement)
        {
            float currentTime = Time.time;
            if (announcement == lastAnnouncement && (currentTime - lastAnnouncementTime) < 0.1f)
                return false;

            lastAnnouncement = announcement;
            lastAnnouncementTime = currentTime;
            return true;
        }

        /// <summary>
        /// Resets battle magic menu state.
        /// </summary>
        public static void Reset()
        {
            IsActive = false;
            lastAnnouncement = "";
            lastAnnouncementTime = 0f;
        }

        /// <summary>
        /// Alias for Reset() for consistency with other patches.
        /// </summary>
        public static void ResetState() => Reset();
    }

    /// <summary>
    /// Static helper class for battle state management.
    /// </summary>
    public static class BattleStateHelper
    {
        /// <summary>
        /// True when we're actually in a battle.
        /// Set true on battle start, false on battle end.
        /// Used to guard state clearing so it only happens during real battle transitions.
        /// </summary>
        public static bool IsInBattle { get; private set; } = false;

        /// <summary>
        /// Called when battle starts to mark that we're in battle.
        /// </summary>
        public static void OnBattleStart()
        {
            IsInBattle = true;
            MelonLogger.Msg("[Battle] Battle started - IsInBattle = true");
        }

        /// <summary>
        /// Clears all battle menu state flags.
        /// Called from BattleResultPatches when victory screen appears.
        /// </summary>
        public static void ClearAllBattleMenuFlags()
        {
            BattleCommandState.ClearState();
            BattleTargetState.ClearState();
            BattleItemMenuState.Reset();
            BattleMagicMenuState.Reset();
            MelonLogger.Msg("[Battle] Cleared all battle menu flags");
        }

        /// <summary>
        /// Clears all states on battle end, but ONLY if we're actually in a battle.
        /// This guards against being called during game initialization.
        /// </summary>
        public static void TryClearOnBattleEnd()
        {
            if (!IsInBattle)
            {
                // Not in battle - skip clearing (prevents startup loops)
                return;
            }

            IsInBattle = false;
            ClearAllBattleMenuFlags();
            MelonLogger.Msg("[Battle] Battle ended - IsInBattle = false");
        }
    }

    /// <summary>
    /// Patches for BattleController to ensure state is cleared on ALL battle end scenarios.
    /// Uses IsInBattle flag to guard against being called during game initialization.
    /// </summary>
    public static class BattleControllerPatches
    {
        private static bool isPatched = false;

        /// <summary>
        /// Apply patches for battle controller end detection.
        /// NOTE: OnDestroy is NOT patched - it causes crashes during asset loading.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched) return;

            try
            {
                var controllerType = typeof(Il2CppLast.Battle.BattleController);

                // Only patch fade-out callbacks - OnDestroy crashes during asset loading
                PatchFadeOutCallbacks(harmony, controllerType);

                isPatched = true;
                MelonLogger.Msg("[Battle Controller] Patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Battle Controller] Error applying patches: {ex.Message}");
            }
        }

        private static void PatchFadeOutCallbacks(HarmonyLib.Harmony harmony, Type controllerType)
        {
            var callbacks = new[] {
                "EndWinFadeOutCallback",
                "EndLoseFadeOutCallback",
                "EndEscapeFadeOut",
                "EndFadeOutCallback"
            };

            foreach (var callbackName in callbacks)
            {
                try
                {
                    var method = controllerType.GetMethod(callbackName,
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    if (method != null)
                    {
                        harmony.Patch(method,
                            postfix: new HarmonyMethod(typeof(BattleControllerPatches), nameof(FadeOut_Postfix)));
                        MelonLogger.Msg($"[Battle Controller] Patched {callbackName}");
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Called on fade-out callbacks (win, lose, escape).
        /// Only clears if IsInBattle is true.
        /// </summary>
        public static void FadeOut_Postfix(object __instance)
        {
            try
            {
                // Guard against null/invalid instances during initialization
                if (__instance == null) return;

                BattleStateHelper.TryClearOnBattleEnd();
            }
            catch (Exception ex)
            {
                // Silently ignore - this can happen during game initialization
                MelonLogger.Warning($"[Battle Controller] FadeOut error (likely during init): {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patches for MainMenuController to clear battle states when returning to field.
    /// MainMenuController uses Show()/Close() methods, NOT SetActive.
    /// </summary>
    public static class MainMenuControllerPatches
    {
        private static bool isPatched = false;

        /// <summary>
        /// Apply patches for MainMenuController.Show() method.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched) return;

            try
            {
                // Try KeyInput namespace first (FF1 uses this for controller input)
                Type controllerType = null;
                try
                {
                    controllerType = typeof(Il2CppLast.UI.KeyInput.MainMenuController);
                }
                catch
                {
                    MelonLogger.Warning("[MainMenu Patch] MainMenuController type not found in KeyInput namespace");
                }

                if (controllerType == null)
                {
                    MelonLogger.Warning("[MainMenu Patch] Could not find MainMenuController type");
                    return;
                }

                // Find Show(bool) method - this is called when main menu opens
                var showMethod = controllerType.GetMethod("Show",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(bool) },
                    null);

                if (showMethod != null)
                {
                    harmony.Patch(showMethod,
                        postfix: new HarmonyMethod(typeof(MainMenuControllerPatches), nameof(Show_Postfix)));
                    MelonLogger.Msg("[MainMenu Patch] Patched MainMenuController.Show successfully");
                    isPatched = true;
                }
                else
                {
                    // Try to find any Show method variant
                    var methods = controllerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                    foreach (var m in methods)
                    {
                        if (m.Name == "Show")
                        {
                            harmony.Patch(m,
                                postfix: new HarmonyMethod(typeof(MainMenuControllerPatches), nameof(Show_Postfix)));
                            MelonLogger.Msg($"[MainMenu Patch] Patched MainMenuController.Show (alt signature)");
                            isPatched = true;
                            break;
                        }
                    }

                    if (!isPatched)
                    {
                        MelonLogger.Warning("[MainMenu Patch] Show method not found on MainMenuController");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MainMenu Patch] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when MainMenuController.Show is called.
        /// Clears all menu states when main menu command bar is shown.
        /// This handles returning from character selection (Magic/Equip/Status/Order) to main menu.
        /// </summary>
        public static void Show_Postfix()
        {
            try
            {
                // Always clear all menu states when main menu is shown
                // This handles backing out from character selection screens
                ManualPatches.ClearAllMenuStates();

                // When main menu opens after battle, also clear battle states
                if (BattleStateHelper.IsInBattle)
                {
                    MelonLogger.Msg("[MainMenu] Show() while IsInBattle - clearing all battle states");
                    BattleStateHelper.TryClearOnBattleEnd();
                    BattleCommandPatches.ClearCachedTargetController();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MainMenu Patch] Error in Show_Postfix: {ex.Message}");
            }
        }
    }
}
