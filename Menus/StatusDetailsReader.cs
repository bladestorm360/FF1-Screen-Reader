using System;
using System.Collections.Generic;
using MelonLoader;
using Il2CppLast.Data.User;
using Il2CppSerial.FF1.UI.KeyInput;
using FFI_ScreenReader.Core;

namespace FFI_ScreenReader.Menus
{
    /// <summary>
    /// Reads character status details from the status details view.
    /// </summary>
    public static class StatusDetailsReader
    {
        private static OwnedCharacterData currentCharacterData = null;

        public static void SetCurrentCharacterData(OwnedCharacterData data)
        {
            currentCharacterData = data;
        }

        public static void ClearCurrentCharacterData()
        {
            currentCharacterData = null;
        }

        public static OwnedCharacterData GetCurrentCharacterData()
        {
            return currentCharacterData;
        }

        /// <summary>
        /// Read all character status information from the status details view.
        /// Returns a formatted string with all relevant information.
        /// </summary>
        public static string ReadStatusDetails(StatusDetailsController controller)
        {
            if (controller == null)
                return null;

            var parts = new List<string>();

            if (currentCharacterData != null)
            {
                try
                {
                    string name = currentCharacterData.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                        parts.Add(name);

                    var param = currentCharacterData.Parameter;
                    if (param != null)
                    {
                        parts.Add($"Level {param.ConfirmedLevel()}");
                        parts.Add($"HP: {param.currentHP} / {param.ConfirmedMaxHp()}");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Error reading status details: {ex.Message}");
                }
            }

            return parts.Count > 0 ? string.Join(". ", parts) : null;
        }
    }
}
