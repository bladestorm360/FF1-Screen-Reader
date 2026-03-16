using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MelonLoader;
using FFI_ScreenReader.Field;
using Il2CppLast.Management;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Translates Japanese entity names to the current game language using an embedded translation resource.
    /// Uses a multi-tier lookup: exact → strip prefix.
    /// Detects language via MessageManager.Instance.currentLanguage.
    /// Fallback: detected language → English → original Japanese.
    /// </summary>
    public static class EntityTranslator
    {
        private static Dictionary<string, Dictionary<string, string>> translations;
        private static bool isInitialized = false;
        private static string cachedLanguageCode = "en";
        private static bool hasLoggedLanguage = false;

        // Track untranslated names by map for dev logging
        private static Dictionary<string, HashSet<string>> untranslatedNamesByMap = new Dictionary<string, HashSet<string>>();

        private static readonly Dictionary<int, string> LanguageCodeMap = new()
        {
            {1,"ja"},{2,"en"},{3,"fr"},{4,"it"},{5,"de"},{6,"es"},
            {7,"ko"},{8,"zht"},{9,"zhc"},{10,"ru"},{11,"th"},{12,"pt"}
        };

        // Matches numeric prefix (e.g., "6:" or "12.") or SC prefix (e.g., "SC01:") at start of entity names
        private static readonly Regex EntityPrefixRegex = new Regex(
            @"^((?:SC)?\d+[.:])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Detects the current game language via MessageManager and returns a language code.
        /// </summary>
        public static string DetectLanguage()
        {
            try
            {
                var mgr = MessageManager.Instance;
                if (mgr != null)
                {
                    int langId = (int)mgr.currentLanguage;
                    if (LanguageCodeMap.TryGetValue(langId, out string code))
                    {
                        cachedLanguageCode = code;
                        if (!hasLoggedLanguage)
                        {
                            MelonLogger.Msg($"[EntityTranslator] Detected language: {cachedLanguageCode}");
                            hasLoggedLanguage = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!hasLoggedLanguage)
                    MelonLogger.Msg($"[EntityTranslator] DetectLanguage exception: {ex.Message}");
            }
            return cachedLanguageCode;
        }

        /// <summary>
        /// Loads the embedded translation resource into the multi-language lookup dictionary.
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;

            translations = new Dictionary<string, Dictionary<string, string>>();

            try
            {
                using var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("translation.json");

                if (stream != null)
                {
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    string json = reader.ReadToEnd();

                    var data = ModTextTranslator.ParseNestedJson(json);

                    foreach (var entry in data)
                    {
                        bool hasValue = false;
                        foreach (var langEntry in entry.Value)
                        {
                            if (!string.IsNullOrEmpty(langEntry.Value))
                            {
                                hasValue = true;
                                break;
                            }
                        }
                        if (hasValue)
                            translations[entry.Key] = entry.Value;
                    }

                    MelonLogger.Msg($"[EntityTranslator] Loaded {translations.Count} translations from embedded resource");
                }
                else
                {
                    MelonLogger.Warning("[EntityTranslator] Embedded translation resource not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[EntityTranslator] Error loading translations: {ex.Message}");
            }

            isInitialized = true;
        }

        /// <summary>
        /// Translates a Japanese entity name to the current game language.
        /// Returns original name if no translation found.
        /// Multi-tier lookup: exact → strip prefix.
        /// </summary>
        public static string Translate(string japaneseName)
        {
            if (string.IsNullOrEmpty(japaneseName))
                return japaneseName;

            if (!isInitialized)
                Initialize();

            if (translations.Count == 0)
                return japaneseName;

            // When game is in Japanese, entity names are already Japanese — no translation needed
            if (DetectLanguage() == "ja")
                return japaneseName;

            // 1. Exact match
            if (TryLookup(japaneseName, out string exactMatch))
                return exactMatch;

            // 2. Strip numeric/SC prefix and try base name lookup
            StripPrefix(japaneseName, out string prefix, out string baseName);
            if (prefix != null && TryLookup(baseName, out string baseTranslation))
                return prefix + " " + baseTranslation;

            // 3. Track untranslated name by current map
            string trackingName = prefix != null ? baseName : japaneseName;
            if (ContainsJapanese(trackingName))
            {
                string mapName = MapNameResolver.GetCurrentMapName();
                if (!string.IsNullOrEmpty(mapName))
                {
                    if (!untranslatedNamesByMap.ContainsKey(mapName))
                        untranslatedNamesByMap[mapName] = new HashSet<string>();
                    untranslatedNamesByMap[mapName].Add(trackingName);
                }
            }

            return japaneseName;
        }

        /// <summary>
        /// Looks up a Japanese key in the translations dictionary for the current game language.
        /// </summary>
        private static bool TryLookup(string key, out string result)
        {
            result = null;
            if (!translations.TryGetValue(key, out var langDict))
                return false;
            string lang = DetectLanguage();
            if (langDict.TryGetValue(lang, out string localized) && !string.IsNullOrEmpty(localized))
            {
                result = localized;
                return true;
            }
            // Fallback to English
            if (lang != "en" && langDict.TryGetValue("en", out string english) && !string.IsNullOrEmpty(english))
            {
                result = english;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if a string contains Japanese characters.
        /// </summary>
        public static bool ContainsJapanese(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                if ((c >= '\u3040' && c <= '\u309F') ||  // Hiragana
                    (c >= '\u30A0' && c <= '\u30FF') ||  // Katakana
                    (c >= '\u4E00' && c <= '\u9FFF'))     // CJK
                    return true;
            }
            return false;
        }

        private static void StripPrefix(string name, out string prefix, out string baseName)
        {
            Match match = EntityPrefixRegex.Match(name);
            if (match.Success)
            {
                prefix = match.Groups[1].Value;
                baseName = name.Substring(prefix.Length);
            }
            else
            {
                prefix = null;
                baseName = name;
            }
        }

        /// <summary>
        /// Gets the count of loaded translations.
        /// </summary>
        public static int TranslationCount => translations?.Count ?? 0;

        /// <summary>
        /// Clears the untranslated names tracking dictionary.
        /// </summary>
        public static void ClearUntranslatedTracking()
        {
            untranslatedNamesByMap.Clear();
        }
    }
}
