using System;
using System.Collections.Generic;
using MelonLoader;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Manages coroutines to prevent memory leaks and crashes.
    /// Limits concurrent coroutines and provides cleanup on mod unload.
    /// </summary>
    public static class CoroutineManager
    {
        private static readonly List<System.Collections.IEnumerator> activeCoroutines = new List<System.Collections.IEnumerator>();
        private static readonly object coroutineLock = new object();
        private static int maxConcurrentCoroutines = 20;

        /// <summary>
        /// Cleanup all active coroutines.
        /// Should be called when the mod is unloaded.
        /// </summary>
        public static void CleanupAll()
        {
            lock (coroutineLock)
            {
                if (activeCoroutines.Count > 0)
                {
                    MelonLogger.Msg($"Cleaning up {activeCoroutines.Count} active coroutines");
                    foreach (var coroutine in activeCoroutines)
                    {
                        try
                        {
                            MelonCoroutines.Stop(coroutine);
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Error($"Error stopping coroutine: {ex.Message}");
                        }
                    }
                    activeCoroutines.Clear();
                }
            }
        }

        /// <summary>
        /// Start an untracked coroutine (fire-and-forget, no leak tracking).
        /// Use for short one-frame-delay coroutines that complete quickly.
        /// </summary>
        public static void StartUntracked(System.Collections.IEnumerator coroutine)
        {
            try { MelonCoroutines.Start(coroutine); }
            catch (Exception ex) { MelonLogger.Error($"Error starting coroutine: {ex.Message}"); }
        }

        /// <summary>
        /// Start a managed coroutine with automatic cleanup and limit enforcement.
        /// </summary>
        /// <param name="coroutine">The coroutine to start.</param>
        public static void StartManaged(System.Collections.IEnumerator coroutine)
        {
            lock (coroutineLock)
            {
                // If we're at the limit, remove the oldest one from tracking
                if (activeCoroutines.Count >= maxConcurrentCoroutines)
                {
                    MelonLogger.Msg("Too many active coroutines, removing oldest from tracking");
                    activeCoroutines.RemoveAt(0);
                }

                // Start the new coroutine
                try
                {
                    MelonCoroutines.Start(coroutine);
                    activeCoroutines.Add(coroutine);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Error starting managed coroutine: {ex.Message}");
                }
            }
        }
    }
}
