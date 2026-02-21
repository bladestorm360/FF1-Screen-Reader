using System;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Tracks popup state for handling in CursorNavigation.
    /// </summary>
    public static class PopupState
    {
        /// <summary>
        /// True when a confirmation popup is active.
        /// Delegates to MenuStateRegistry for centralized state tracking.
        /// </summary>
        public static bool IsConfirmationPopupActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.POPUP);
            private set => MenuStateRegistry.SetActive(MenuStateRegistry.POPUP, value);
        }

        /// <summary>
        /// The type name of the current popup.
        /// </summary>
        public static string CurrentPopupType { get; private set; }

        /// <summary>
        /// Pointer to the active popup instance.
        /// </summary>
        public static IntPtr ActivePopupPtr { get; private set; }

        /// <summary>
        /// Offset to commandList field (-1 if popup has no buttons).
        /// </summary>
        public static int CommandListOffset { get; private set; }

        public static void SetActive(string typeName, IntPtr ptr, int cmdListOffset)
        {
            IsConfirmationPopupActive = true;
            CurrentPopupType = typeName;
            ActivePopupPtr = ptr;
            CommandListOffset = cmdListOffset;
        }

        public static void Clear()
        {
            IsConfirmationPopupActive = false;
            CurrentPopupType = null;
            ActivePopupPtr = IntPtr.Zero;
            CommandListOffset = -1;
        }

        /// <summary>
        /// Returns true if popup with buttons is active (suppress MenuTextDiscovery).
        /// </summary>
        public static bool ShouldSuppress() => IsConfirmationPopupActive && CommandListOffset >= 0;
    }
}
