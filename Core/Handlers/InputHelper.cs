using UnityEngine;

namespace FFI_ScreenReader.Core.Handlers
{
    /// <summary>
    /// Shared input utility methods for handler classes.
    /// </summary>
    internal static class InputHelper
    {
        internal static bool IsShiftHeld()
        {
            return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }

        internal static bool IsCtrlHeld()
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }
    }
}
