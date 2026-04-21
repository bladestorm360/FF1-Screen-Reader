using System;
using System.Collections;
using FFI_ScreenReader.Core;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Helper coroutines for announcements that need to wait for UI to settle
    /// before reading state. No deduplication — every patch that calls into here
    /// is already at a discrete event boundary.
    /// </summary>
    public static class AnnouncementHelper
    {
        /// <summary>
        /// Coroutine that waits one frame, then speaks the result of textGetter.
        /// Common pattern used across many patches for reading UI state after it updates.
        /// </summary>
        public static IEnumerator DelayedSpeak(Func<string> textGetter, bool interrupt = false)
        {
            yield return null;
            string text = textGetter();
            if (!string.IsNullOrEmpty(text))
                FFI_ScreenReaderMod.SpeakText(text, interrupt: interrupt);
        }

        /// <summary>
        /// Coroutine that waits the specified number of frames, then speaks the result of textGetter.
        /// </summary>
        public static IEnumerator DelayedSpeak(int frames, Func<string> textGetter, bool interrupt = false)
        {
            for (int i = 0; i < frames; i++)
                yield return null;
            string text = textGetter();
            if (!string.IsNullOrEmpty(text))
                FFI_ScreenReaderMod.SpeakText(text, interrupt: interrupt);
        }

        /// <summary>
        /// Coroutine that waits the specified number of seconds, then speaks the result of textGetter.
        /// </summary>
        public static IEnumerator DelayedSpeakSeconds(float seconds, Func<string> textGetter, bool interrupt = false)
        {
            yield return new UnityEngine.WaitForSeconds(seconds);
            string text = textGetter();
            if (!string.IsNullOrEmpty(text))
                FFI_ScreenReaderMod.SpeakText(text, interrupt: interrupt);
        }
    }
}
