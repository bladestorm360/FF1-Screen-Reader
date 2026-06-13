using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppInterop.Runtime.InteropTypes;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Screen-reader support for FF1's 15-puzzle minigame
    /// (Serial.FF1.MiniGame.MiniGamePuzzleController).
    ///
    /// The puzzle uses a custom controller (not Il2CppLast.UI.Cursor), and the tile
    /// numbers are drawn as sprites (no Text component), so neither the generic cursor
    /// announcer nor MenuTextDiscovery can read it. This patch hooks the controller's
    /// cursor-movement methods, derives the tile number from each piece's index field,
    /// and speaks it.
    ///
    /// - Arrowing over a tile speaks the number only ("7") or "empty".
    /// - On-demand position key (keyboard I / controller right-stick up) speaks the
    ///   current row and column via AnnouncePosition().
    /// </summary>
    public static class PuzzlePatches
    {
        // Pointer to the live MiniGamePuzzleController, cached so AnnouncePosition()
        // can read the cursor without re-finding the object. IL2CPP's GC is non-moving,
        // so a raw pointer is stable for the object's lifetime (same pattern as PopupState).
        private static IntPtr activeControllerPtr = IntPtr.Zero;

        // Dedup: skip re-announcing the same cursor position (e.g. two move methods firing,
        // or coroutines coalescing during fast arrowing).
        private static int lastAnnouncedCursorPos = -1;

        // One-time board dump per puzzle session (resolves pieceList ordering + index mapping).
        private static bool boardLogged = false;

        static PuzzlePatches()
        {
            // Fires on Close_Postfix AND on MenuStateRegistry.ResetAll (scene change / abort),
            // so the cached pointer can never get stuck across sessions.
            MenuStateRegistry.RegisterResetHandler(MenuStateRegistry.PUZZLE_GAME, ResetLocalState);
        }

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type ctrl = FindType("Il2CppSerial.FF1.MiniGame.MiniGamePuzzleController");
                if (ctrl == null)
                {
                    MelonLogger.Warning("[Puzzle] MiniGamePuzzleController not found - puzzle patches disabled");
                    return;
                }

                // Lifecycle: turn state on after the shuffle (Show), off on Close.
                PatchPostfix(harmony, ctrl, "Show", nameof(Show_Postfix));
                PatchPostfix(harmony, ctrl, "Close", nameof(Close_Postfix));

                // Cursor movement → announce the tile under the cursor.
                PatchPostfix(harmony, ctrl, "NextPiece", nameof(Move_Postfix));
                PatchPostfix(harmony, ctrl, "PrevPiece", nameof(Move_Postfix));
                PatchPostfix(harmony, ctrl, "SkipNextPeice", nameof(Move_Postfix)); // game's spelling
                PatchPostfix(harmony, ctrl, "SkipPrevPeice", nameof(Move_Postfix)); // game's spelling
                PatchPostfix(harmony, ctrl, "TouchPiece", nameof(Move_Postfix));

                MelonLogger.Msg("[Puzzle] Patches applied");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Puzzle] Error applying patches: {ex.Message}\n{ex.StackTrace}");
            }
        }

        // ---- Lifecycle postfixes -------------------------------------------------

        public static void Show_Postfix(object __instance)
        {
            try
            {
                activeControllerPtr = (__instance as Il2CppObjectBase)?.Pointer ?? IntPtr.Zero;
                lastAnnouncedCursorPos = -1;
                boardLogged = false;
                PuzzleGameState.IsActive = true;
                MelonLogger.Msg("[Puzzle] Show - puzzle active");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Puzzle] Show_Postfix error: {ex.Message}");
            }
        }

        public static void Close_Postfix()
        {
            try
            {
                PuzzleGameState.IsActive = false; // reset handler clears the cached pointer/flags
                MelonLogger.Msg("[Puzzle] Close - puzzle inactive");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Puzzle] Close_Postfix error: {ex.Message}");
            }
        }

        // ---- Cursor movement -----------------------------------------------------

        public static void Move_Postfix(object __instance)
        {
            try
            {
                IntPtr ptr = (__instance as Il2CppObjectBase)?.Pointer ?? IntPtr.Zero;
                if (ptr == IntPtr.Zero) return;
                activeControllerPtr = ptr;
                // One frame delay lets cursorPos/animation settle (TouchPiece, move() coroutine).
                CoroutineManager.StartUntracked(AnnounceTileAfterDelay(ptr));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Puzzle] Move_Postfix error: {ex.Message}");
            }
        }

        private static IEnumerator AnnounceTileAfterDelay(IntPtr ctrlPtr)
        {
            yield return null;
            AnnounceTile(ctrlPtr);
        }

        private static void AnnounceTile(IntPtr ctrlPtr)
        {
            try
            {
                if (!PuzzleGameState.IsActive || ctrlPtr == IntPtr.Zero) return;

                int cursorPos = IL2CppFieldReader.ReadInt32(ctrlPtr, IL2CppOffsets.Puzzle.CursorPos);
                int emptyPos = IL2CppFieldReader.ReadInt32(ctrlPtr, IL2CppOffsets.Puzzle.EmptyPiece);

                if (!boardLogged)
                {
                    boardLogged = true;
                    LogSnapshot(ctrlPtr, cursorPos, emptyPos);
                }

                if (cursorPos == lastAnnouncedCursorPos) return;
                lastAnnouncedCursorPos = cursorPos;

                string label;
                if (cursorPos == emptyPos)
                {
                    // True regardless of how pieceList is ordered.
                    label = "empty";
                }
                else
                {
                    IntPtr listPtr = IL2CppFieldReader.ReadPointerSafe(ctrlPtr, IL2CppOffsets.Puzzle.PieceList);
                    IntPtr piecePtr = IL2CppFieldReader.ReadListElement(listPtr, cursorPos);
                    if (piecePtr == IntPtr.Zero) return;
                    int pieceIdx = IL2CppFieldReader.ReadInt32(piecePtr, IL2CppOffsets.Puzzle.PieceIndex);
                    label = PieceIndexToLabel(pieceIdx);
                }

                FFI_ScreenReaderMod.SpeakText(label, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Puzzle] AnnounceTile error: {ex.Message}");
            }
        }

        // ---- On-demand position (I key / right-stick up) -------------------------

        /// <summary>
        /// Speaks the current cursor's row and column. Gated by callers on
        /// PuzzleGameState.IsActive (GlobalHotkeyHandler, ControllerRouter).
        /// </summary>
        public static void AnnouncePosition()
        {
            try
            {
                if (!PuzzleGameState.IsActive || activeControllerPtr == IntPtr.Zero) return;
                int cursorPos = IL2CppFieldReader.ReadInt32(activeControllerPtr, IL2CppOffsets.Puzzle.CursorPos);
                int row = cursorPos / 4 + 1;
                int col = cursorPos % 4 + 1;
                FFI_ScreenReaderMod.SpeakText($"Row {row}, Column {col}", interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Puzzle] AnnouncePosition error: {ex.Message}");
            }
        }

        // ---- Helpers -------------------------------------------------------------

        /// <summary>
        /// Maps a PuzzlePiece.index to its printed number. Initial assumption: index + 1
        /// (tiles 1-15). Confirm against the SNAPSHOT log and adjust if needed.
        /// </summary>
        private static string PieceIndexToLabel(int pieceIndex)
        {
            return (pieceIndex + 1).ToString();
        }

        private static void LogSnapshot(IntPtr ctrlPtr, int cursorPos, int emptyPos)
        {
            try
            {
                IntPtr listPtr = IL2CppFieldReader.ReadPointerSafe(ctrlPtr, IL2CppOffsets.Puzzle.PieceList);
                int count = IL2CppFieldReader.ReadListSize(listPtr);
                MelonLogger.Msg($"[Puzzle] SNAPSHOT cursorPos={cursorPos} emptyPiece={emptyPos} count={count}");
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = IL2CppFieldReader.ReadListElement(listPtr, i);
                    int idx = p == IntPtr.Zero ? -999 : IL2CppFieldReader.ReadInt32(p, IL2CppOffsets.Puzzle.PieceIndex);
                    MelonLogger.Msg($"[Puzzle]  slot={i} index={idx}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Puzzle] LogSnapshot error: {ex.Message}");
            }
        }

        private static void ResetLocalState()
        {
            activeControllerPtr = IntPtr.Zero;
            lastAnnouncedCursorPos = -1;
            boardLogged = false;
        }

        private static void PatchPostfix(HarmonyLib.Harmony harmony, Type type, string methodName, string postfixName)
        {
            var target = AccessTools.Method(type, methodName);
            if (target == null)
            {
                MelonLogger.Warning($"[Puzzle] method {methodName} not found - skipped");
                return;
            }
            var postfix = typeof(PuzzlePatches).GetMethod(postfixName, BindingFlags.Public | BindingFlags.Static);
            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.FullName == fullName) return type;
                    }
                }
                catch { } // Assembly may throw on GetTypes
            }
            return null;
        }
    }
}
