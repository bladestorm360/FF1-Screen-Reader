using System;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Tracker for location/map name announcements.
    /// Uses content-based matching (no timers) to prevent duplicates.
    /// E.g., "Cornelia 1F" is skipped if "Entering Cornelia 1F" was just announced.
    /// </summary>
    public static class LocationMessageTracker
    {
        private static string lastMapTransitionMessage = "";
        private static bool inMapTransition = false;

        /// <summary>
        /// Record a map transition announcement and mark that we're in a transition.
        /// Called from MapTransitionPatches before announcing "Entering X".
        /// </summary>
        public static void SetLastMapTransition(string message)
        {
            lastMapTransitionMessage = message?.Trim() ?? "";
            inMapTransition = !string.IsNullOrEmpty(lastMapTransitionMessage);
        }

        /// <summary>
        /// Check if a fade/system message should be announced.
        /// Returns false if:
        /// - The message is contained in the last map transition (duplicate)
        /// - No transition fired but this looks like a location (menu open case)
        /// </summary>
        public static bool ShouldAnnounceFadeMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            string trimmed = message.Trim();

            // If we're in a map transition (SetCurrentMapId_Postfix fired)
            if (inMapTransition && !string.IsNullOrEmpty(lastMapTransitionMessage))
            {
                // Skip if this message is contained in the transition message
                // E.g., "Cornelia 1F" contained in "Entering Cornelia 1F"
                if (lastMapTransitionMessage.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else if (!inMapTransition)
            {
                // No transition fired - this might be menu open or other UI event
                // Skip if it looks like a short location name (1-4 words, no punctuation)
                // This prevents location names from being announced when opening menu
                if (LooksLikeLocationName(trimmed))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Check if a string looks like a location name.
        /// Location names are typically 1-4 words without special punctuation.
        /// </summary>
        private static bool LooksLikeLocationName(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // If it has sentence-like punctuation, it's probably a system message
            if (text.Contains('.') || text.Contains('!') || text.Contains('?'))
                return false;

            // Count words (simple split)
            string[] words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // Location names are typically 1-4 words (e.g., "Overworld", "Cornelia 1F", "Chaos Shrine 1F")
            // System messages tend to be longer
            return words.Length <= 4;
        }

        /// <summary>
        /// Reset state on scene transition.
        /// </summary>
        public static void Reset()
        {
            lastMapTransitionMessage = "";
            inMapTransition = false;
        }
    }
}
