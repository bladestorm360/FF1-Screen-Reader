using System;
using MelonLoader;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Standardized error handling for Harmony patch postfixes and other mod operations.
    /// </summary>
    internal static class PatchLogger
    {
        /// <summary>
        /// Executes an action with standardized error logging.
        /// Use in Harmony postfixes to ensure exceptions never propagate to game code.
        /// </summary>
        internal static void SafeExecute(string context, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[{context}] {ex.Message}");
            }
        }

        /// <summary>
        /// Logs a warning with standardized format: [Context] message: exception
        /// </summary>
        internal static void Warn(string context, string message, Exception ex)
        {
            MelonLogger.Warning($"[{context}] {message}: {ex.Message}");
        }

        /// <summary>
        /// Logs an error with standardized format: [Context] message: exception
        /// </summary>
        internal static void Error(string context, string message, Exception ex)
        {
            MelonLogger.Error($"[{context}] {message}: {ex.Message}");
        }
    }
}
