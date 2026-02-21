using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Utils;

namespace FFI_ScreenReader.Core
{
    /// <summary>
    /// Modal text input dialog using Windows API focus stealing.
    /// Creates an invisible window to capture keyboard input, preventing keys from reaching the game.
    /// </summary>
    internal static class TextInputWindow
    {
        public static bool IsOpen { get; private set; }

        private static StringBuilder inputBuffer = new StringBuilder();
        private static string prompt = "";
        private static Action<string> onConfirmCallback;
        private static Action onCancelCallback;
        private static int cursorPosition = 0;

        public static void Open(string promptText, string initialText, Action<string> onConfirm, Action onCancel = null)
        {
            if (IsOpen) return;

            IsOpen = true;
            prompt = promptText ?? "";
            inputBuffer.Clear();
            if (!string.IsNullOrEmpty(initialText))
                inputBuffer.Append(initialText);

            onConfirmCallback = onConfirm;
            onCancelCallback = onCancel;
            cursorPosition = inputBuffer.Length;

            var trackedKeys = new List<int> {
                WindowsFocusHelper.VK_BACK, WindowsFocusHelper.VK_RETURN,
                WindowsFocusHelper.VK_SHIFT, WindowsFocusHelper.VK_ESCAPE,
                WindowsFocusHelper.VK_SPACE,
                WindowsFocusHelper.VK_LEFT, WindowsFocusHelper.VK_UP,
                WindowsFocusHelper.VK_RIGHT, WindowsFocusHelper.VK_DOWN,
                WindowsFocusHelper.VK_HOME, WindowsFocusHelper.VK_END,
                WindowsFocusHelper.VK_OEM_MINUS, WindowsFocusHelper.VK_OEM_PERIOD,
                WindowsFocusHelper.VK_OEM_COMMA, WindowsFocusHelper.VK_OEM_7,
                WindowsFocusHelper.VK_OEM_1, WindowsFocusHelper.VK_OEM_2,
                WindowsFocusHelper.VK_OEM_3, WindowsFocusHelper.VK_OEM_4,
                WindowsFocusHelper.VK_OEM_5, WindowsFocusHelper.VK_OEM_6,
                WindowsFocusHelper.VK_OEM_PLUS
            };

            for (int vk = WindowsFocusHelper.VK_A; vk <= WindowsFocusHelper.VK_Z; vk++)
                trackedKeys.Add(vk);
            for (int vk = WindowsFocusHelper.VK_0; vk <= WindowsFocusHelper.VK_9; vk++)
                trackedKeys.Add(vk);

            WindowsFocusHelper.InitializeKeyStates(trackedKeys.ToArray());
            WindowsFocusHelper.StealFocus("FFI_TextInput");

            CoroutineManager.StartManaged(DelayedPromptAnnouncement(prompt, inputBuffer.ToString()));
        }

        private static IEnumerator DelayedPromptAnnouncement(string promptText, string initialText)
        {
            yield return new WaitForSeconds(0.3f);
            string announcement = promptText;
            if (!string.IsNullOrEmpty(initialText))
                announcement += $": {initialText}";
            FFI_ScreenReaderMod.SpeakText(announcement, interrupt: true);
        }

        private static IEnumerator DelayedCloseAnnouncement(string text, Action callback)
        {
            Close();
            yield return new WaitForSeconds(0.3f);
            FFI_ScreenReaderMod.SpeakText(text, interrupt: true);
            callback?.Invoke();
        }

        private static string GetCharacterName(char c)
        {
            switch (c)
            {
                case ' ': return "space";
                case '.': return "period";
                case ',': return "comma";
                case '\'': return "apostrophe";
                case '"': return "quote";
                case '-': return "dash";
                case '_': return "underscore";
                case ';': return "semicolon";
                case ':': return "colon";
                case '!': return "exclamation";
                case '?': return "question";
                case '/': return "slash";
                case '\\': return "backslash";
                case '(': return "open paren";
                case ')': return "close paren";
                case '[': return "open bracket";
                case ']': return "close bracket";
                case '{': return "open brace";
                case '}': return "close brace";
                case '`': return "backtick";
                case '~': return "tilde";
                case '=': return "equals";
                case '+': return "plus";
                case '|': return "pipe";
                default: return c.ToString();
            }
        }

        public static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            WindowsFocusHelper.RestoreFocus();
            onConfirmCallback = null;
            onCancelCallback = null;
        }

        /// <summary>
        /// Handles keyboard input. Returns true if input was consumed (dialog is open).
        /// </summary>
        public static bool HandleInput()
        {
            if (!IsOpen) return false;

            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_RETURN))
            {
                string finalText = inputBuffer.ToString().Trim();
                if (string.IsNullOrEmpty(finalText))
                {
                    FFI_ScreenReaderMod.SpeakText("Name cannot be empty", interrupt: true);
                    return true;
                }
                var callback = onConfirmCallback;
                CoroutineManager.StartManaged(DelayedCloseAnnouncement($"Confirmed: {finalText}", () => callback?.Invoke(finalText)));
                return true;
            }

            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_ESCAPE))
            {
                var callback = onCancelCallback;
                CoroutineManager.StartManaged(DelayedCloseAnnouncement("Cancelled", callback));
                return true;
            }

            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_BACK))
            {
                if (cursorPosition > 0)
                {
                    char deletedChar = inputBuffer[cursorPosition - 1];
                    inputBuffer.Remove(cursorPosition - 1, 1);
                    cursorPosition--;
                    FFI_ScreenReaderMod.SpeakText(GetCharacterName(deletedChar), interrupt: true);
                }
                return true;
            }

            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_LEFT))
            {
                if (cursorPosition > 0)
                {
                    cursorPosition--;
                    FFI_ScreenReaderMod.SpeakText(GetCharacterName(inputBuffer[cursorPosition]), interrupt: true);
                }
                return true;
            }

            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_RIGHT))
            {
                if (cursorPosition < inputBuffer.Length)
                {
                    FFI_ScreenReaderMod.SpeakText(GetCharacterName(inputBuffer[cursorPosition]), interrupt: true);
                    cursorPosition++;
                }
                return true;
            }

            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_UP) || WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_DOWN))
            {
                string text = inputBuffer.Length > 0 ? inputBuffer.ToString() : "empty";
                FFI_ScreenReaderMod.SpeakText(text, interrupt: true);
                return true;
            }

            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_HOME))
            {
                cursorPosition = 0;
                if (inputBuffer.Length > 0)
                    FFI_ScreenReaderMod.SpeakText(GetCharacterName(inputBuffer[0]), interrupt: true);
                return true;
            }

            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_END))
            {
                cursorPosition = inputBuffer.Length;
                return true;
            }

            if (WindowsFocusHelper.IsKeyDown(WindowsFocusHelper.VK_SPACE))
            {
                inputBuffer.Insert(cursorPosition, ' ');
                cursorPosition++;
                return true;
            }

            bool shiftHeld = WindowsFocusHelper.IsKeyPressed(WindowsFocusHelper.VK_SHIFT);

            for (int vk = WindowsFocusHelper.VK_A; vk <= WindowsFocusHelper.VK_Z; vk++)
            {
                if (WindowsFocusHelper.IsKeyDown(vk))
                {
                    char c = (char)('a' + (vk - WindowsFocusHelper.VK_A));
                    if (shiftHeld) c = char.ToUpper(c);
                    inputBuffer.Insert(cursorPosition, c);
                    cursorPosition++;
                    return true;
                }
            }

            for (int vk = WindowsFocusHelper.VK_0; vk <= WindowsFocusHelper.VK_9; vk++)
            {
                if (WindowsFocusHelper.IsKeyDown(vk))
                {
                    char c = (char)('0' + (vk - WindowsFocusHelper.VK_0));
                    inputBuffer.Insert(cursorPosition, c);
                    cursorPosition++;
                    return true;
                }
            }

            // Punctuation
            if (HandlePunctuation(WindowsFocusHelper.VK_OEM_MINUS, shiftHeld, '_', '-')) return true;
            if (HandlePunctuation(WindowsFocusHelper.VK_OEM_PERIOD, shiftHeld, '>', '.')) return true;
            if (HandlePunctuation(WindowsFocusHelper.VK_OEM_COMMA, shiftHeld, '<', ',')) return true;
            if (HandlePunctuation(WindowsFocusHelper.VK_OEM_7, shiftHeld, '"', '\'')) return true;
            if (HandlePunctuation(WindowsFocusHelper.VK_OEM_1, shiftHeld, ':', ';')) return true;
            if (HandlePunctuation(WindowsFocusHelper.VK_OEM_2, shiftHeld, '?', '/')) return true;
            if (HandlePunctuation(WindowsFocusHelper.VK_OEM_3, shiftHeld, '~', '`')) return true;
            if (HandlePunctuation(WindowsFocusHelper.VK_OEM_4, shiftHeld, '{', '[')) return true;
            if (HandlePunctuation(WindowsFocusHelper.VK_OEM_5, shiftHeld, '|', '\\')) return true;
            if (HandlePunctuation(WindowsFocusHelper.VK_OEM_6, shiftHeld, '}', ']')) return true;
            if (HandlePunctuation(WindowsFocusHelper.VK_OEM_PLUS, shiftHeld, '+', '=')) return true;

            return true; // Consume all input while dialog is open
        }

        private static bool HandlePunctuation(int vk, bool shiftHeld, char shiftChar, char normalChar)
        {
            if (WindowsFocusHelper.IsKeyDown(vk))
            {
                char c = shiftHeld ? shiftChar : normalChar;
                inputBuffer.Insert(cursorPosition, c);
                cursorPosition++;
                return true;
            }
            return false;
        }
    }
}
