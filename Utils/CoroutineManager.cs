using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Manages coroutines to prevent memory leaks and crashes.
    /// Limits concurrent coroutines and provides cleanup on mod unload.
    /// Completed coroutines self-remove via ManagedWrapper.
    /// </summary>
    public static class CoroutineManager
    {
        private static readonly List<IEnumerator> activeCoroutines = new List<IEnumerator>();
        private static readonly object coroutineLock = new object();
        private static int maxConcurrentCoroutines = 20;

        /// <summary>
        /// Holds a reference to a wrapper coroutine so ManagedWrapper can self-remove.
        /// </summary>
        private class WrapperRef
        {
            public IEnumerator Wrapper;
        }

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
        public static void StartUntracked(IEnumerator coroutine)
        {
            try { MelonCoroutines.Start(coroutine); }
            catch (Exception ex) { MelonLogger.Error($"Error starting coroutine: {ex.Message}"); }
        }

        /// <summary>
        /// Start a managed coroutine with automatic cleanup and limit enforcement.
        /// The coroutine is wrapped so it self-removes from tracking on completion.
        /// </summary>
        public static void StartManaged(IEnumerator coroutine)
        {
            lock (coroutineLock)
            {
                // If we're at the limit, stop and remove the oldest coroutine
                if (activeCoroutines.Count >= maxConcurrentCoroutines)
                {
                    MelonLogger.Msg("Too many active coroutines, stopping oldest");
                    var oldest = activeCoroutines[0];
                    activeCoroutines.RemoveAt(0);
                    try { MelonCoroutines.Stop(oldest); }
                    catch (Exception ex) { MelonLogger.Error($"Error stopping evicted coroutine: {ex.Message}"); }
                }

                // Use a holder to pass the wrapper reference into the iterator
                var holder = new WrapperRef();
                var wrapper = ManagedWrapper(coroutine, holder);
                holder.Wrapper = wrapper;

                // Start the wrapper coroutine
                try
                {
                    MelonCoroutines.Start(wrapper);
                    activeCoroutines.Add(wrapper);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Error starting managed coroutine: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Wraps a coroutine so it automatically removes itself from tracking on completion.
        /// The holder provides a reference to this wrapper for self-removal.
        /// </summary>
        private static IEnumerator ManagedWrapper(IEnumerator inner, WrapperRef holder)
        {
            try
            {
                while (inner.MoveNext())
                    yield return inner.Current;
            }
            finally
            {
                lock (coroutineLock)
                {
                    if (holder.Wrapper != null)
                        activeCoroutines.Remove(holder.Wrapper);
                }
            }
        }
    }
}
