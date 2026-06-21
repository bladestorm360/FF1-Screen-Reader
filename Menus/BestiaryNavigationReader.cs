using System;
using System.Collections.Generic;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Patches;

namespace FFI_ScreenReader.Menus
{
    /// <summary>
    /// Virtual buffer navigation for bestiary detail stats. Builds a shared <see cref="NavigationBuffer"/>
    /// of the visible stats (with dynamic group boundaries + names) and delegates all navigation to it.
    /// </summary>
    public static class BestiaryNavigationReader
    {
        private static NavigationBuffer buffer = null;

        /// <summary>
        /// Initialize the stat buffer from the current detail view's UI elements.
        /// Called when entering the bestiary detail view.
        /// </summary>
        public static void Initialize(List<BestiaryStatEntry> entries)
        {
            var funcs = new List<Func<string>>();
            var groupStarts = new List<int>();
            var groupNames = new List<string>();

            if (entries != null && entries.Count > 0)
            {
                BestiaryStatGroup lastGroup = entries[0].Group;
                groupStarts.Add(0);
                groupNames.Add(GetGroupDisplayName(entries[0].Group));

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i]; // capture per-iteration
                    funcs.Add(() => entry.ToString());

                    if (i > 0 && entry.Group != lastGroup)
                    {
                        groupStarts.Add(i);
                        groupNames.Add(GetGroupDisplayName(entry.Group));
                        lastGroup = entry.Group;
                    }
                }
            }

            buffer = new NavigationBuffer(funcs, groupStarts, groupNames);
        }

        /// <summary>Clear navigation state.</summary>
        public static void Reset()
        {
            buffer = null;
        }

        /// <summary>Whether navigation is currently active (populated buffer + the tracker is active).</summary>
        public static bool IsActive => buffer != null && !buffer.IsEmpty &&
                                        BestiaryNavigationTracker.Instance.IsNavigationActive;

        public static void NavigateNext() => Move(b => b.Next());
        public static void NavigatePrevious() => Move(b => b.Previous());
        public static void JumpToNextGroup() => Move(b => b.NextGroup());
        public static void JumpToPreviousGroup() => Move(b => b.PreviousGroup());
        public static void JumpToTop() => Move(b => b.JumpTop());
        public static void JumpToBottom() => Move(b => b.JumpBottom());
        public static void ReadCurrentStat() => Move(b => b.Current());

        // All wrap/group logic lives in NavigationBuffer; this just gates + speaks.
        private static void Move(Func<NavigationBuffer, string> op)
        {
            if (!IsActive) return;
            string value = op(buffer);
            if (!string.IsNullOrEmpty(value))
            {
                var (localIndex, groupCount) = buffer.CurrentGroupPosition();
                value = FFI_ScreenReader.Utils.MenuPosition.Format(value, localIndex, groupCount);
                FFI_ScreenReaderMod.SpeakText(value, true);
            }
        }

        /// <summary>Get display-friendly name for a stat group.</summary>
        private static string GetGroupDisplayName(BestiaryStatGroup group)
        {
            switch (group)
            {
                case BestiaryStatGroup.MonsterData: return "Monster Data";
                case BestiaryStatGroup.Status: return "Status";
                case BestiaryStatGroup.Options: return "Rewards";
                case BestiaryStatGroup.Items: return "Items";
                case BestiaryStatGroup.Properties: return "Properties";
                default: return "Other";
            }
        }
    }
}
