using System;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Patches;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Menus
{
    /// <summary>
    /// Announces the description text of the currently-focused spell in the magic menu.
    /// Triggered by the I key when the magic (field) menu is active.
    /// </summary>
    public static class MagicDetailsAnnouncer
    {
        public static void AnnounceCurrentSpellDescription()
        {
            try
            {
                var ability = MagicMenuState.LastAnnouncedAbility;
                if (ability == null)
                {
                    FFI_ScreenReaderMod.SpeakText(T("No description"), interrupt: true);
                    return;
                }

                string description = MagicMenuState.GetSpellDescription(ability);
                if (string.IsNullOrWhiteSpace(description))
                {
                    FFI_ScreenReaderMod.SpeakText(T("No description"), interrupt: true);
                    return;
                }

                FFI_ScreenReaderMod.SpeakText(description.Trim(), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MagicDetails] Error: {ex.Message}");
            }
        }
    }
}
