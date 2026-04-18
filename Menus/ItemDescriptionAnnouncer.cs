using System;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Patches;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Menus
{
    /// <summary>
    /// Announces the description text of the currently-focused inventory/key item.
    /// Triggered by the I key in the items menu.
    /// </summary>
    public static class ItemDescriptionAnnouncer
    {
        public static void AnnounceCurrentDescription()
        {
            try
            {
                var itemData = ItemMenuState.LastSelectedItem;
                if (itemData == null)
                {
                    FFI_ScreenReaderMod.SpeakText(T("No description"), interrupt: true);
                    return;
                }

                string description = itemData.Description;
                if (string.IsNullOrWhiteSpace(description))
                {
                    FFI_ScreenReaderMod.SpeakText(T("No description"), interrupt: true);
                    return;
                }

                description = TextUtils.StripIconMarkup(description);
                if (string.IsNullOrWhiteSpace(description))
                {
                    FFI_ScreenReaderMod.SpeakText(T("No description"), interrupt: true);
                    return;
                }

                FFI_ScreenReaderMod.SpeakText(description.Trim(), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemDescription] Error: {ex.Message}");
            }
        }
    }
}
