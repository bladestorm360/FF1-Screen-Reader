using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using UnityEngine;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Translates Japanese entity names to English using a JSON dictionary.
    /// Loaded from UserData/FFI_ScreenReader/FF1_translations.json
    /// </summary>
    public static class EntityTranslator
    {
        private static Dictionary<string, string> translations = new Dictionary<string, string>();
        private static bool isInitialized = false;
        private static string translationsPath;

        // Track untranslated names for dumping
        private static HashSet<string> untranslatedNames = new HashSet<string>();

        /// <summary>
        /// Initializes the translator by loading translations from JSON file.
        /// Creates empty file if not exists.
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;

            try
            {
                // Build path: UserData/FFI_ScreenReader/translations.json
                // Get game root directory (parent of _Data folder)
                string gameDataPath = Application.dataPath; // e.g., .../FINAL FANTASY/FINAL FANTASY_Data
                string gameRoot = Path.GetDirectoryName(gameDataPath);
                string userDataPath = Path.Combine(gameRoot, "UserData", "FFI_ScreenReader");
                translationsPath = Path.Combine(userDataPath, "FF1_translations.json");

                // Create directory if needed
                if (!Directory.Exists(userDataPath))
                {
                    Directory.CreateDirectory(userDataPath);
                    MelonLogger.Msg($"[EntityTranslator] Created directory: {userDataPath}");
                }

                // Load or create translations file
                if (File.Exists(translationsPath))
                {
                    LoadTranslations();
                }
                else
                {
                    // Create empty translations file
                    File.WriteAllText(translationsPath, "{\n}");
                    MelonLogger.Msg($"[EntityTranslator] Created empty translations file: {translationsPath}");
                }

                isInitialized = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[EntityTranslator] Failed to initialize: {ex.Message}");
                isInitialized = true; // Prevent repeated init attempts
            }
        }

        /// <summary>
        /// Loads translations from the JSON file.
        /// </summary>
        private static void LoadTranslations()
        {
            try
            {
                string json = File.ReadAllText(translationsPath);
                translations = ParseJsonDictionary(json);
                MelonLogger.Msg($"[EntityTranslator] Loaded {translations.Count} translations from {translationsPath}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[EntityTranslator] Failed to load translations: {ex.Message}");
                translations = new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Simple JSON dictionary parser (no external dependencies).
        /// Parses {"key": "value", ...} format.
        /// </summary>
        private static Dictionary<string, string> ParseJsonDictionary(string json)
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrWhiteSpace(json))
                return result;

            // Remove outer braces and whitespace
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);
            json = json.Trim();

            if (string.IsNullOrEmpty(json))
                return result;

            // Parse key-value pairs
            int pos = 0;
            while (pos < json.Length)
            {
                // Find opening quote for key
                int keyStart = json.IndexOf('"', pos);
                if (keyStart < 0) break;

                // Find closing quote for key
                int keyEnd = json.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;

                string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);

                // Find colon
                int colonPos = json.IndexOf(':', keyEnd);
                if (colonPos < 0) break;

                // Find opening quote for value
                int valueStart = json.IndexOf('"', colonPos);
                if (valueStart < 0) break;

                // Find closing quote for value (handle escaped quotes)
                int valueEnd = valueStart + 1;
                while (valueEnd < json.Length)
                {
                    valueEnd = json.IndexOf('"', valueEnd);
                    if (valueEnd < 0) break;

                    // Check if escaped
                    int backslashes = 0;
                    int checkPos = valueEnd - 1;
                    while (checkPos >= valueStart && json[checkPos] == '\\')
                    {
                        backslashes++;
                        checkPos--;
                    }

                    if (backslashes % 2 == 0)
                        break; // Not escaped

                    valueEnd++;
                }

                if (valueEnd < 0) break;

                string value = json.Substring(valueStart + 1, valueEnd - valueStart - 1);

                // Unescape basic sequences
                value = value.Replace("\\\"", "\"").Replace("\\\\", "\\");

                result[key] = value;

                // Move to next pair
                pos = valueEnd + 1;
            }

            return result;
        }

        /// <summary>
        /// Translates a Japanese entity name to English.
        /// Returns original name if no translation found.
        /// </summary>
        public static string Translate(string japaneseName)
        {
            if (string.IsNullOrEmpty(japaneseName))
                return japaneseName;

            // Ensure initialized
            if (!isInitialized)
                Initialize();

            // Look up translation
            if (translations.TryGetValue(japaneseName, out string englishName))
            {
                return englishName;
            }

            // Track untranslated name for potential dump
            if (ContainsJapanese(japaneseName))
            {
                untranslatedNames.Add(japaneseName);
            }

            // Return original if no translation
            return japaneseName;
        }

        /// <summary>
        /// Checks if a string contains Japanese characters (hiragana, katakana, or kanji).
        /// </summary>
        private static bool ContainsJapanese(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            foreach (char c in text)
            {
                // Hiragana: U+3040 - U+309F
                // Katakana: U+30A0 - U+30FF
                // Kanji: U+4E00 - U+9FFF (common CJK)
                if ((c >= '\u3040' && c <= '\u309F') ||  // Hiragana
                    (c >= '\u30A0' && c <= '\u30FF') ||  // Katakana
                    (c >= '\u4E00' && c <= '\u9FFF'))    // Common Kanji
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Dumps all untranslated entity names to EntityNames.json.
        /// Called by the ' hotkey.
        /// </summary>
        public static void DumpUntranslatedNames()
        {
            if (untranslatedNames.Count == 0)
            {
                MelonLogger.Msg("[EntityTranslator] No untranslated entity names found.");
                return;
            }

            try
            {
                string dumpPath = Path.Combine(
                    Path.GetDirectoryName(translationsPath),
                    "EntityNames.json"
                );

                // Build proper JSON
                using (var writer = new StreamWriter(dumpPath, false, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine("{");
                    var names = new List<string>(untranslatedNames);
                    for (int i = 0; i < names.Count; i++)
                    {
                        string escaped = names[i].Replace("\\", "\\\\").Replace("\"", "\\\"");
                        string comma = (i < names.Count - 1) ? "," : "";
                        writer.WriteLine($"  \"{escaped}\": \"\"{comma}");
                    }
                    writer.WriteLine("}");
                }

                MelonLogger.Msg($"[EntityTranslator] Saved {untranslatedNames.Count} untranslated names to: {dumpPath}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[EntityTranslator] Failed to save EntityNames.json: {ex.Message}");
            }
        }

        /// <summary>
        /// Reloads translations from file.
        /// </summary>
        public static void Reload()
        {
            if (!string.IsNullOrEmpty(translationsPath) && File.Exists(translationsPath))
            {
                LoadTranslations();
            }
        }

        /// <summary>
        /// Gets the count of loaded translations.
        /// </summary>
        public static int TranslationCount => translations.Count;
    }
}
