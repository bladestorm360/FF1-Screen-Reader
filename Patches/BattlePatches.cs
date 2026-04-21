using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

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
        private static readonly MenuStateInstance _state = new MenuStateInstance();
        static BattleCommandState() => _state.RegisterResetHandler();

        public static bool IsActive { get => _state.IsActive; set => _state.IsActive = value; }
        public static bool ShouldSuppress() => _state.ShouldSuppress();
        public static void ClearState() => _state.ClearState();
        public static void ResetState() => _state.ResetState();

        /// <summary>
        /// The last selected command index (0=Attack, 1=Magic, 2=Items, 3=Defend/Run).
        /// Used to detect stale callbacks from magic/item menus after backing out.
        /// </summary>
        public static int LastSelectedCommandIndex { get; set; } = -1;

        private class MenuStateInstance : MenuStateBase
        {
            protected override string RegistryKey => MenuStateRegistry.BATTLE_COMMAND;
            protected override void OnReset()
            {
                LastSelectedCommandIndex = -1;
                base.OnReset();
            }
        }
    }

    /// <summary>
    /// Tracks battle target selection state (enemy/ally targeting).
    /// </summary>
    public static class BattleTargetState
    {
        private static readonly MenuStateInstance _state = new MenuStateInstance();
        static BattleTargetState() => _state.RegisterResetHandler();

        public static bool IsTargetSelectionActive { get => _state.IsActive; set => _state.IsActive = value; }
        public static bool ShouldSuppress() => _state.ShouldSuppress();
        public static void ClearState() => _state.ClearState();
        public static void ResetState() => _state.ResetState();

        /// <summary>
        /// Sets target selection active state.
        /// </summary>
        public static void SetTargetSelectionActive(bool active)
        {
            IsTargetSelectionActive = active;
            if (active)
            {
                // Clear magic menu state when entering target selection
                // Prevents leftover dataList from causing "Empty" announcements
                BattleMagicMenuState.IsActive = false;
                BattleItemMenuState.IsActive = false;
            }
        }

        private class MenuStateInstance : MenuStateBase
        {
            protected override string RegistryKey => MenuStateRegistry.BATTLE_TARGET;
        }
    }

    /// <summary>
    /// Tracks battle item menu state.
    /// </summary>
    public static class BattleItemMenuState
    {
        private static readonly MenuStateInstance _state = new MenuStateInstance();
        static BattleItemMenuState() => _state.RegisterResetHandler();

        public static bool IsActive { get => _state.IsActive; set => _state.IsActive = value; }
        public static bool ShouldSuppress() => _state.ShouldSuppress();
        public static void Reset() => _state.ClearState();
        public static void ResetState() => _state.ResetState();

        private class MenuStateInstance : MenuStateBase
        {
            protected override string RegistryKey => MenuStateRegistry.BATTLE_ITEM;
        }
    }

    /// <summary>
    /// Tracks battle magic menu state.
    /// </summary>
    public static class BattleMagicMenuState
    {
        private static readonly MenuStateInstance _state = new MenuStateInstance();
        static BattleMagicMenuState() => _state.RegisterResetHandler();

        public static bool IsActive { get => _state.IsActive; set => _state.IsActive = value; }
        public static bool ShouldSuppress() => _state.ShouldSuppress();
        public static void Reset() => _state.ClearState();
        public static void ResetState() => _state.ResetState();

        private class MenuStateInstance : MenuStateBase
        {
            protected override string RegistryKey => MenuStateRegistry.BATTLE_MAGIC;
        }
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
        }

        /// <summary>
        /// Clears all battle menu state flags.
        /// Called from BattleResultPatches when victory screen appears.
        /// </summary>
        public static void ClearAllBattleMenuFlags()
        {
            MenuStateRegistry.Reset(
                MenuStateRegistry.BATTLE_COMMAND,
                MenuStateRegistry.BATTLE_TARGET,
                MenuStateRegistry.BATTLE_ITEM,
                MenuStateRegistry.BATTLE_MAGIC
            );
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
            BattleStartPatches.ResetState();
        }

        /// <summary>
        /// Force clears battle state. Used as fallback when normal clearing fails.
        /// Called when Tab is pressed to open main menu (indicating player is on field, not in battle).
        /// </summary>
        public static void ForceClearBattleState()
        {
            if (IsInBattle)
            {
                IsInBattle = false;
                ClearAllBattleMenuFlags();
            }
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
                    }
                }
                catch { }
            }

            // Patch Exit() method - most reliable hook for battle end
            // Exit(bool isUnloadAsset = true) is called when battle truly ends, regardless of win/lose/escape path
            try
            {
                var exitMethod = controllerType.GetMethod("Exit",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(bool) },
                    null);

                if (exitMethod != null)
                {
                    harmony.Patch(exitMethod,
                        prefix: new HarmonyMethod(typeof(BattleControllerPatches), nameof(Exit_Prefix)));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Controller] Failed to patch Exit: {ex.Message}");
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

        /// <summary>
        /// Called when BattleController.Exit() is invoked - reliable battle end detection.
        /// This is the most reliable hook for battle end as it's called regardless of win/lose/escape path.
        /// </summary>
        public static void Exit_Prefix(object __instance)
        {
            try
            {
                if (__instance == null) return;
                BattleStateHelper.TryClearOnBattleEnd();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Controller] Exit error: {ex.Message}");
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
                            isPatched = true;
                            break;
                        }
                    }

                    if (!isPatched)
                    {
                        MelonLogger.Warning("[MainMenu Patch] Show method not found on MainMenuController");
                    }
                }

                // Patch Close() to clear MAIN_MENU state when menu closes
                var closeMethod = controllerType.GetMethod("Close",
                    BindingFlags.Public | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
                if (closeMethod != null)
                {
                    harmony.Patch(closeMethod,
                        postfix: new HarmonyMethod(typeof(MainMenuControllerPatches), nameof(Close_Postfix)));
                }
                else
                {
                    MelonLogger.Warning("[MainMenu Patch] Close method not found on MainMenuController");
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
                // Clear sub-menu states when returning to main menu command bar
                ManualPatches.ClearAllMenuStates();

                // Mark main menu as active so ControllerRouter knows we're in a menu
                MenuStateRegistry.SetActive(MenuStateRegistry.MAIN_MENU, true);

                // When main menu opens after battle, also clear battle states
                if (BattleStateHelper.IsInBattle)
                {
                    BattleStateHelper.TryClearOnBattleEnd();
                    BattleCommandPatches.ClearCachedTargetController();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MainMenu Patch] Error in Show_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when MainMenuController.Close is called.
        /// Clears MAIN_MENU state so field controls resume.
        /// </summary>
        public static void Close_Postfix()
        {
            MenuStateRegistry.SetActive(MenuStateRegistry.MAIN_MENU, false);
        }
    }
}
