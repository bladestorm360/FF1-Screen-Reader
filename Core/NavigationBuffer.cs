using System;
using System.Collections.Generic;

namespace FFI_ScreenReader.Core
{
    /// <summary>
    /// Shared virtual-buffer navigation. Holds an ordered list of entries — each produced by a
    /// <see cref="Func{String}"/> so a feature can read live values (status stats) or return a
    /// pre-rendered string (bestiary, key-help) — plus optional group boundaries. Owns ALL the
    /// index wrap/clamp and group-jump logic so feature readers (status, bestiary, key-help) no
    /// longer duplicate it. Holds DATA only; the caller speaks the returned string.
    ///
    /// Index is public so a feature that persists position elsewhere (e.g. the status tracker) can
    /// sync it in/out around a navigation call. Group navigation is a no-op (returns the current
    /// entry) when no groups were supplied; group names are optional — when present, group jumps
    /// prefix "GroupName. " (bestiary), when absent they don't (status).
    /// </summary>
    public sealed class NavigationBuffer
    {
        private readonly List<Func<string>> entries;
        private readonly List<int> groupStarts;   // null/empty => no groups
        private readonly List<string> groupNames;  // parallel to groupStarts; null => no group-name prefix

        public NavigationBuffer(List<Func<string>> entries, List<int> groupStarts = null, List<string> groupNames = null)
        {
            this.entries = entries ?? new List<Func<string>>();
            this.groupStarts = groupStarts;
            this.groupNames = groupNames;
            Index = 0;
        }

        public int Index { get; set; }
        public int Count => entries.Count;
        public bool IsEmpty => entries.Count == 0;

        /// <summary>The announce string for the current entry (null if empty). Clamps a stray index.</summary>
        public string Current()
        {
            if (IsEmpty) return null;
            if (Index < 0 || Index >= entries.Count) Index = 0;
            return entries[Index]?.Invoke();
        }

        /// <summary>Advance one entry (wraps to top) and return its announce string.</summary>
        public string Next()
        {
            if (IsEmpty) return null;
            Index = (Index + 1) % entries.Count;
            return Current();
        }

        /// <summary>Step back one entry (wraps to bottom) and return its announce string.</summary>
        public string Previous()
        {
            if (IsEmpty) return null;
            Index--;
            if (Index < 0) Index = entries.Count - 1;
            return Current();
        }

        public string JumpTop()
        {
            if (IsEmpty) return null;
            Index = 0;
            return Current();
        }

        public string JumpBottom()
        {
            if (IsEmpty) return null;
            Index = entries.Count - 1;
            return Current();
        }

        /// <summary>Jump to the first entry of the next group (wraps to the first group). No groups → current entry.</summary>
        public string NextGroup()
        {
            if (IsEmpty) return null;
            if (groupStarts == null || groupStarts.Count == 0) return Current();

            int gi = 0, target = groupStarts[0];
            for (int i = 0; i < groupStarts.Count; i++)
            {
                if (groupStarts[i] > Index) { gi = i; target = groupStarts[i]; break; }
            }
            Index = target;
            return WithGroup(gi);
        }

        /// <summary>Jump to the first entry of the previous group (wraps to the last group). No groups → current entry.</summary>
        public string PreviousGroup()
        {
            if (IsEmpty) return null;
            if (groupStarts == null || groupStarts.Count == 0) return Current();

            int gi = groupStarts.Count - 1, target = groupStarts[gi];
            for (int i = groupStarts.Count - 1; i >= 0; i--)
            {
                if (groupStarts[i] < Index) { gi = i; target = groupStarts[i]; break; }
            }
            Index = target;
            return WithGroup(gi);
        }

        private string WithGroup(int groupIndex)
        {
            string cur = Current();
            if (groupNames != null && groupIndex >= 0 && groupIndex < groupNames.Count)
            {
                string name = groupNames[groupIndex];
                if (!string.IsNullOrWhiteSpace(name))
                    return $"{name}. {cur}";
            }
            return cur;
        }
    }
}
