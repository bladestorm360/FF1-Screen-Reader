using System;
using MelonLoader;
using FFI_ScreenReader.Utils;

// Type aliases for IL2CPP types
using BattleUIManager = Il2CppLast.UI.BattleUIManager;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Tracks battle pause menu state by reading game memory directly.
    /// When active, bypasses BattleCommandState suppression so MenuTextDiscovery can read.
    /// </summary>
    public static class BattlePauseState
    {
        /// <summary>
        /// Checks if battle pause menu is active by reading game memory directly.
        /// This avoids needing to hook methods that don't fire at runtime.
        /// </summary>
        public static bool IsActive
        {
            get
            {
                try
                {
                    // Get BattleUIManager singleton
                    var uiManager = BattleUIManager.Instance;
                    if (uiManager == null) return false;

                    // Must be initialized (actually in battle) before reading pause state
                    // Without this check, garbage memory values outside battle can cause false positives
                    if (!uiManager.Initialized) return false;

                    // Read pauseController pointer (FF1 specific)
                    IntPtr uiManagerPtr = uiManager.Pointer;
                    IntPtr pauseControllerPtr = IL2CppFieldReader.ReadPointerSafe(uiManagerPtr, IL2CppOffsets.BattlePause.PauseController);
                    if (pauseControllerPtr == IntPtr.Zero) return false;

                    // Read isActivePauseMenu bool
                    return IL2CppFieldReader.ReadBool(pauseControllerPtr, IL2CppOffsets.BattlePause.IsActivePauseMenu);
                }
                catch
                {
                    // If anything fails, assume not active
                    return false;
                }
            }
        }

        public static void Reset()
        {
            // No-op - state is read directly from game memory
        }
    }

    /// <summary>
    /// Minimal class for battle pause menu support.
    /// State detection via BattlePauseState (direct memory read).
    /// Popup button reading handled by PopupPatches.CommonPopup_UpdateFocus_Postfix.
    /// </summary>
    public static class BattlePausePatches
    {
        /// <summary>
        /// Apply battle pause menu patches.
        /// Note: CommonPopup.UpdateFocus is now patched in PopupPatches.cs for all popup button reading.
        /// State clearing for Return to Title is handled by TitleMenuCommandController.SetEnableMainMenu.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            // Battle pause uses direct memory read for pause state detection
            // FF1: pauseController offset = 0x98 (differs from FF3's 0x90)
            // Popup button reading handled by PopupPatches
        }

        /// <summary>
        /// Reset state (called when battle ends).
        /// </summary>
        public static void Reset()
        {
            BattlePauseState.Reset();
        }
    }
}
