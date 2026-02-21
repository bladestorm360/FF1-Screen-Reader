using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Patches for scrolling intro/outro messages.
    /// The intro uses ScrollMessageWindowController which displays scrolling text.
    /// </summary>
    public static class ScrollMessagePatches
    {
        private static string lastScrollMessage = "";
        private static IEnumerator activeScrollCoroutine = null;

        /// <summary>
        /// Applies scroll message patches using manual Harmony patching.
        /// Patches the Manager classes which receive the actual message text as parameters.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch FadeMessageManager.Play - receives single message string
                Type fadeManagerType = FindType("Il2CppLast.Message.FadeMessageManager");
                if (fadeManagerType != null)
                {
                    var playMethod = AccessTools.Method(fadeManagerType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("FadeManagerPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
                else
                {
                    MelonLogger.Warning("FadeMessageManager type not found");
                }

                // Patch LineFadeMessageWindowController.SetData directly
                // (some code paths bypass the Manager and call the Controller)
                Type lineFadeControllerType = FindType("Il2CppLast.UI.Message.LineFadeMessageWindowController");
                if (lineFadeControllerType != null)
                {
                    var setDataMethod = AccessTools.Method(lineFadeControllerType, "SetData");
                    if (setDataMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("LineFadeWindowController_SetData_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(setDataMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
                else
                {
                    MelonLogger.Warning("LineFadeMessageWindowController type not found");
                }

                // Patch ScrollMessageManager.Play - receives scroll message string
                Type scrollManagerType = FindType("Il2CppLast.Message.ScrollMessageManager");
                if (scrollManagerType != null)
                {
                    var playMethod = AccessTools.Method(scrollManagerType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("ScrollManagerPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
                else
                {
                    MelonLogger.Warning("ScrollMessageManager type not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying scroll message patches: {ex.Message}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Finds a type by name across all loaded assemblies.
        /// </summary>
        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.FullName == fullName)
                        {
                            return type;
                        }
                    }
                }
                catch { } // Assembly may throw on GetTypes
            }
            return null;
        }

        /// <summary>
        /// Postfix for FadeMessageManager.Play - captures the message parameter directly.
        /// FadeMessageManager.Play(string message, int fontSize, Color32 color, float fadeinTime, float fadeoutTime, float waitTime, bool isCenterAnchor, float postionX, float postionY)
        /// </summary>
        public static void FadeManagerPlay_Postfix(object __0)
        {
            try
            {
                // __0 is the first parameter (message string)
                string message = __0?.ToString();
                if (string.IsNullOrEmpty(message))
                {
                    return;
                }

                // Avoid duplicate announcements
                if (message == lastScrollMessage)
                {
                    return;
                }

                lastScrollMessage = message;

                // Clean up the message
                string cleanMessage = message.Replace("\n", " ").Replace("\r", " ");
                while (cleanMessage.Contains("  "))
                {
                    cleanMessage = cleanMessage.Replace("  ", " ");
                }
                cleanMessage = cleanMessage.Trim();

                // Check if this message duplicates a map transition announcement
                if (!LocationMessageTracker.ShouldAnnounceFadeMessage(cleanMessage))
                {
                    return;
                }

                FFI_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in FadeManagerPlay_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for LineFadeMessageWindowController.SetData - captures messages directly.
        /// Some code paths (like game over) bypass the Manager and call the Controller directly.
        /// Uses reflection to access IL2CPP List<string> since IEnumerable doesn't work.
        /// </summary>
        public static void LineFadeWindowController_SetData_Postfix(object __0)
        {
            try
            {
                if (__0 == null) return;

                // Use reflection to access IL2CPP List<string>
                var countProp = __0.GetType().GetProperty("Count");
                if (countProp == null)
                {
                    MelonLogger.Warning("[Line Fade Controller] Count property not found");
                    return;
                }

                int count = (int)countProp.GetValue(__0);
                if (count == 0) return;

                var indexer = __0.GetType().GetProperty("Item");
                if (indexer == null)
                {
                    MelonLogger.Warning("[Line Fade Controller] Item indexer not found");
                    return;
                }

                string combinedMessage = "";
                for (int i = 0; i < count; i++)
                {
                    string line = indexer.GetValue(__0, new object[] { i }) as string;
                    if (!string.IsNullOrEmpty(line))
                    {
                        if (combinedMessage.Length > 0)
                            combinedMessage += " ";
                        combinedMessage += line;
                    }
                }

                if (string.IsNullOrEmpty(combinedMessage)) return;
                if (combinedMessage == lastScrollMessage) return;

                lastScrollMessage = combinedMessage;

                string cleanMessage = combinedMessage.Replace("\n", " ").Replace("\r", " ").Trim();
                while (cleanMessage.Contains("  "))
                    cleanMessage = cleanMessage.Replace("  ", " ");

                // Use interrupt for defeat message to ensure it's heard over any preceding KO message
                bool isDefeatMessage = cleanMessage.Contains("defeated", StringComparison.OrdinalIgnoreCase);

                FFI_ScreenReaderMod.SpeakText(cleanMessage, interrupt: isDefeatMessage);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in LineFadeWindowController_SetData_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ScrollMessageManager.Play - captures the message parameter.
        /// ScrollMessageManager.Play(ScrollMessageClient.ScrollType type, string message, float scrollTime, int fontSize, Color32 color, TextAnchor anchor, Rect margin)
        /// __1 = message (string), __2 = scrollTime (float)
        /// </summary>
        public static void ScrollManagerPlay_Postfix(object __1, object __2)
        {
            try
            {
                // __1 is the second parameter (message string, first is ScrollType)
                string message = __1?.ToString();
                if (string.IsNullOrEmpty(message))
                {
                    return;
                }

                // Avoid duplicate announcements
                if (message == lastScrollMessage)
                {
                    return;
                }

                lastScrollMessage = message;

                // Stop any previous scroll announcement
                if (activeScrollCoroutine != null)
                {
                    CoroutineManager.StopManaged(activeScrollCoroutine);
                    activeScrollCoroutine = null;
                }

                // Get scroll time (default to 30 seconds if unavailable)
                float scrollTime = 30f;
                if (__2 is float st)
                {
                    scrollTime = st;
                }
                else if (__2 != null && float.TryParse(__2.ToString(), out float parsed))
                {
                    scrollTime = parsed;
                }

                // Split by newlines
                string[] lines = message.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                // Start timed coroutine
                activeScrollCoroutine = SpeakScrollLinesWithTiming(lines, scrollTime);
                CoroutineManager.StartManaged(activeScrollCoroutine);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ScrollManagerPlay_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine that speaks scroll message lines with timing that matches the visual scroll.
        /// </summary>
        private static IEnumerator SpeakScrollLinesWithTiming(string[] lines, float totalScrollTime)
        {
            // Calculate delay between lines (visual scroll is linear)
            float delayPerLine = totalScrollTime / (lines.Length + 1);
            float nextSpeakTime = Time.time;

            foreach (string line in lines)
            {
                string cleanLine = line.Trim();
                if (string.IsNullOrEmpty(cleanLine)) continue;

                // Wait until appropriate time
                while (Time.time < nextSpeakTime)
                    yield return null;

                FFI_ScreenReaderMod.SpeakText(cleanLine, interrupt: false);
                nextSpeakTime = Time.time + delayPerLine;
            }

            // Clear the active coroutine reference when done
            activeScrollCoroutine = null;
        }

    }
}
