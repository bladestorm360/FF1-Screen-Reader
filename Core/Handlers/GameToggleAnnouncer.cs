using System;
using Il2CppLast.OutGame.Library;
using MelonLoader;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Core.Handlers
{
    /// <summary>
    /// Per-frame poller for the two game-side toggles the player can flip from many sources:
    /// random encounters (R3 in-game / cheat menu / F3 historically) and walk/run
    /// (F1 in-game / L3 in-game / cheat menu). Announces only when the value changes inside
    /// active field gameplay; map transitions and menus silently reset the seed so the next
    /// valid poll doesn't false-announce a context shift as a toggle.
    ///
    /// Walk/run reads the live FieldKeyController.dashFlag XOR Config.IsAutoDash via
    /// MoveStateHelper.GetDashFlag(), so every source surfaces through the same announce.
    /// </summary>
    internal static class GameToggleAnnouncer
    {
        private static bool? lastEncounter;
        private static bool? lastRunning;

        internal static void Poll()
        {
            try
            {
                // Out-of-context (loading, menus, battle, scene transition): silently re-seed
                // so we never report a state shift caused by entering/leaving a context.
                if (!ControllerRouter.IsFieldActive)
                {
                    lastEncounter = null;
                    lastRunning = null;
                    return;
                }

                var ud = Il2CppLast.Management.UserDataManager.Instance();
                if (ud == null) return;

                var cheat = ud.CheatSettingsData;
                if (cheat != null)
                {
                    bool e = cheat.IsEnableEncount;
                    if (lastEncounter.HasValue && lastEncounter.Value != e)
                        FFI_ScreenReaderMod.SpeakText(e ? T("Encounters on") : T("Encounters off"), interrupt: true);
                    lastEncounter = e;
                }

                // Skip walk/run until a FieldKeyController exists in the scene — otherwise
                // GetDashFlag falls back to AutoDash alone and we'd announce when the
                // controller finally loads on the next frame.
                var fkc = GameObjectCache.Get<FieldKeyController>() ?? GameObjectCache.Refresh<FieldKeyController>();
                if (fkc == null)
                {
                    lastRunning = null;
                    return;
                }

                bool running = MoveStateHelper.GetDashFlag();
                if (lastRunning.HasValue && lastRunning.Value != running)
                    FFI_ScreenReaderMod.SpeakText(running ? T("Run") : T("Walk"), interrupt: true);
                lastRunning = running;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameToggleAnnouncer] {ex.Message}");
            }
        }

        internal static void Reset()
        {
            lastEncounter = null;
            lastRunning = null;
        }
    }
}
