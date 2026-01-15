using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using static FFI_ScreenReader.Utils.TextUtils;
using MenuManager = Il2CppLast.UI.MenuManager;

namespace FFI_ScreenReader.Menus
{
    /// <summary>
    /// Handles reading character information from character selection screens.
    /// Used in menus like Status, Magic, Equipment, etc.
    /// Extracts and announces: character name, job, level, HP, MP.
    /// FF1-specific: Uses job system (Fighter, Thief, Black Mage, etc.)
    /// </summary>
    public static class CharacterSelectionReader
    {
        /// <summary>
        /// Try to read character information from the current cursor position.
        /// Returns a formatted string with character information, or null if not a character selection.
        /// Format: "Name, Job, Level X, HP current/max, MP current/max"
        /// </summary>
        public static string TryReadCharacterSelection(Transform cursorTransform, int cursorIndex)
        {
            try
            {
                // Safety check: Only read character data if we're in a menu or battle
                // This prevents character data from being read during game load
                var sceneName = SceneManager.GetActiveScene().name;
                bool isBattleScene = sceneName != null && sceneName.Contains("Battle");
                bool isMenuOpen = false;

                try
                {
                    var menuManager = MenuManager.Instance;
                    if (menuManager != null)
                    {
                        isMenuOpen = menuManager.IsOpen;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Could not check MenuManager.IsOpen: {ex.Message}");
                }

                if (!isBattleScene && !isMenuOpen)
                {
                    return null;
                }

                // Walk up the hierarchy to find character selection structures
                Transform current = cursorTransform;
                int depth = 0;

                while (current != null && depth < 15)
                {
                    string lowerName = current.name.ToLower();

                    // Look for character selection menu structures
                    if (lowerName.Contains("character") || lowerName.Contains("chara") ||
                        lowerName.Contains("status") || lowerName.Contains("formation") ||
                        lowerName.Contains("party") || lowerName.Contains("member"))
                    {
                        // Try to find Content list
                        Transform contentList = FindContentList(current);

                        if (contentList != null && cursorIndex >= 0 && cursorIndex < contentList.childCount)
                        {
                            Transform characterSlot = contentList.GetChild(cursorIndex);

                            // Try to read the character information
                            string characterInfo = ReadCharacterInformation(characterSlot);
                            if (characterInfo != null)
                            {
                                return characterInfo;
                            }
                        }
                    }

                    // Also check if we're directly on a character info element
                    if (lowerName.Contains("info_content") || lowerName.Contains("status_info") ||
                        lowerName.Contains("chara_status"))
                    {
                        string characterInfo = ReadCharacterInformation(current);
                        if (characterInfo != null)
                        {
                            return characterInfo;
                        }
                    }

                    current = current.parent;
                    depth++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"CharacterSelectionReader error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Check if a string contains only numeric characters.
        /// Used to filter out job IDs (numbers) vs job names (text).
        /// </summary>
        private static bool IsNumericOnly(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (char c in value)
            {
                if (!char.IsDigit(c))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Find the Content transform within a ScrollView structure.
        /// </summary>
        private static Transform FindContentList(Transform root)
        {
            try
            {
                var content = FindTransformInChildren(root, "Content");
                if (content != null && content.parent != null &&
                    (content.parent.name == "Viewport" || content.parent.parent?.name == "Scroll View"))
                {
                    return content;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error finding content list: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Read character information from a character slot transform.
        /// Returns formatted announcement string or null if unable to read.
        /// </summary>
        private static string ReadCharacterInformation(Transform slotTransform)
        {
            try
            {
                // Try to read from text components
                return ReadFromTextComponents(slotTransform);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading character information: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Read character information from text components.
        /// Looks for specific text field names to extract character data.
        /// </summary>
        private static string ReadFromTextComponents(Transform slotTransform)
        {
            try
            {
                string characterName = null;
                string jobName = null;
                string level = null;
                string currentHP = null;
                string maxHP = null;
                string currentMP = null;
                string maxMP = null;

                ForEachTextInChildren(slotTransform, text =>
                {
                    if (text == null || text.text == null) return;

                    string content = text.text.Trim();
                    if (string.IsNullOrEmpty(content)) return;

                    string textName = text.name.ToLower();

                    // Check for character name
                    // Name text fields - exclude job names, area names, etc.
                    if ((textName.Contains("name") && !textName.Contains("job") &&
                        !textName.Contains("area") && !textName.Contains("floor") &&
                        !textName.Contains("slot")) ||
                        textName == "nametext" || textName == "name_text")
                    {
                        // Skip labels and very short text
                        if (content.Length > 1 && !content.Contains(":") &&
                            content != "HP" && content != "MP" && content != "Lv")
                        {
                            characterName = content;
                        }
                    }
                    // Check for job/class name (FF1: Fighter, Thief, Black Mage, White Mage, etc.)
                    else if ((textName.Contains("job") || textName.Contains("class")) &&
                             !textName.Contains("label") && !textName.Contains("id") &&
                             !textName.Contains("level") && !textName.Contains("lv"))
                    {
                        // Filter out numeric-only content (likely a job ID, not a job name)
                        if (!IsNumericOnly(content))
                        {
                            jobName = content;
                        }
                    }
                    // Check for level
                    else if ((textName.Contains("level") || textName.Contains("lv")) &&
                             !textName.Contains("label") && !textName.Contains("fixed"))
                    {
                        // Skip "Lv" label, get the number
                        if (content != "Lv" && content != "Level" && content != "LV")
                        {
                            level = content;
                        }
                    }
                    // Check for HP values
                    else if (textName.Contains("hp") && !textName.Contains("label"))
                    {
                        if (textName.Contains("current") || textName.Contains("now"))
                        {
                            currentHP = content;
                        }
                        else if (textName.Contains("max"))
                        {
                            maxHP = content;
                        }
                    }
                    // Check for MP values (FF1 uses spell slots per level, but may still show MP)
                    else if (textName.Contains("mp") && !textName.Contains("label"))
                    {
                        if (textName.Contains("current") || textName.Contains("now"))
                        {
                            currentMP = content;
                        }
                        else if (textName.Contains("max"))
                        {
                            maxMP = content;
                        }
                    }
                });

                // Build announcement string
                // Format: "Name, Job, Level X, HP current/max, MP current/max"
                string announcement = "";

                // Start with character name
                if (!string.IsNullOrEmpty(characterName))
                {
                    announcement = characterName;
                }

                // Add job name
                if (!string.IsNullOrEmpty(jobName))
                {
                    if (!string.IsNullOrEmpty(announcement))
                    {
                        announcement += ", " + jobName;
                    }
                    else
                    {
                        announcement = jobName;
                    }
                }

                // Add level
                if (!string.IsNullOrEmpty(level))
                {
                    if (!string.IsNullOrEmpty(announcement))
                    {
                        announcement += ", Level " + level;
                    }
                    else
                    {
                        announcement = "Level " + level;
                    }
                }

                // Add HP
                if (!string.IsNullOrEmpty(currentHP))
                {
                    if (!string.IsNullOrEmpty(maxHP))
                    {
                        announcement += $", HP {currentHP}/{maxHP}";
                    }
                    else
                    {
                        announcement += $", HP {currentHP}";
                    }
                }

                // Add MP (if available in FF1)
                if (!string.IsNullOrEmpty(currentMP))
                {
                    if (!string.IsNullOrEmpty(maxMP))
                    {
                        announcement += $", MP {currentMP}/{maxMP}";
                    }
                    else
                    {
                        announcement += $", MP {currentMP}";
                    }
                }

                if (!string.IsNullOrEmpty(announcement))
                {
                    return announcement;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading character text components: {ex.Message}");
            }

            return null;
        }
    }
}
