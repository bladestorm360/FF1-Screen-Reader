using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using Il2CppInterop.Runtime;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Patches for message windows (NPC dialogue, system messages).
    /// Uses manual patching to avoid IL2CPP string parameter crashes.
    /// Reads dialogue line-by-line as displayed, not all at once.
    /// Uses pointer-based access for IL2CPP fields.
    /// </summary>
    public static class MessageWindowPatches
    {
        // Store message list for page-by-page reading
        private static List<string> currentMessageList = new List<string>();
        private static List<int> currentPageBreaks = new List<int>(); // Line indices where new pages start
        private static int lastAnnouncedPageIndex = -1;

        // Speaker tracking
        private static string currentSpeaker = "";
        private static string lastAnnouncedSpeaker = "";

        // Track if we're in a dialogue sequence
        private static bool isInDialogue = false;

        /// <summary>
        /// Applies message window patches using manual Harmony patching.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch MessageWindowManager
                Type mwmType = FindType("MessageWindowManager");
                if (mwmType != null)
                {
                    // Patch SetContent - captures dialogue text from messageList field
                    var setContentMethod = AccessTools.Method(mwmType, "SetContent");
                    if (setContentMethod != null)
                    {
                        var postfix = typeof(MessageWindowPatches).GetMethod("SetContent_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(setContentMethod, postfix: new HarmonyMethod(postfix));
                    }
                    else
                    {
                        MelonLogger.Warning("[MessageWindow] MessageWindowManager.SetContent method not found");
                    }

                    // Patch PlayingInit - called when each page starts displaying (catches all pages including last)
                    var playingInitMethod = AccessTools.Method(mwmType, "PlayingInit");
                    if (playingInitMethod != null)
                    {
                        var postfix = typeof(MessageWindowPatches).GetMethod("PlayingInit_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playingInitMethod, postfix: new HarmonyMethod(postfix));
                    }
                    else
                    {
                        MelonLogger.Warning("[MessageWindow] MessageWindowManager.PlayingInit method not found");
                    }

                    // Patch Close - clears dialogue state
                    var closeMethod = AccessTools.Method(mwmType, "Close");
                    if (closeMethod != null)
                    {
                        var postfix = typeof(MessageWindowPatches).GetMethod("Close_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(closeMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
                else
                {
                    MelonLogger.Warning("[MessageWindow] MessageWindowManager type not found");
                }

                // Patch MessageMultipleWindowManager for concurrent windows
                PatchMessageMultipleWindowManager(harmony);

                // Patch MessageSelectWindowManager for choice dialogs
                PatchMessageSelectWindowManager(harmony);

                // Patch SystemMessageWindowManager for system prompts
                PatchSystemMessageWindowManager(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MessageWindow] Error applying message window patches: {ex.Message}");
                MelonLogger.Error($"[MessageWindow] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Patches MessageMultipleWindowManager for concurrent dialogue windows.
        /// </summary>
        private static void PatchMessageMultipleWindowManager(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type mmwmType = FindType("MessageMultipleWindowManager");
                if (mmwmType != null)
                {
                    var playMethod = AccessTools.Method(mmwmType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(MessageWindowPatches).GetMethod("MultipleWindowPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error patching MessageMultipleWindowManager: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches MessageSelectWindowManager for choice dialogs.
        /// </summary>
        private static void PatchMessageSelectWindowManager(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type mswmType = FindType("MessageSelectWindowManager");
                if (mswmType != null)
                {
                    var playMethod = AccessTools.Method(mswmType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(MessageWindowPatches).GetMethod("SelectWindowPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error patching MessageSelectWindowManager: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches SystemMessageWindowManager for system prompts.
        /// </summary>
        private static void PatchSystemMessageWindowManager(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type smwmType = FindType("SystemMessageWindowManager");
                if (smwmType != null)
                {
                    var openMethod = AccessTools.Method(smwmType, "Open");
                    if (openMethod != null)
                    {
                        var postfix = typeof(MessageWindowPatches).GetMethod("SystemMessageOpen_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(openMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error patching SystemMessageWindowManager: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds a type by name across all loaded assemblies.
        /// </summary>
        private static Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.FullName == typeName || type.Name == typeName)
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
        /// Cleans up message text by removing extra whitespace and icon markup.
        /// </summary>
        private static string CleanMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            // Strip icon markup
            string clean = TextUtils.StripIconMarkup(message);

            // Normalize whitespace
            clean = clean.Replace("\n", " ").Replace("\r", " ");
            while (clean.Contains("  "))
            {
                clean = clean.Replace("  ", " ");
            }

            return clean.Trim();
        }

        /// <summary>
        /// Reads the messageList field from a manager instance and returns combined text.
        /// Uses pointer-based access for IL2CPP types.
        /// </summary>
        private static string ReadMessageListFromInstance(object instance)
        {
            if (instance == null)
                return null;

            try
            {
                // Get the IL2CPP object pointer
                var il2cppObj = instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (il2cppObj == null)
                    return null;

                IntPtr instancePtr = il2cppObj.Pointer;
                if (instancePtr == IntPtr.Zero)
                    return null;

                // Read messageList pointer at offset 0x88
                IntPtr listPtr = IL2CppFieldReader.ReadPointer(instancePtr, IL2CppOffsets.MessageWindow.MessageList);
                if (listPtr == IntPtr.Zero)
                    return null;

                // Wrap as IL2CPP List<string>
                var il2cppList = new Il2CppSystem.Collections.Generic.List<string>(listPtr);
                if (il2cppList == null)
                    return null;

                var sb = new StringBuilder();
                int count = il2cppList.Count;

                for (int i = 0; i < count; i++)
                {
                    var msg = il2cppList[i];
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        if (sb.Length > 0)
                            sb.Append(" ");
                        sb.Append(msg.Trim());
                    }
                }

                return sb.Length > 0 ? sb.ToString() : null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error reading messageList: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads the messageList field from a manager instance using pointer-based access.
        /// </summary>
        private static List<string> ReadMessageListAsList(object instance)
        {
            if (instance == null)
                return null;

            try
            {
                // Get the IL2CPP object pointer
                var il2cppObj = instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (il2cppObj == null)
                    return null;

                IntPtr instancePtr = il2cppObj.Pointer;
                if (instancePtr == IntPtr.Zero)
                    return null;

                // Read messageList pointer at offset 0x88
                IntPtr listPtr = IL2CppFieldReader.ReadPointer(instancePtr, IL2CppOffsets.MessageWindow.MessageList);
                if (listPtr == IntPtr.Zero)
                {
                    MelonLogger.Warning("[MessageWindow] messageList pointer is null");
                    return null;
                }

                // Wrap as IL2CPP List<string>
                var il2cppList = new Il2CppSystem.Collections.Generic.List<string>(listPtr);
                if (il2cppList == null)
                    return null;

                var result = new List<string>();
                int count = il2cppList.Count;

                for (int i = 0; i < count; i++)
                {
                    var msg = il2cppList[i];
                    result.Add(msg ?? "");
                }

                return result;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error reading messageList: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Reads the spekerValue field from a manager instance using pointer-based access.
        /// Note: "speker" is a typo in the game code.
        /// </summary>
        private static string ReadSpeakerFromInstance(object instance)
        {
            if (instance == null)
                return null;

            try
            {
                // Get the IL2CPP object pointer
                var il2cppObj = instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (il2cppObj == null)
                    return null;

                IntPtr instancePtr = il2cppObj.Pointer;
                if (instancePtr == IntPtr.Zero)
                    return null;

                // Read spekerValue pointer at offset 0xA8
                IntPtr stringPtr = IL2CppFieldReader.ReadPointer(instancePtr, IL2CppOffsets.MessageWindow.SpeakerValue);
                if (stringPtr == IntPtr.Zero)
                    return null;

                // Convert IL2CPP string to managed string
                return IL2CPP.Il2CppStringToManaged(stringPtr);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error reading speaker: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Postfix for MessageWindowManager.SetContent - captures dialogue lines and page breaks.
        /// Stores message list for page-by-page reading instead of announcing all at once.
        /// </summary>
        public static void SetContent_Postfix(object __instance)
        {
            try
            {
                // Read messageList from the instance and store it
                var messageList = ReadMessageListAsList(__instance);

                if (messageList == null || messageList.Count == 0)
                    return;

                // Store messages for page-by-page reading
                currentMessageList.Clear();
                foreach (var msg in messageList)
                {
                    currentMessageList.Add(msg != null ? CleanMessage(msg) : "");
                }

                // Read page breaks (newPageLineList)
                // newPageLineList contains the ENDING line index (inclusive) for each page
                // e.g., [0, 2] means: page 0 ends at line 0, page 1 ends at line 2, page 2 is the rest
                // We convert these to START indices for easier processing
                currentPageBreaks.Clear();
                var pageBreaks = ReadPageBreaksList(__instance);
                if (pageBreaks != null && pageBreaks.Count > 0)
                {
                    // First page always starts at line 0
                    currentPageBreaks.Add(0);
                    // Subsequent pages start at (previous end index + 1)
                    for (int i = 0; i < pageBreaks.Count; i++)
                    {
                        int nextStart = pageBreaks[i] + 1;
                        if (nextStart < currentMessageList.Count)
                        {
                            currentPageBreaks.Add(nextStart);
                        }
                    }
                }
                else
                {
                    // If no page breaks, all lines belong to a single page starting at line 0
                    currentPageBreaks.Add(0);
                }

                // Reset page tracking
                lastAnnouncedPageIndex = -1;
                isInDialogue = true;

            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error in SetContent_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the newPageLineList field from a manager instance using pointer-based access.
        /// </summary>
        private static List<int> ReadPageBreaksList(object instance)
        {
            if (instance == null)
                return null;

            try
            {
                // Get the IL2CPP object pointer
                var il2cppObj = instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (il2cppObj == null)
                    return null;

                IntPtr instancePtr = il2cppObj.Pointer;
                if (instancePtr == IntPtr.Zero)
                    return null;

                // Read newPageLineList pointer at offset 0xA0
                IntPtr listPtr = IL2CppFieldReader.ReadPointer(instancePtr, IL2CppOffsets.MessageWindow.NewPageLineList);
                if (listPtr == IntPtr.Zero)
                {
                    return null;
                }

                // Wrap as IL2CPP List<int>
                var il2cppList = new Il2CppSystem.Collections.Generic.List<int>(listPtr);
                if (il2cppList == null)
                    return null;

                var result = new List<int>();
                int count = il2cppList.Count;

                for (int i = 0; i < count; i++)
                {
                    result.Add(il2cppList[i]);
                }

                return result;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error reading newPageLineList: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets all lines for a given page index.
        /// </summary>
        private static string GetPageText(int pageIndex)
        {
            if (pageIndex < 0 || pageIndex >= currentPageBreaks.Count)
                return null;

            int startLine = currentPageBreaks[pageIndex];
            int endLine = (pageIndex + 1 < currentPageBreaks.Count)
                ? currentPageBreaks[pageIndex + 1]
                : currentMessageList.Count;

            var sb = new StringBuilder();
            for (int i = startLine; i < endLine && i < currentMessageList.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(currentMessageList[i]))
                {
                    if (sb.Length > 0)
                        sb.Append(" ");
                    sb.Append(currentMessageList[i]);
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        /// <summary>
        /// Postfix for MessageWindowManager.PlayingInit - called when each page starts displaying.
        /// Catches all pages. Handles speaker tracking and page-by-page announcement.
        /// </summary>
        public static void PlayingInit_Postfix(object __instance)
        {
            try
            {
                if (!isInDialogue || currentPageBreaks.Count == 0)
                    return;

                // Get current page number from instance
                int currentPage = GetCurrentPageNumber(__instance);

                // Announce page if we haven't announced it yet
                if (currentPage >= 0 && currentPage < currentPageBreaks.Count && currentPage != lastAnnouncedPageIndex)
                {
                    string pageText = GetPageText(currentPage);
                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        // Read speaker from instance and prepend if changed
                        string speaker = ReadSpeakerFromInstance(__instance);
                        if (!string.IsNullOrWhiteSpace(speaker))
                        {
                            currentSpeaker = CleanMessage(speaker);
                        }

                        if (!string.IsNullOrEmpty(currentSpeaker) && currentSpeaker != lastAnnouncedSpeaker)
                        {
                            pageText = $"{currentSpeaker}: {pageText}";
                            lastAnnouncedSpeaker = currentSpeaker;
                        }

                        lastAnnouncedPageIndex = currentPage;
                        FFI_ScreenReaderMod.SpeakText(pageText, interrupt: false);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error in PlayingInit_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for MessageWindowManager.Close - clears dialogue state.
        /// </summary>
        public static void Close_Postfix(object __instance)
        {
            try
            {
                ClearState();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error in Close_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current page number from MessageWindowManager instance using pointer-based access.
        /// </summary>
        private static int GetCurrentPageNumber(object instance)
        {
            if (instance == null)
                return -1;

            try
            {
                // Get the IL2CPP object pointer
                var il2cppObj = instance as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                if (il2cppObj == null)
                    return -1;

                IntPtr instancePtr = il2cppObj.Pointer;
                if (instancePtr == IntPtr.Zero)
                    return -1;

                // Read currentPageNumber at offset 0xF8
                int pageNum = IL2CppFieldReader.ReadInt32(instancePtr, IL2CppOffsets.MessageWindow.CurrentPageNumber);
                return pageNum;
            }
            catch
            {
                return -1; // Pointer read may fail during transitions
            }
        }

        /// <summary>
        /// Postfix for MessageMultipleWindowManager.Play - handles concurrent dialogue windows.
        /// </summary>
        public static void MultipleWindowPlay_Postfix(object __instance)
        {
            try
            {
                // Read messageList from instance
                string fullText = ReadMessageListFromInstance(__instance);

                if (string.IsNullOrWhiteSpace(fullText))
                    return;

                // Use central deduplicator
                if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.MESSAGE_LINE, fullText))
                    return;

                string cleanMessage = CleanMessage(fullText);
                if (!string.IsNullOrWhiteSpace(cleanMessage))
                {
                    FFI_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error in MultipleWindowPlay_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for MessageSelectWindowManager.Play - handles choice dialogs.
        /// </summary>
        public static void SelectWindowPlay_Postfix(object __instance)
        {
            try
            {
                // Read messageList from instance
                string fullText = ReadMessageListFromInstance(__instance);

                if (string.IsNullOrWhiteSpace(fullText))
                    return;

                // Use central deduplicator
                if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.MESSAGE_LINE, fullText))
                    return;

                string cleanMessage = CleanMessage(fullText);
                if (!string.IsNullOrWhiteSpace(cleanMessage))
                {
                    FFI_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error in SelectWindowPlay_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SystemMessageWindowManager.Open - reads system messages.
        /// </summary>
        public static void SystemMessageOpen_Postfix(object __instance)
        {
            try
            {
                // Try to read messageList from instance
                string message = ReadMessageListFromInstance(__instance);

                if (string.IsNullOrWhiteSpace(message))
                    return;

                // Use central deduplicator
                if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.MESSAGE_LINE, message))
                    return;

                string cleanMessage = CleanMessage(message);
                if (!string.IsNullOrWhiteSpace(cleanMessage))
                {
                    FFI_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error in SystemMessageOpen_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears dialogue state. Call when dialogue window closes.
        /// </summary>
        public static void ClearState()
        {
            currentMessageList.Clear();
            currentPageBreaks.Clear();
            lastAnnouncedPageIndex = -1;
            currentSpeaker = "";
            lastAnnouncedSpeaker = "";
            isInDialogue = false;
        }
    }
}
