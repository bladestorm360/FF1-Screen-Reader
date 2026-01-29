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

        // Track if we're in a dialogue sequence
        private static bool isInDialogue = false;

        // Offsets for MessageWindowManager (from dump.cs)
        private const int OFFSET_MESSAGE_LIST = 0x88;        // List<string> messageList
        private const int OFFSET_NEW_PAGE_LINE_LIST = 0xA0;  // List<int> newPageLineList
        private const int OFFSET_MESSAGE_LINE_INDEX = 0xB0;  // int messageLineIndex
        private const int OFFSET_CURRENT_PAGE_NUMBER = 0xF8; // int currentPageNumber

        /// <summary>
        /// Applies message window patches using manual Harmony patching.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[MessageWindow] Applying message window patches...");

                // Patch MessageWindowManager
                Type mwmType = FindType("MessageWindowManager");
                if (mwmType != null)
                {
                    MelonLogger.Msg($"[MessageWindow] Found MessageWindowManager: {mwmType.FullName}");

                    // Patch SetContent - captures dialogue text from messageList field
                    var setContentMethod = AccessTools.Method(mwmType, "SetContent");
                    if (setContentMethod != null)
                    {
                        var postfix = typeof(MessageWindowPatches).GetMethod("SetContent_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(setContentMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("[MessageWindow] Patched MessageWindowManager.SetContent");
                    }
                    else
                    {
                        MelonLogger.Warning("[MessageWindow] MessageWindowManager.SetContent method not found");
                    }

                    // Patch Play - reads speaker and announces first line
                    var playMethod = AccessTools.Method(mwmType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(MessageWindowPatches).GetMethod("Play_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("[MessageWindow] Patched MessageWindowManager.Play");
                    }
                    else
                    {
                        MelonLogger.Warning("[MessageWindow] MessageWindowManager.Play method not found");
                    }

                    // Patch NewPageInputWaitInit - announces next line when advancing
                    var newPageInitMethod = AccessTools.Method(mwmType, "NewPageInputWaitInit");
                    if (newPageInitMethod != null)
                    {
                        var postfix = typeof(MessageWindowPatches).GetMethod("NewPageInputWaitInit_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(newPageInitMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("[MessageWindow] Patched MessageWindowManager.NewPageInputWaitInit");
                    }
                    else
                    {
                        MelonLogger.Warning("[MessageWindow] MessageWindowManager.NewPageInputWaitInit method not found");
                    }

                    // Patch PlayingInit - called when each page starts displaying (catches all pages including last)
                    var playingInitMethod = AccessTools.Method(mwmType, "PlayingInit");
                    if (playingInitMethod != null)
                    {
                        var postfix = typeof(MessageWindowPatches).GetMethod("PlayingInit_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playingInitMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("[MessageWindow] Patched MessageWindowManager.PlayingInit");
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
                        MelonLogger.Msg("[MessageWindow] Patched MessageWindowManager.Close");
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

                MelonLogger.Msg("[MessageWindow] Message window patches applied successfully");
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
                    MelonLogger.Msg($"[MessageWindow] Found MessageMultipleWindowManager: {mmwmType.FullName}");

                    var playMethod = AccessTools.Method(mmwmType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(MessageWindowPatches).GetMethod("MultipleWindowPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("[MessageWindow] Patched MessageMultipleWindowManager.Play");
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
                    MelonLogger.Msg($"[MessageWindow] Found MessageSelectWindowManager: {mswmType.FullName}");

                    var playMethod = AccessTools.Method(mswmType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(MessageWindowPatches).GetMethod("SelectWindowPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("[MessageWindow] Patched MessageSelectWindowManager.Play");
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
                    MelonLogger.Msg($"[MessageWindow] Found SystemMessageWindowManager: {smwmType.FullName}");

                    var openMethod = AccessTools.Method(smwmType, "Open");
                    if (openMethod != null)
                    {
                        var postfix = typeof(MessageWindowPatches).GetMethod("SystemMessageOpen_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(openMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("[MessageWindow] Patched SystemMessageWindowManager.Open");
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
                catch { }
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

                unsafe
                {
                    // Read messageList pointer at offset 0x88
                    IntPtr listPtr = *(IntPtr*)((byte*)instancePtr.ToPointer() + OFFSET_MESSAGE_LIST);
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

                unsafe
                {
                    // Read messageList pointer at offset 0x88
                    IntPtr listPtr = *(IntPtr*)((byte*)instancePtr.ToPointer() + OFFSET_MESSAGE_LIST);
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
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error reading messageList: {ex.Message}");
                return null;
            }
        }

        // Offset for speaker value (from dump.cs: spekerValue at 0xA8)
        private const int OFFSET_SPEAKER_VALUE = 0xA8;

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

                unsafe
                {
                    // Read spekerValue pointer at offset 0xA8
                    IntPtr stringPtr = *(IntPtr*)((byte*)instancePtr.ToPointer() + OFFSET_SPEAKER_VALUE);
                    if (stringPtr == IntPtr.Zero)
                        return null;

                    // Convert IL2CPP string to managed string
                    return IL2CPP.Il2CppStringToManaged(stringPtr);
                }
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

                unsafe
                {
                    // Read newPageLineList pointer at offset 0xA0
                    IntPtr listPtr = *(IntPtr*)((byte*)instancePtr.ToPointer() + OFFSET_NEW_PAGE_LINE_LIST);
                    if (listPtr == IntPtr.Zero)
                    {
                        MelonLogger.Msg("[MessageWindow] newPageLineList pointer is null (will use fallback)");
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
        /// Postfix for MessageWindowManager.Play - announces speaker then first page of dialogue.
        /// Subsequent pages are announced by NewPageInputWaitInit_Postfix.
        /// </summary>
        public static void Play_Postfix(object __instance)
        {
            try
            {
                // Read speaker from instance
                string speaker = ReadSpeakerFromInstance(__instance);

                // Announce speaker if new (using central deduplicator)
                if (!string.IsNullOrWhiteSpace(speaker) && AnnouncementDeduplicator.ShouldAnnounce("Message.Speaker", speaker))
                {
                    string cleanSpeaker = CleanMessage(speaker);
                    if (!string.IsNullOrWhiteSpace(cleanSpeaker))
                    {
                        MelonLogger.Msg($"[MessageWindow] Speaker: {cleanSpeaker}");
                        FFI_ScreenReaderMod.SpeakText(cleanSpeaker, interrupt: false);
                    }
                }

                // Announce first page only (if we have stored messages)
                if (currentPageBreaks.Count > 0 && lastAnnouncedPageIndex < 0)
                {
                    string firstPage = GetPageText(0);
                    if (!string.IsNullOrWhiteSpace(firstPage) && AnnouncementDeduplicator.ShouldAnnounce("Message.Line", firstPage))
                    {
                        lastAnnouncedPageIndex = 0;
                        MelonLogger.Msg($"[MessageWindow] Page 1/{currentPageBreaks.Count}: {firstPage}");
                        FFI_ScreenReaderMod.SpeakText(firstPage, interrupt: false);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error in Play_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for MessageWindowManager.NewPageInputWaitInit - announces next dialogue page.
        /// Called when advancing to a new page after user presses confirm.
        /// </summary>
        public static void NewPageInputWaitInit_Postfix(object __instance)
        {
            try
            {
                if (!isInDialogue || currentPageBreaks.Count == 0)
                    return;

                // Get current page number from instance
                int currentPage = GetCurrentPageNumber(__instance);

                // Announce next page if we haven't announced it yet
                if (currentPage >= 0 && currentPage < currentPageBreaks.Count && currentPage != lastAnnouncedPageIndex)
                {
                    string pageText = GetPageText(currentPage);
                    if (!string.IsNullOrWhiteSpace(pageText) && AnnouncementDeduplicator.ShouldAnnounce("Message.Line", pageText))
                    {
                        lastAnnouncedPageIndex = currentPage;
                        MelonLogger.Msg($"[MessageWindow] Page {currentPage + 1}/{currentPageBreaks.Count}: {pageText}");
                        FFI_ScreenReaderMod.SpeakText(pageText, interrupt: false);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MessageWindow] Error in NewPageInputWaitInit_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for MessageWindowManager.PlayingInit - called when each page starts displaying.
        /// This catches all pages including the last one that NewPageInputWaitInit misses.
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
                    if (!string.IsNullOrWhiteSpace(pageText) && AnnouncementDeduplicator.ShouldAnnounce("Message.Line", pageText))
                    {
                        lastAnnouncedPageIndex = currentPage;
                        MelonLogger.Msg($"[MessageWindow] Page {currentPage + 1}/{currentPageBreaks.Count}: {pageText}");
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
                MelonLogger.Msg("[MessageWindow] Close - clearing dialogue state");
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

                unsafe
                {
                    // Read currentPageNumber at offset 0xF8
                    int pageNum = *(int*)((byte*)instancePtr.ToPointer() + OFFSET_CURRENT_PAGE_NUMBER);
                    return pageNum;
                }
            }
            catch
            {
                return -1;
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
                if (!AnnouncementDeduplicator.ShouldAnnounce("Message.Line", fullText))
                    return;

                string cleanMessage = CleanMessage(fullText);
                if (!string.IsNullOrWhiteSpace(cleanMessage))
                {
                    MelonLogger.Msg($"[MessageWindow] Multiple window dialogue: {cleanMessage}");
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
                if (!AnnouncementDeduplicator.ShouldAnnounce("Message.Line", fullText))
                    return;

                string cleanMessage = CleanMessage(fullText);
                if (!string.IsNullOrWhiteSpace(cleanMessage))
                {
                    MelonLogger.Msg($"[MessageWindow] Select window dialogue: {cleanMessage}");
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
                if (!AnnouncementDeduplicator.ShouldAnnounce("Message.Line", message))
                    return;

                string cleanMessage = CleanMessage(message);
                if (!string.IsNullOrWhiteSpace(cleanMessage))
                {
                    MelonLogger.Msg($"[MessageWindow] System message: {cleanMessage}");
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
            AnnouncementDeduplicator.Reset("Message.Speaker", "Message.Line");
            currentMessageList.Clear();
            currentPageBreaks.Clear();
            lastAnnouncedPageIndex = -1;
            isInDialogue = false;
        }
    }
}
