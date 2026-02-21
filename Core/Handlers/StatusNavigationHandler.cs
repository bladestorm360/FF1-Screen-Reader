using UnityEngine;
using FFI_ScreenReader.Menus;

namespace FFI_ScreenReader.Core.Handlers
{
    /// <summary>
    /// Handles arrow key navigation through status screen stats.
    /// Returns true if input was consumed (status navigation is active).
    /// </summary>
    internal static class StatusNavigationHandler
    {
        internal static bool HandleInput()
        {
            var tracker = StatusNavigationTracker.Instance;

            if (!tracker.IsNavigationActive || !tracker.ValidateState())
                return false;

            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                if (InputHelper.IsCtrlHeld())
                {
                    StatusNavigationReader.JumpToTop();
                }
                else if (InputHelper.IsShiftHeld())
                {
                    StatusNavigationReader.JumpToPreviousGroup();
                }
                else
                {
                    StatusNavigationReader.NavigatePrevious();
                }
                return true;
            }

            if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                if (InputHelper.IsCtrlHeld())
                {
                    StatusNavigationReader.JumpToBottom();
                }
                else if (InputHelper.IsShiftHeld())
                {
                    StatusNavigationReader.JumpToNextGroup();
                }
                else
                {
                    StatusNavigationReader.NavigateNext();
                }
                return true;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                StatusNavigationReader.ReadCurrentStat();
                return true;
            }

            return false;
        }
    }
}
