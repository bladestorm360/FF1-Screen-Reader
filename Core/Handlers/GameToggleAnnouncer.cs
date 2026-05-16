using System;
using MelonLoader;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Core.Handlers
{
    /// <summary>
    /// Per-frame poller for the two game-side toggles the player can flip from many sources:
    /// random encounters (R3 in-game / cheat menu / F3 historically) and auto-dash (L3 in-game
    /// / cheat menu / F1 historically). Announces only when the value changes, so the speech
    /// always reflects the actual state regardless of which path toggled it.
    ///
    /// Light polling — two property reads per frame, gated on UserDataManager being present.
    /// Granted as an explicit exception to the no-polling rule.
    /// </summary>
    internal static class GameToggleAnnouncer
    {
        private static bool? lastEncounter;
        private static int? lastAutoDash;

        internal static void Poll()
        {
            try
            {
                var ud = Il2CppLast.Management.UserDataManager.Instance();
                if (ud == null) return;

                var cheat = ud.CheatSettingsData;
                if (cheat != null)
                {
                    bool e = cheat.IsEnableEncount;
                    // First poll seeds the cache silently; subsequent changes announce.
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
