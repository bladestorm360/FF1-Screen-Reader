using System;
using MelonLoader;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Core.Handlers
{
    /// <summary>
    /// Per-frame poller for the two game-side toggles the player can flip from many sources:
    /// random encounters (R3 in-game / cheat menu) and auto-dash / walk/run (F1 in-game /
    /// L3 in-game / cheat menu / config menu). Announces only when the value changes inside
    /// active field gameplay; map transitions and menus silently reset the seed so the next
    /// valid poll doesn't false-announce a context shift as a toggle.
    /// </summary>
    internal static class GameToggleAnnouncer
    {
        private static bool? lastEncounter;
        private static int? lastAutoDash;

        internal static void Poll()
        {
            try
            {
                // Out-of-context (loading, menus, battle, scene transition): silently re-seed
                // so we never report a state shift caused by entering/leaving a context.
                if (!ControllerRouter.IsFieldActive)
                {
                    lastEncounter = null;
                    lastAutoDash = null;
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

                var cfg = ud.Config;
                if (cfg != null)
                {
                    int a = cfg.IsAutoDash;
                    if (lastAutoDash.HasValue && lastAutoDash.Value != a)
                        FFI_ScreenReaderMod.SpeakText(a != 0 ? T("Run") : T("Walk"), interrupt: true);
                    lastAutoDash = a;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameToggleAnnouncer] {ex.Message}");
            }
        }

        internal static void Reset()
        {
            lastEncounter = null;
            lastAutoDash = null;
        }
    }
}
