using FFI_ScreenReader.Utils;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Tracks whether the FF1 15-puzzle minigame is active.
    /// Delegates to MenuStateRegistry so the on-demand position key (keyboard I /
    /// controller right-stick up) can be context-gated like every other menu.
    ///
    /// Side effect: while active, MenuStateRegistry.AnyActive() is true, which makes
    /// ControllerRouter.IsFieldActive false during the puzzle (suppressing field
    /// hotkeys/entity scanning) — desired, since the puzzle is modal.
    /// </summary>
    public static class PuzzleGameState
    {
        /// <summary>
        /// True while the 15-puzzle is showing and accepting input.
        /// </summary>
        public static bool IsActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.PUZZLE_GAME);
            set => MenuStateRegistry.SetActive(MenuStateRegistry.PUZZLE_GAME, value);
        }
    }
}
