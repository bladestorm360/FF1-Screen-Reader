using System;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Placeholder for cursor navigation patches.
    /// Actual patching is done manually in FFI_ScreenReaderMod.TryPatchCursorNavigation()
    /// to avoid IL2CPP string parameter crashes with attribute-based patches.
    /// </summary>
    public static class CursorNavigationPatches
    {
        // Patching is handled manually in the main mod class
        // See FFI_ScreenReaderMod.TryPatchCursorNavigation()
    }
}
