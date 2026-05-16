using UnityEngine;
using FFI_ScreenReader.Patches;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Core.Handlers
{
    /// <summary>
    /// Handles function key input (F4, F5, F6, F8).
    /// F1 (walk/run) and F3 (encounter) announcements are now delivered by GameToggleAnnouncer,
    /// which watches the underlying game state and announces on change rather than on keypress.
    /// </summary>
    internal static class FunctionKeyHandler
    {
        internal static bool HandleInput()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                if (!BattleStateHelper.IsInBattle)
                {
                    ModMenu.Open();
                }
                else
                {
                    FFI_ScreenReaderMod.SpeakText(T("Unavailable in battle"), interrupt: true);
                }
                return true;
            }

            if (Input.GetKeyDown(KeyCode.F5))
            {
                if (!BattleStateHelper.IsInBattle)
                {
                    int current = PreferencesManager.EnemyHPDisplay;
                    int next = (current + 1) % 3;
                    PreferencesManager.SetEnemyHPDisplay(next);

                    string[] options = { T("Numbers"), T("Percentage"), T("Hidden") };
                    FFI_ScreenReaderMod.SpeakText(string.Format(T("Enemy HP: {0}"), options[next]), interrupt: true);
                }
                else
                {
                    FFI_ScreenReaderMod.SpeakText(T("Unavailable in battle"), interrupt: true);
                }
                return true;
            }

            if (Input.GetKeyDown(KeyCode.F4))
            {
                FFI_ScreenReaderMod.Instance?.ToggleAutoDetail();
                return true;
            }

            if (Input.GetKeyDown(KeyCode.F6))
            {
                FFI_ScreenReaderMod.Instance?.ToggleAudioBeacons();
                return true;
            }

            return false;
        }
    }
}
