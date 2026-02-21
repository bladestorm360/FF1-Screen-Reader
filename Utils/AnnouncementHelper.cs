using System;
using System.Collections;
using FFI_ScreenReader.Core;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Helper methods for common announcement patterns.
    /// Combines deduplication check + speak into single calls.
    /// </summary>
    public static class AnnouncementHelper
    {
        /// <summary>
        /// Announces text only if it hasn't been announced recently for this context.
        /// Combines AnnouncementDeduplicator.ShouldAnnounce + SpeakText into one call.
        /// Returns true if the announcement was made (text was new).
        /// </summary>
        public static bool AnnounceIfNew(string context, string text, bool interrupt = false)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (!AnnouncementDeduplicator.ShouldAnnounce(context, text)) return false;
            FFI_ScreenReaderMod.SpeakText(text, interrupt: interrupt);
            return true;
        }

        /// <summary>
        /// Announces text using a dedup key that may differ from the spoken text.
        /// Useful when dedup should track an index/ID but announce a descriptive string.
        /// Returns true if the announcement was made.
        /// </summary>
        public static bool AnnounceIfNew(string context, object dedupKey, string text, bool interrupt = false)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (!AnnouncementDeduplicator.ShouldAnnounce(context, dedupKey)) return false;
            FFI_ScreenReaderMod.SpeakText(text, interrupt: interrupt);
            return true;
        }

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

        /// <summary>
        /// Coroutine that waits one frame, then announces with dedup check.
        /// Returns without speaking if the text was already announced for this context.
        /// </summary>
        public static IEnumerator DelayedAnnounceIfNew(string context, Func<string> textGetter, bool interrupt = false)
        {
            yield return null;
            string text = textGetter();
            if (!string.IsNullOrEmpty(text))
                AnnounceIfNew(context, text, interrupt);
        }
    }
}
