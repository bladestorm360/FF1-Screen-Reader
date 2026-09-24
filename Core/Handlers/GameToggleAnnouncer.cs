using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Patches;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Core.Handlers
{
    /// <summary>
    /// Announces the two game-side field toggles when the player flips them on the field: random
    /// encounters and auto-dash (walk/run).
    ///
    /// Event-driven (round 2, 2026-09-24; replaces a per-frame poll of both flags). Hooks the CLIENT
    /// setters, which are separate, unique bodies (the data-class setter CheatSettingsData
    /// .set_IsEnableEncount shares its body with 22 setters):
    ///  - Last.Management.CheatSettingsClient.SetIsEnableEncount(bool) — RVA 0x6D3BC0
    ///  - Last.Management.ConfigClient.SetIsAutoDash(int)             — RVA 0x9AD550
    /// Callers: FieldMap.UpdatePlayerStatePlay (the field toggle keys), the config menu
    /// (ConfigActualDetailsControllerBase) and, for encounters, the save load (SaveSlotManager
    /// .GotoLoadSaveData). The prefix records the old value; the postfix speaks only for a real change
    /// made while the main game is in its Player state (SubSceneManagerMainGame.State.Player = 3 — the
    /// only state whose update, FieldMap.UpdatePlayer, runs UpdatePlayerStatePlay). A config-menu change
    /// (state Menu) or a save load therefore stays silent; the config menu reads its own row.
    /// </summary>
    internal static class GameToggleAnnouncer
    {
        // Old value captured by the prefix; null when it could not be read (then the postfix is silent).
        private static bool? _encounterBefore;
        private static int? _autoDashBefore;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            Patch(harmony, typeof(Il2CppLast.Management.CheatSettingsClient), "SetIsEnableEncount", typeof(bool),
                nameof(SetIsEnableEncount_Prefix), nameof(SetIsEnableEncount_Postfix));
            Patch(harmony, typeof(Il2CppLast.Management.ConfigClient), "SetIsAutoDash", typeof(int),
                nameof(SetIsAutoDash_Prefix), nameof(SetIsAutoDash_Postfix));
        }

        private static void Patch(HarmonyLib.Harmony harmony, Type type, string method, Type argType, string prefix, string postfix)
        {
            try
            {
                var target = AccessTools.Method(type, method, new[] { argType });
                if (target == null)
                {
                    MelonLogger.Warning($"[GameToggleAnnouncer] {type.Name}.{method} not found");
                    return;
                }
                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(GameToggleAnnouncer).GetMethod(prefix, BindingFlags.Public | BindingFlags.Static)),
                    postfix: new HarmonyMethod(typeof(GameToggleAnnouncer).GetMethod(postfix, BindingFlags.Public | BindingFlags.Static)));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameToggleAnnouncer] Error patching {type.Name}.{method}: {ex.Message}");
            }
        }

        // ── Encounters (CheatSettingsClient.SetIsEnableEncount) ──
        public static void SetIsEnableEncount_Prefix()
        {
            _encounterBefore = null;
            try
            {
                var cheat = Il2CppLast.Management.UserDataManager.Instance()?.CheatSettingsData;
                if (cheat != null)
                    _encounterBefore = cheat.IsEnableEncount;
            }
            catch { } // unreadable → stay silent
        }

        public static void SetIsEnableEncount_Postfix(bool __0)
        {
            try
            {
                bool? before = _encounterBefore;
                _encounterBefore = null;
                if (!before.HasValue || before.Value == __0) return;   // not a real change
                if (!GameStatePatches.IsFieldPlayerState()) return;    // config menu / save load
                FFI_ScreenReaderMod.SpeakText(__0 ? T("Encounters on") : T("Encounters off"), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameToggleAnnouncer] {ex.Message}");
            }
        }

        // ── Auto-dash / walk-run (ConfigClient.SetIsAutoDash) ──
        public static void SetIsAutoDash_Prefix()
        {
            _autoDashBefore = null;
            try
            {
                var cfg = Il2CppLast.Management.UserDataManager.Instance()?.Config;
                if (cfg != null)
                    _autoDashBefore = cfg.IsAutoDash;
            }
            catch { } // unreadable → stay silent
        }

        public static void SetIsAutoDash_Postfix(int __0)
        {
            try
            {
                int? before = _autoDashBefore;
                _autoDashBefore = null;
                if (!before.HasValue || (before.Value != 0) == (__0 != 0)) return;   // not a real change
                if (!GameStatePatches.IsFieldPlayerState()) return;                  // config menu
                FFI_ScreenReaderMod.SpeakText(__0 != 0 ? T("Run") : T("Walk"), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameToggleAnnouncer] {ex.Message}");
            }
        }
    }
}
