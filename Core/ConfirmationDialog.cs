using System;
using System.Collections;
using UnityEngine;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Core
{
    /// <summary>
    /// Simple Yes/No confirmation dialog using Windows API focus stealing.
    /// </summary>
    internal static class ConfirmationDialog
    {
        public static bool IsOpen { get; private set; }

        private static string prompt = "";
        private static Action onYesCallback;
        private static Action onNoCallback;
        private static bool selectedYes = true;

        public static void Open(string promptText, Action onYes, Action onNo = null)
        {
            if (IsOpen) return;

            IsOpen = true;
            prompt = promptText ?? "";
            onYesCallback = onYes;
            onNoCallback = onNo;
            selectedYes = true;

            WindowsFocusHelper.InitializeKeyStates(new[] {
                WindowsFocusHelper.VK_RETURN, WindowsFocusHelper.VK_ESCAPE,
                WindowsFocusHelper.VK_LEFT, WindowsFocusHelper.VK_RIGHT,
                WindowsFocusHelper.VK_Y, WindowsFocusHelper.VK_N
            });

            WindowsFocusHelper.StealFocus("FFI_ConfirmDialog");
            CoroutineManager.StartManaged(DelayedPromptAnnouncement($"{prompt} {T("Yes or No")}"));
        }

        private static IEnumerator DelayedPromptAnnouncement(string text)
        {
            yield return new WaitForSeconds(0.1f);
            FFI_ScreenReaderMod.SpeakText(text, interrupt: true);
        }

        private static IEnumerator DelayedCloseAnnouncement(string text, Action callback)
        {
            Close();
            yield return new WaitForSeconds(0.1f);
            FFI_ScreenReaderMod.SpeakText(text, interrupt: true);
            callback?.Invoke();
        }

        public static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            WindowsFocusHelper.RestoreFocus();
            onYesCallback = null;
            onNoCallback = null;
        }

        /// <summary>
        /// Handles keyboard input. Returns true if input was consumed (dialog is open).
        /// </summary>
        public static bool HandleInput()
        {
            if (!IsOpen) return false;

            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_Y))
            {
                var callback = onYesCallback;
                CoroutineManager.StartManaged(DelayedCloseAnnouncement(T("Yes"), callback));
                return true;
            }

            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_N))
            {
                var callback = onNoCallback;
                CoroutineManager.StartManaged(DelayedCloseAnnouncement(T("No"), callback));
                return true;
            }

            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_ESCAPE))
            {
                var callback = onNoCallback;
                CoroutineManager.StartManaged(DelayedCloseAnnouncement(T("Cancelled"), callback));
                return true;
            }

            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_RETURN))
            {
                if (selectedYes)
                {
                    var callback = onYesCallback;
                    CoroutineManager.StartManaged(DelayedCloseAnnouncement(T("Yes"), callback));
                }
                else
                {
                    var callback = onNoCallback;
                    CoroutineManager.StartManaged(DelayedCloseAnnouncement(T("No"), callback));
                }
                return true;
            }

            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_LEFT) || WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_RIGHT))
            {
                selectedYes = !selectedYes;
                string selection = selectedYes ? T("Yes") : T("No");
                FFI_ScreenReaderMod.SpeakText(selection, interrupt: true);
                return true;
            }

            return true; // Consume all input while dialog is open
        }
    }
}
