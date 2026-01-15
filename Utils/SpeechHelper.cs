using System.Collections;
using FFI_ScreenReader.Core;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Shared speech helper utilities for all patches.
    /// Provides delayed speech coroutines to prevent race conditions.
    /// </summary>
    internal static class SpeechHelper
    {
        /// <summary>
        /// Coroutine that speaks text after one frame delay.
        /// Use with CoroutineManager.StartManaged().
        /// This prevents race conditions when multiple patches fire in sequence.
        /// </summary>
        /// <param name="text">Text to speak.</param>
        internal static IEnumerator DelayedSpeech(string text)
        {
            yield return null; // Wait one frame
            FFI_ScreenReaderMod.SpeakText(text);
        }

        /// <summary>
        /// Coroutine that speaks text after one frame delay without interrupting.
        /// Use for queued announcements that shouldn't cut off previous speech.
        /// </summary>
        /// <param name="text">Text to speak.</param>
        internal static IEnumerator DelayedSpeechNoInterrupt(string text)
        {
            yield return null; // Wait one frame
            FFI_ScreenReaderMod.SpeakText(text, interrupt: false);
        }
    }
}
