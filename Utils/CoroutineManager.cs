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
        private static readonly Dictionary<IEnumerator, IEnumerator> originalToWrapper = new Dictionary<IEnumerator, IEnumerator>();
        // Reverse mapping for O(1) lookup when evicting oldest coroutine
        private static readonly Dictionary<IEnumerator, IEnumerator> wrapperToOriginal = new Dictionary<IEnumerator, IEnumerator>();
        // Wrapper -> the token MelonCoroutines.Start returned. Stopping must use this token, NOT the
        // IEnumerator: MelonCoroutines.Stop given the raw IEnumerator resolves to a null UnityEngine
        // .Coroutine and StopCoroutine(null) throws ("routine is null") at teardown.
        private static readonly Dictionary<IEnumerator, object> wrapperToToken = new Dictionary<IEnumerator, object>();
        private static readonly object coroutineLock = new object();
        private static int maxConcurrentCoroutines = 20;

        /// <summary>
        /// Holds a reference to a wrapper coroutine so ManagedWrapper can self-remove.
        /// </summary>
        private class WrapperRef
        {
            public IEnumerator Wrapper;
            public IEnumerator Original;
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
                    foreach (var wrapper in activeCoroutines)
                        StopByToken(wrapper);
                    activeCoroutines.Clear();
                    originalToWrapper.Clear();
                    wrapperToOriginal.Clear();
                    wrapperToToken.Clear();
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
                    var oldest = activeCoroutines[0];
                    activeCoroutines.RemoveAt(0);
                    // Use reverse mapping for O(1) lookup instead of O(n) dictionary scan
                    if (wrapperToOriginal.TryGetValue(oldest, out var original))
                    {
                        originalToWrapper.Remove(original);
                        wrapperToOriginal.Remove(oldest);
                    }
                    StopByToken(oldest);
                }

                // Use a holder to pass the wrapper reference into the iterator
                var holder = new WrapperRef();
                var wrapper = ManagedWrapper(coroutine, holder);
                holder.Wrapper = wrapper;
                holder.Original = coroutine;

                // Start the wrapper coroutine, keeping the token MelonCoroutines hands back so we
                // can stop it cleanly later.
                try
                {
                    object token = MelonCoroutines.Start(wrapper);
                    activeCoroutines.Add(wrapper);
                    originalToWrapper[coroutine] = wrapper;
                    wrapperToOriginal[wrapper] = coroutine;
                    wrapperToToken[wrapper] = token;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Error starting managed coroutine: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Stops a managed coroutine by its original IEnumerator reference.
        /// This correctly looks up and stops the wrapper that's actually running.
        /// </summary>
        public static void StopManaged(IEnumerator original)
        {
            if (original == null) return;

            lock (coroutineLock)
            {
                if (originalToWrapper.TryGetValue(original, out var wrapper))
                {
                    originalToWrapper.Remove(original);
                    wrapperToOriginal.Remove(wrapper);
                    activeCoroutines.Remove(wrapper);
                    StopByToken(wrapper);
                }
            }
        }

        /// <summary>
        /// Stops a wrapper using the token MelonCoroutines.Start returned, and forgets the token.
        /// Must be called while holding coroutineLock. Tolerates a missing/null token (the coroutine
        /// already finished), which is what prevents the teardown "routine is null" NRE.
        /// </summary>
        private static void StopByToken(IEnumerator wrapper)
        {
            if (wrapper == null) return;
            if (!wrapperToToken.TryGetValue(wrapper, out var token)) return;
            wrapperToToken.Remove(wrapper);
            if (token == null) return;
            try { MelonCoroutines.Stop(token); }
            catch (Exception ex) { MelonLogger.Error($"Error stopping managed coroutine: {ex.Message}"); }
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
                    {
                        activeCoroutines.Remove(holder.Wrapper);
                        wrapperToOriginal.Remove(holder.Wrapper);
                        wrapperToToken.Remove(holder.Wrapper);
                    }
                    if (holder.Original != null)
                        originalToWrapper.Remove(holder.Original);
                }
            }
        }
    }
}
