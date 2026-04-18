using System;
using System.Collections;
using UnityEngine;
using MelonLoader;
using FFI_ScreenReader.Patches;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Core.Handlers
{
    /// <summary>
    /// Handles function key input (F1, F3, F5, F8).
    /// Returns true if a key was consumed.
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

            if (Input.GetKeyDown(KeyCode.F1))
            {
                CoroutineManager.StartUntracked(AnnounceWalkRunState());
                return true;
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                CoroutineManager.StartUntracked(AnnounceEncounterState());
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

        internal static IEnumerator AnnounceWalkRunState()
        {
            yield return null;
            yield return null;
            yield return null;

            try
            {
                bool isDashing = MoveStateHelper.GetDashFlag();
                string state = isDashing ? T("Run") : T("Walk");
                FFI_ScreenReaderMod.SpeakText(state, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[F1] Error reading walk/run state: {ex.Message}");
            }
        }

        internal static IEnumerator AnnounceEncounterState()
        {
            yield return null;
            try
            {
                var userData = Il2CppLast.Management.UserDataManager.Instance();
                if (userData?.CheatSettingsData != null)
                {
                    bool enabled = userData.CheatSettingsData.IsEnableEncount;
                    string state = enabled ? T("Encounters on") : T("Encounters off");
                    FFI_ScreenReaderMod.SpeakText(state, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[F3] Error reading encounter state: {ex.Message}");
            }
        }
    }
}
