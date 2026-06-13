using System;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Patches;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Menus
{
    /// <summary>
    /// Announces the description text of the currently-focused inventory/key item.
    /// Triggered by the I key / right-stick-up in the items menu.
    ///
    /// Reads the live UI panel directly (mirrors ShopMenuTracker.GetDescriptionFromUI),
    /// not item master data, so it reflects exactly what the game has rendered — the one
    /// place FF1's item detail was still reading from the database.
    /// </summary>
    public static class ItemDescriptionAnnouncer
    {
        // Last.UI.ItemWindowView.descriptionText (live item info panel).
        private const int OFFSET_ITEM_DESCRIPTION_TEXT = 0x18;

        public static void AnnounceCurrentDescription()
        {
            try
            {
                string description = GetDescriptionFromUI();
                if (string.IsNullOrWhiteSpace(description))
                {
                    FFI_ScreenReaderMod.SpeakText(T("No description"), interrupt: true);
                    return;
                }

                FFI_ScreenReaderMod.SpeakText(TextUtils.StripIconMarkup(description).Trim(), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemDescription] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// ItemWindowView -> descriptionText (0x18) -> .text
        /// </summary>
        private static string GetDescriptionFromUI()
        {
            try
            {
                var view = UnityEngine.Object.FindObjectOfType<Il2CppLast.UI.ItemWindowView>();
                if (view == null) return null;

                IntPtr viewPtr = view.Pointer;
                if (viewPtr == IntPtr.Zero) return null;

                IntPtr textPtr = IL2CppFieldReader.ReadPointer(viewPtr, OFFSET_ITEM_DESCRIPTION_TEXT);
                if (textPtr == IntPtr.Zero) return null;

                var text = new UnityEngine.UI.Text(textPtr);
                return text?.text;
            }
            catch
            {
                return null;
            }
        }
    }
}
