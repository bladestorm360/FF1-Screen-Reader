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

        // Matches a parenthesized suffix at the end of entity names (half-width or full-width parens)
        private static readonly Regex ParenSuffixRegex = new Regex(
            @"[(\uff08][^)\uff09]*[)\uff09]$",
            RegexOptions.Compiled);

        // Matches leading enumeration markers (circled digits) at start of entity names
        private static readonly Regex LeadingEnumPrefixRegex = new Regex(
            @"^[①-⑳]+",
            RegexOptions.Compiled);

        // Matches leading plain ASCII digits (Unity-instance enumeration, e.g., "15ルフェイン人").
        // Negative lookahead for [\d.:] defers "1:村人(...)" / "12:村人(...)" style names to
        // EntityPrefixRegex without backtracking into a partial digit match (which would leave
        // the colon orphaned and produce e.g. "2: Male Villager 1").
        private static readonly Regex LeadingDigitPrefixRegex = new Regex(
            @"^\d+(?![\d.:])",
            RegexOptions.Compiled);

        /// <summary>
        /// Converts a string of circled digits (U+2460..U+2473) to space-separated ASCII numbers.
        /// </summary>
        private static string CircledDigitToNumber(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= '①' && c <= '⑳')
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append((c - '①') + 1);
                }
            }
            return sb.ToString();
        }

        // Matches trailing enumeration markers (circled digits) at end of entity names
        private static readonly Regex TrailingEnumSuffixRegex = new Regex(
            @"[\u2460-\u2473]+$",
            RegexOptions.Compiled);

        // Matches trailing ASCII digits (Unity-instance enumeration suffix, e.g., "\u843d\u3068\u3057\u7a742")
        private static readonly Regex TrailingDigitSuffixRegex = new Regex(
            @"\d+$",
            RegexOptions.Compiled);

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

            // Extract trailing enumeration suffix (e.g., "①") so lookups target the base name;
            // the suffix is reappended to whichever translation tier succeeds.
            string enumSuffix = "";
            string coreName = japaneseName;
            Match enumMatch = TrailingEnumSuffixRegex.Match(japaneseName);
            if (enumMatch.Success)
            {
                enumSuffix = " " + enumMatch.Value;
                coreName = japaneseName.Substring(0, enumMatch.Index);
            }

            // Trailing plain-digit instance suffix (Unity-style enumeration). Index > 0
            // guard avoids matching pure-numeric names.
            if (enumSuffix.Length == 0)
            {
                Match digitMatch = TrailingDigitSuffixRegex.Match(coreName);
                if (digitMatch.Success && digitMatch.Index > 0)
                {
                    enumSuffix = " " + digitMatch.Value;
                    coreName = coreName.Substring(0, digitMatch.Index);
                }
            }

            // Extract leading enumeration prefix (e.g., "⑭"). The numeric form is appended as
            // a suffix to the translation, so "⑭村人（男性）" → "Male Villager 14".
            string leadingEnumSuffix = "";
            Match leadingMatch = LeadingEnumPrefixRegex.Match(coreName);
            if (leadingMatch.Success)
            {
                leadingEnumSuffix = " " + CircledDigitToNumber(leadingMatch.Value);
                coreName = coreName.Substring(leadingMatch.Length);
            }

            // Leading plain-digit instance prefix (e.g., "15ルフェイン人" → "Lufenian 15").
            // Length < coreName.Length guard avoids stripping a pure-numeric name.
            if (leadingEnumSuffix.Length == 0)
            {
                Match leadingDigitMatch = LeadingDigitPrefixRegex.Match(coreName);
                if (leadingDigitMatch.Success && leadingDigitMatch.Length < coreName.Length)
                {
                    leadingEnumSuffix = " " + leadingDigitMatch.Value;
                    coreName = coreName.Substring(leadingDigitMatch.Length);
                }
            }

            // 1. Exact match
            if (TryLookup(coreName, out string exactMatch))
                return exactMatch + leadingEnumSuffix + enumSuffix;

            // 1b. Normalize full-width parens to half-width and retry exact match
            string normalized = NormalizeParens(coreName);
            if (normalized != coreName && TryLookup(normalized, out string normalizedMatch))
                return normalizedMatch + leadingEnumSuffix + enumSuffix;

            // 2. Strip numeric/SC prefix and try base name lookup
            StripPrefix(coreName, out string prefix, out string baseName);
            if (prefix != null && TryLookup(baseName, out string baseTranslation))
                return prefix + " " + baseTranslation + leadingEnumSuffix + enumSuffix;

            // 3. Strip parenthesized suffix (e.g., "兵士(e_v_0002専用)" → "兵士") and try base name
            string nameForSuffix = prefix != null ? baseName : coreName;
            string stripped = ParenSuffixRegex.Replace(nameForSuffix, "").Trim();
            if (stripped != nameForSuffix && stripped.Length > 0 && TryLookup(stripped, out string strippedMatch))
                return (prefix != null ? prefix + " " + strippedMatch : strippedMatch) + leadingEnumSuffix + enumSuffix;

            // 4. Track untranslated name by current map
            string trackingName = prefix != null ? baseName : coreName;
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

        /// <summary>
        /// Normalizes full-width parentheses to half-width for dictionary matching.
        /// </summary>
        private static string NormalizeParens(string name)
        {
            return name.Replace('\uff08', '(').Replace('\uff09', ')');
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
