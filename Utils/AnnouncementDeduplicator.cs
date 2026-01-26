using System.Collections.Generic;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Centralized deduplication for screen reader announcements.
    /// Uses context keys to separate different announcement sources.
    /// Supports string, index, and object-based deduplication.
    /// </summary>
    public static class AnnouncementDeduplicator
    {
        private static readonly Dictionary<string, string> _lastStrings = new Dictionary<string, string>();
        private static readonly Dictionary<string, int> _lastInts = new Dictionary<string, int>();
        private static readonly Dictionary<string, object> _lastObjects = new Dictionary<string, object>();

        /// <summary>
        /// Checks if a string announcement should be spoken (different from last).
        /// Updates tracking if announcement is new.
        /// </summary>
        /// <param name="context">Unique context key (e.g., "BattleCommand.Select")</param>
        /// <param name="text">The announcement text</param>
        /// <returns>True if announcement should be spoken, false if duplicate</returns>
        public static bool ShouldAnnounce(string context, string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            if (_lastStrings.TryGetValue(context, out var last) && last == text)
                return false;

            _lastStrings[context] = text;
            return true;
        }

        /// <summary>
        /// Checks if an index-based announcement should be spoken (different from last).
        /// Updates tracking if index is new.
        /// </summary>
        /// <param name="context">Unique context key (e.g., "BattleCommand.Cursor")</param>
        /// <param name="index">The current index</param>
        /// <returns>True if announcement should be spoken, false if duplicate</returns>
        public static bool ShouldAnnounce(string context, int index)
        {
            if (_lastInts.TryGetValue(context, out var last) && last == index)
                return false;

            _lastInts[context] = index;
            return true;
        }

        /// <summary>
        /// Checks if an object reference announcement should be spoken (different from last).
        /// Uses reference equality for comparison - different instances with same text will both announce.
        /// Use this for battle actions where multiple enemies with the same name should each be announced.
        /// </summary>
        /// <param name="context">Unique context key (e.g., "BattleAction")</param>
        /// <param name="obj">The object reference</param>
        /// <returns>True if announcement should be spoken, false if duplicate</returns>
        public static bool ShouldAnnounce(string context, object obj)
        {
            if (obj == null)
                return false;

            if (_lastObjects.TryGetValue(context, out var last) && ReferenceEquals(last, obj))
                return false;

            _lastObjects[context] = obj;
            return true;
        }

        /// <summary>
        /// Resets tracking for a specific context.
        /// Call this when a menu opens/closes or state changes.
        /// </summary>
        public static void Reset(string context)
        {
            _lastStrings.Remove(context);
            _lastInts.Remove(context);
            _lastObjects.Remove(context);
        }

        /// <summary>
        /// Resets tracking for multiple contexts at once.
        /// </summary>
        public static void Reset(params string[] contexts)
        {
            foreach (var context in contexts)
            {
                Reset(context);
            }
        }

        /// <summary>
        /// Clears all tracking. Call on major state transitions (e.g., battle end).
        /// </summary>
        public static void ResetAll()
        {
            _lastStrings.Clear();
            _lastInts.Clear();
            _lastObjects.Clear();
        }
    }
}
