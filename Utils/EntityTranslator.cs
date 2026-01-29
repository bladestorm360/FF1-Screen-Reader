using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MelonLoader;
using FFI_ScreenReader.Field;

namespace FFI_ScreenReader.Utils
{
    /// <summary>
    /// Translates Japanese entity names to English using an embedded dictionary.
    /// Translations are compiled into the DLL — no external file needed.
    /// </summary>
    public static class EntityTranslator
    {
        private static readonly Dictionary<string, string> translations = new Dictionary<string, string>
        {
            // === NPCs & Characters ===
            { "コウモリ", "Bat" },
            { "妖精王", "Fairy King" },
            { "カギのかかった扉", "Locked Door" },
            { "エルフ(村人)④", "Elf Villager 4" },
            { "エルフ(村人)③", "Elf Villager 3" },
            { "エルフ(村人)②", "Elf Villager 2" },
            { "エルフ(村人)①", "Elf Villager 1" },
            { "エルフ(村人)", "Elf Villager" },
            { "エルフ王子", "Elf Prince" },
            { "エルフ白ローブ", "White-Robed Elf" },
            { "井戸", "Well" },
            { "噴水", "Fountain" },
            { "村人(女性)", "Female Villager" },
            { "踊り子", "Dancer" },
            { "兵士", "Soldier" },
            { "村人(男性)", "Male Villager" },
            { "村人(おばあさん)", "Old Woman" },
            { "村人(おじいさん)", "Old Man" },
            { "飛空先入手", "Airship Acquisition" },

            // === Story Characters ===
            { "コーネリア王", "King of Cornelia" },
            { "コーネリア大臣", "Chancellor of Cornelia" },
            { "ガーランド", "Garland" },
            { "セーラ", "Princess Sarah" },
            { "マトーヤ", "Matoya" },
            { "ほうき", "Broom" },
            { "アストス", "Astos" },
            { "賢者サーダ", "Sage Sarda" },
            { "予言者ルカーン", "Prophet Lukahn" },
            { "バハムート", "Bahamut" },
            { "②ウネ", "Unne 2" },
            { "女性", "Woman" },
            { "男性", "Man" },
            { "巨人", "Giant" },

            // === Villagers (Mohawk, numbered) ===
            { "村人(モヒカン)青", "Blue Mohawk Villager" },
            { "村人(男性)①", "Male Villager 1" },

            // === Pirates ===
            { "海賊(ビッケ)", "Pirate (Bikke)" },
            { "海賊(下っ端)", "Pirate Underling" },

            // === Elves ===
            { "エルフ(村人)⑤", "Elf Villager 5" },
            { "エルフ(村人)⑦", "Elf Villager 7" },
            { "エルフ青ローブ②", "Blue-Robed Elf 2" },
            { "エルフ青ローブ⑥", "Blue-Robed Elf 6" },

            // === Dwarves ===
            { "ドワーフ(黄色の三角ニット帽)", "Dwarf" },
            { "ドワーフ(黄色の三角ニット帽)①", "Dwarf 1" },
            { "ドワーフ(黄色の三角ニット帽)②", "Dwarf 2" },
            { "ドワーフ(黄色の三角ニット帽)③", "Dwarf 3" },
            { "ドワーフ(黄色の三角ニット帽)④", "Dwarf 4" },
            { "ドワーフ(スミス)(赤の羽根つき帽子)", "Dwarf (Smith)" },
            { "ドワーフ(ネリク)(緑の帽子)", "Dwarf (Nerrick)" },
            { "スミスの作品", "Smith's Work" },

            // === Numbered Villagers ===
            { "②村人（男性）", "Male Villager 2" },
            { "③村人（おじいさん）", "Old Man 3" },
            { "④村人（男性）", "Male Villager 4" },
            { "⑤村人（男性）", "Male Villager 5" },
            { "⑥村人（男性）", "Male Villager 6" },
            { "⑦村人（女性）", "Female Villager 7" },
            { "⑧村人（男性）", "Male Villager 8" },
            { "⑨村人（女性）", "Female Villager 9" },
            { "⑩村人（男性）", "Male Villager 10" },
            { "⑪ドワーフ", "Dwarf 11" },
            { "⑫村人（おじいさん）", "Old Man 12" },
            { "⑬村人（男性）", "Male Villager 13" },
            { "⑭村人（男性）", "Male Villager 14" },

            // === Creatures & Objects ===
            { "ドラゴン", "Dragon" },
            { "魔女", "Witch" },
            { "モヒカン頭", "Mohawk" },
            { "学者", "Scholar" },
            { "墓標", "Grave Marker" },
            { "樽の潜水艦", "Barrel Submarine" },
            { "人魚の霊", "Mermaid Ghost" },
            { "動くほうき", "Moving Broom" },
            { "妖精解放エフェクト", "Fairy Release Effect" },

            // === Sages ===
            { "賢者", "Sage" },
            { "墓石", "Gravestone" },

            // === Bats (suffix-numbered) ===
            { "コウモリ1", "Bat1" },
            { "コウモリ2", "Bat2" },
            { "コウモリ3", "Bat3" },
            { "コウモリ4", "Bat4" },
            { "コウモリ5", "Bat5" },

            // === Vampires ===
            { "ヴァンパイア（コウモリ）", "Vampire (Bat)" },
            { "ヴァンパイア", "Vampire" },
            { "ヴァンパイア変身エフェクト", "Vampire Transformation Effect" },

            // === Dragons (suffix-numbered) ===
            { "ドラゴン1", "Dragon1" },
            { "ドラゴン2", "Dragon2" },
            { "ドラゴン3", "Dragon3" },
            { "ドラゴン4", "Dragon4" },
            { "ドラゴン5", "Dragon5" },
            { "ドラゴン6", "Dragon6" },
            { "ドラゴン7", "Dragon7" },
            { "ドラゴン8", "Dragon8" },
            { "ドラゴン9", "Dragon9" },
            { "ドラゴン10", "Dragon10" },
            { "クラスチェンジ用", "Class Change" },

            // === Mermaids ===
            { "人魚", "Mermaid" },
            { "人魚①", "Mermaid 1" },
            { "タル潜水艦", "Barrel Submarine" },

            // === Lufenians (prefix-numbered, no dot) ===
            { "1ルフェイン人", "1Lufenian" },
            { "2ルフェイン人", "2Lufenian" },
            { "3ルフェイン人", "3Lufenian" },
            { "4ルフェイン人", "4Lufenian" },
            { "5ルフェイン人", "5Lufenian" },
            { "6ルフェイン人", "6Lufenian" },
            { "7ルフェイン人", "7Lufenian" },
            { "8ルフェイン人", "8Lufenian" },
            { "9ルフェイン人", "9Lufenian" },
            { "10ルフェイン人", "10Lufenian" },
            { "11ルフェイン人", "11Lufenian" },
            { "12ルフェイン人", "12Lufenian" },
            { "13ルフェイン人", "13Lufenian" },
            { "14ルフェイン人", "14Lufenian" },

            // === Robots ===
            { "ロボット", "Robot" },
            { "ロボット電気", "Electric Robot" },
            { "ロボット(78)", "Robot" },

            // === Shops ===
            { "防具屋", "Armor Shop" },

            // === Crystals ===
            { "クリスタルのかけら青", "Blue Crystal Shard" },
            { "クリスタルのかけら緑", "Green Crystal Shard" },
            { "クリスタルのかけら赤", "Red Crystal Shard" },
            { "クリスタルのかけら黄", "Yellow Crystal Shard" },
            { "緑色のクリスタルのかけら", "Green Crystal Shard" },
            { "土のクリスタル暗", "Earth Crystal (Dark)" },
            { "土のクリスタル明", "Earth Crystal (Lit)" },
            { "火のクリスタル暗", "Fire Crystal (Dark)" },
            { "火のクリスタル明", "Fire Crystal (Lit)" },
            { "水のクリスタル明", "Water Crystal (Lit)" },
            { "水のクリスタル暗", "Water Crystal (Dark)" },
            { "風のクリスタル（明）", "Wind Crystal (Lit)" },
            { "風のクリスタル（暗）", "Wind Crystal (Dark)" },

            // === Crystal Cave Warps ===
            { "クリスタルの洞窟のワープ（黄）暗", "Crystal Cave Warp (Yellow) Dark" },
            { "クリスタルの洞窟のワープ（黄）明", "Crystal Cave Warp (Yellow) Lit" },
            { "クリスタルの洞窟のワープ（赤）暗", "Crystal Cave Warp (Red) Dark" },
            { "クリスタルの洞窟のワープ（赤）明", "Crystal Cave Warp (Red) Lit" },
            { "クリスタルの洞窟のワープ（青）暗", "Crystal Cave Warp (Blue) Dark" },
            { "クリスタルの洞窟のワープ（青）明", "Crystal Cave Warp (Blue) Lit" },
            { "クリスタルの洞窟のワープ（緑）明\n", "Crystal Cave Warp (Green) Lit" },
            { "クリスタルの洞窟のワープ（緑）暗\n", "Crystal Cave Warp (Green) Dark" },

            // === Orbs & Key Items ===
            { "黒色の玉", "Black Orb" },
            { "土色の玉（リッチ会話前）", "Earth Orb (Before Lich)" },
            { "赤色の玉(マリリス会話前)", "Fire Orb (Before Marilith)" },
            { "水色の玉(クラーケン会話前)", "Water Orb (Before Kraken)" },
            { "ティアマットのオーブ（会話前）", "Tiamat Orb (Before Dialogue)" },
            { "石板", "Stone Tablet" },
            { "石板（有）", "Stone Tablet (Present)" },
            { "スタールビー", "Star Ruby" },
            { "浮遊石", "Levistone" },
            { "アダマンタイト（有）", "Adamantite (Present)" },

            // === UI Markers ===
            { "イベント用「！」マーク", "Event Exclamation Mark" },
            { "ビックリマーク", "Exclamation Mark" },
            { "ビックリマーク1", "Exclamation Mark 1" },
            { "ビックリマーク2", "Exclamation Mark 2" },
            { "ビックリマーク3", "Exclamation Mark 3" },
            { "ビックリマーク4", "Exclamation Mark 4" },
            { "PC1.ビックリマーク", "PC1 Exclamation Mark" },
            { "PC2.ビックリマーク", "PC2 Exclamation Mark" },
            { "PC3.ビックリマーク", "PC3 Exclamation Mark" },
            { "PC4.ビックリマーク", "PC4 Exclamation Mark" },
            { "PC1ビックリマーク", "PC1 Exclamation Mark" },
            { "PC2ビックリマーク", "PC2 Exclamation Mark" },
            { "PC3ビックリマーク", "PC3 Exclamation Mark" },
            { "PC4ビックリマーク", "PC4 Exclamation Mark" },

            // === Forced Encounters ===
            { "強制エンカウント", "Forced Encounter" },
            { "強制エンカウント①", "Forced Encounter 1" },
            { "強制エンカウント②", "Forced Encounter 2" },
            { "強制エンカウント：ブルードラゴン", "Forced Encounter: Blue Dragon" },
            { "強制エンカウント：デスアイ", "Forced Encounter: Death Eye" },
            { "強制エンカウント：リッチ", "Forced Encounter: Lich" },
            { "強制エンカウント：マリリス", "Forced Encounter: Marilith" },
            { "強制エンカウント：クラーケン", "Forced Encounter: Kraken" },
            { "強制エンカウント：ティアマット", "Forced Encounter: Tiamat" },

            // === Warps & Interactions ===
            { "祭壇", "Altar" },
            { "ワープ", "Warp" },
            { "カギのかかった扉(確認用)", "Locked Door (Confirmation)" },
            { "おじいさんのテレポート", "Old Man's Teleport" },
            { "リュートで石板をどかす", "Move Tablet with Lute" },
            { "浮遊城へワープ", "Warp to Flying Fortress" },
            { "ミラージュの塔へワープ", "Warp to Mirage Tower" },
            { "過去のカオス神殿にワープ", "Warp to Past Chaos Shrine" },
            { "カオス神殿へワープ", "Warp to Chaos Shrine" },
            { "クリスタルの洞窟のワープ（赤）明(仮置き）", "Crystal Cave Warp (Red) Lit" },
            { "テレポートマス", "Teleport Tile" },
            { "展望窓を覗く", "Look Through Window" },

            // === Floor Warps ===
            { "1Fへワープ", "Warp to 1F" },
            { "2Fへワープ", "Warp to 2F" },
            { "3Fへワープ", "Warp to 3F" },
            { "4Fへワープ", "Warp to 4F" },
            { "5Fへワープ", "Warp to 5F" },

            // === Numbered Warps ===
            { "③へワープ", "Warp to 3" },
            { "④へワープ", "Warp to 4" },
            { "⑤へワープ", "Warp to 5" },
            { "⑥へワープ", "Warp to 6" },
            { "⑦へワープ", "Warp to 7" },
            { "⑧へワープ", "Warp to 8" },
            { "⑩へワープ", "Warp to 10" },
            { "⑪へワープ", "Warp to 11" },
            { "⑫へワープ", "Warp to 12" },
            { "⑬へワープ", "Warp to 13" },
        };

        private static bool isInitialized = false;

        // Track untranslated names by map for dev logging
        private static Dictionary<string, HashSet<string>> untranslatedNamesByMap = new Dictionary<string, HashSet<string>>();

        // Matches numeric prefix (e.g., "6:" or "12.") or SC prefix (e.g., "SC01:") at start of entity names
        private static readonly Regex EntityPrefixRegex = new Regex(
            @"^((?:SC)?\d+[.:])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Initializes the translator. Translations are embedded at compile time.
        /// </summary>
        public static void Initialize()
        {
            if (isInitialized) return;
            MelonLogger.Msg($"[EntityTranslator] Initialized with {translations.Count} embedded translations");
            isInitialized = true;
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

            // 1. Exact match first (preserves existing behavior)
            if (translations.TryGetValue(japaneseName, out string englishName))
                return englishName;

            // 2. Strip numeric/SC prefix and try base name lookup
            StripPrefix(japaneseName, out string prefix, out string baseName);
            if (prefix != null && translations.TryGetValue(baseName, out string baseTranslation))
                return prefix + baseTranslation;

            // 3. Track untranslated name by current map (use base name to deduplicate)
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
        /// Strips a numeric or SC prefix from an entity name.
        /// Returns the prefix (e.g., "6:" or "SC01:") and the base name.
        /// If no prefix is found, prefix will be null and baseName will equal the input.
        /// </summary>
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
        public static int TranslationCount => translations.Count;

        /// <summary>
        /// Clears the untranslated names tracking dictionary.
        /// Call on map transitions to prevent unbounded memory growth.
        /// </summary>
        public static void ClearUntranslatedTracking()
        {
            untranslatedNamesByMap.Clear();
        }
    }
}
