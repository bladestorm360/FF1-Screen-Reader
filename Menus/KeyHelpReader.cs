using System;
using System.Collections.Generic;
using Il2CppLast.UI.KeyInput;
using UnityEngine;
using UnityEngine.UI;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Menus
{
    /// <summary>
    /// Reads the controls display. Two independent features:
    ///   • Shift+I — reads the on-screen control-hint bar (the persistent KeyHelpController) at once
    ///     via a scene scan (AnnounceKeyHelp). Works on the field, in menus, anywhere.
    ///   • Arrow/WASD — steps the dedicated "Gamepad/Keyboard Controls" pop-up one entry at a time
    ///     via a shared NavigationBuffer, gated to KeyContext.KeyHelp.
    ///
    /// IMPORTANT: the buffer is armed ONLY from the real controls pop-up — ConfigKeysSettingController's
    /// GamePad/Keyboard Help state (ConfigMenuPatches feeds us the pre-rendered entries via
    /// <see cref="OpenControlsHelp"/>). It is NOT armed off the hint bar's KeyHelpController.SetValues:
    /// the title-screen Options menu hosts its hint bar under OptionController, so arming off SetValues
    /// hijacked that menu's arrows (KeyContext flipped to KeyHelp) and produced a double read.
    /// </summary>
    public static class KeyHelpReader
    {
        // KeyHelpController.view (KeyHelpView) — private field, no public accessor. (Shift+I scan only.)
        private const int OFFSET_VIEW = 0x18;

        private static NavigationBuffer buffer = null;
        private static bool primed = false;

        // The controls pop-up state. helpOwner validates the screen is still on-screen (cheap, per-frame
        // safe — no scene scan) so a missed close can't leave KeyContext.KeyHelp stuck.
        private static bool helpActive = false;
        private static ConfigKeysSettingController helpOwner = null;

        /// <summary>
        /// Called by ConfigMenuPatches when the Gamepad/Keyboard Controls pop-up opens, with the
        /// already-rendered entry strings (action + binding). Builds the navigation buffer and
        /// announces the first entry as the initial focus.
        /// </summary>
        public static void OpenControlsHelp(ConfigKeysSettingController owner, List<string> entries)
        {
            helpOwner = owner;
            if (entries == null || entries.Count == 0)
            {
                buffer = null;
                helpActive = false;
                primed = false;
                return;
            }

            var funcs = new List<Func<string>>(entries.Count);
            foreach (var entry in entries) { var s = entry; funcs.Add(() => s); }
            buffer = new NavigationBuffer(funcs);
            helpActive = true;
            primed = true;            // entry[0] is announced now; first Down moves to entry[1].
            Speak(buffer.Current());  // initial focus
        }

        /// <summary>Called when the pop-up closes / returns to the controls list.</summary>
        public static void CloseControlsHelp()
        {
            helpActive = false;
            helpOwner = null;
            buffer = null;
            primed = false;
        }

        /// <summary>True while the controls pop-up is on-screen — drives KeyContext.KeyHelp.</summary>
        public static bool IsScreenActive
        {
            get
            {
                try
                {
                    if (!helpActive) return false;
                    if (helpOwner == null || helpOwner.gameObject == null || !helpOwner.gameObject.activeInHierarchy)
                    {
                        CloseControlsHelp();
                        return false;
                    }
                    return true;
                }
                catch { CloseControlsHelp(); return false; }
            }
        }

        // ── Navigation (arrows + WASD, gated to KeyContext.KeyHelp) ──
        public static void NavigateNext()
        {
            if (buffer == null || buffer.IsEmpty) return;
            Speak(primed ? buffer.Next() : Prime());
        }

        public static void NavigatePrevious()
        {
            if (buffer == null || buffer.IsEmpty) return;
            Speak(primed ? buffer.Previous() : Prime());
        }

        public static void JumpToTop()
        {
            if (buffer == null || buffer.IsEmpty) return;
            primed = true;
            Speak(buffer.JumpTop());
        }

        public static void JumpToBottom()
        {
            if (buffer == null || buffer.IsEmpty) return;
            primed = true;
            Speak(buffer.JumpBottom());
        }

        // Fallback orient-at-top if the open announce was missed.
        private static string Prime()
        {
            primed = true;
            return buffer.Current();
        }

        private static void Speak(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            // Flat controls list → whole-buffer position. buffer.Index is already updated by the
            // nav op that produced `s`, so CurrentGroupPosition reflects the entry being spoken.
            if (buffer != null && !buffer.IsEmpty)
            {
                var (localIndex, groupCount) = buffer.CurrentGroupPosition();
                s = MenuPosition.Format(s, localIndex, groupCount);
            }
            FFI_ScreenReaderMod.SpeakText(s, interrupt: true);
        }

        /// <summary>Shift+I: read every visible control hint at once (on-screen KeyHelpController).</summary>
        public static void AnnounceKeyHelp()
        {
            try
            {
                var entries = ReadVisibleEntries();
                string result = (entries != null && entries.Count > 0)
                    ? string.Join(", ", entries)
                    : T("No controls displayed");
                FFI_ScreenReaderMod.SpeakText(result, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in AnnounceKeyHelp: {ex.Message}");
                FFI_ScreenReaderMod.SpeakText(T("Error reading controls"), interrupt: true);
            }
        }

        // Reads each visible hint entry's Text components → "{action}: {key}" from the on-screen
        // KeyHelpController (scene scan). Independent of the controls-pop-up buffer.
        private static unsafe List<string> ReadVisibleEntries()
        {
            var controller = GameObjectCache.Get<KeyHelpController>() ?? GameObjectCache.Refresh<KeyHelpController>();
            if (controller == null || controller.gameObject == null || !controller.gameObject.activeInHierarchy)
                return null;

            // Read private 'view' field (KeyHelpView) at offset 0x18 via unsafe pointer.
            IntPtr controllerPtr = controller.Pointer;
            IntPtr viewPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_VIEW);
            if (viewPtr == IntPtr.Zero) return null;

            var view = new KeyHelpView(viewPtr);
            var contentsParent = view.ContentsParent;
            if (contentsParent == null) return null;

            var contentsTransform = contentsParent.transform;
            if (contentsTransform == null || contentsTransform.childCount == 0) return null;

            var entries = new List<string>();
            // Each child is a control entry (KeyIconController). The game deactivates entries on other
            // pages, so activeInHierarchy filters to the visible page.
            for (int i = 0; i < contentsTransform.childCount; i++)
            {
                var child = contentsTransform.GetChild(i);
                if (child == null || child.gameObject == null || !child.gameObject.activeInHierarchy)
                    continue;

                var texts = child.GetComponentsInChildren<Text>(false);
                if (texts == null) continue;

                var parts = new List<string>();
                foreach (var txt in texts)
                {
                    if (txt != null && !string.IsNullOrWhiteSpace(txt.text))
                        parts.Add(txt.text.Trim());
                }
                if (parts.Count > 0)
                    entries.Add(string.Join(": ", parts));
            }

            return entries.Count > 0 ? entries : null;
        }
    }
}
