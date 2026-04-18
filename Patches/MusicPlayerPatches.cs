using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Menus;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;
using Il2CppLast.Management;
using Il2CppLast.UI.KeyInput;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Tracks music player (Extra Sound) scene state.
    /// Mirrors BestiaryStateTracker pattern: coroutine clears suppression flag in finally.
    /// </summary>
    public static class MusicPlayerStateTracker
    {
        public static bool IsInMusicPlayer { get; set; } = false;
        public static bool SuppressContentChange { get; set; } = false;
        public static IntPtr CachedFocusedPtr { get; set; } = IntPtr.Zero;

        // ExtraSoundListController field offsets
        public const int OFFSET_CURRENT_LIST_TYPE = 0xC0;  // currentListType (AudioManager.BgmType)

        public static void ClearState()
        {
            IsInMusicPlayer = false;
            SuppressContentChange = false;
            CachedFocusedPtr = IntPtr.Zero;
            MenuStateRegistry.Reset(MenuStateRegistry.MUSIC_PLAYER);
            AnnouncementDeduplicator.Reset(AnnouncementContexts.MUSIC_LIST_ENTRY);
        }
    }

    /// <summary>
    /// Patches for the Music Player (Extra Sound) extras menu.
    /// Uses manual Harmony patching to avoid silent failures from attribute-based patches.
    /// </summary>
    internal static class MusicPlayerManualPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch 1: SubSceneManagerExtraSound.ChangeState
                var changeStateMethod = AccessTools.Method(
                    typeof(SubSceneManagerExtraSound), "ChangeState");
                if (changeStateMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(MusicPlayerManualPatches), nameof(ChangeState_Postfix));
                    harmony.Patch(changeStateMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[MusicPlayer] SubSceneManagerExtraSound.ChangeState not found");
                }

                // Patch 2: ExtraSoundListContentController.SetFocus
                var setFocusMethod = AccessTools.Method(
                    typeof(ExtraSoundListContentController), "SetFocus");
                if (setFocusMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(MusicPlayerManualPatches), nameof(SetFocus_Postfix));
                    harmony.Patch(setFocusMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[MusicPlayer] ExtraSoundListContentController.SetFocus not found");
                }

                // Patch 3: ExtraSoundController.ChangeKeyHelpPlaybackIcon
                var playbackIconMethod = AccessTools.Method(
                    typeof(ExtraSoundController), "ChangeKeyHelpPlaybackIcon");
                if (playbackIconMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(MusicPlayerManualPatches), nameof(ChangeKeyHelpPlaybackIcon_Postfix));
                    harmony.Patch(playbackIconMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[MusicPlayer] ExtraSoundController.ChangeKeyHelpPlaybackIcon not found");
                }

                // Patch 4: ExtraSoundListController.SwitchOriginalArrangeList
                var switchListMethod = AccessTools.Method(
                    typeof(ExtraSoundListController), "SwitchOriginalArrangeList");
                if (switchListMethod != null)
                {
                    var prefix = AccessTools.Method(typeof(MusicPlayerManualPatches), nameof(SwitchOriginalArrangeList_Prefix));
                    var postfix = AccessTools.Method(typeof(MusicPlayerManualPatches), nameof(SwitchOriginalArrangeList_Postfix));
                    harmony.Patch(switchListMethod, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[MusicPlayer] ExtraSoundListController.SwitchOriginalArrangeList not found");
                }

                MelonLogger.Msg("[MusicPlayer] Patches applied");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MusicPlayer] Error applying patches: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 1: State transitions
        // ─────────────────────────────────────────────────────────────────────────

        public static void ChangeState_Postfix(int state)
        {
            try
            {
                switch (state)
                {
                    case 1: // View — entering music player
                        MusicPlayerStateTracker.IsInMusicPlayer = true;
                        MusicPlayerStateTracker.SuppressContentChange = true;
                        MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.MUSIC_PLAYER);
                        CoroutineManager.StartManaged(AnnounceMusicPlayerEntry());
                        break;

                    case 2: // GotoTitle — leaving music player
                        MusicPlayerStateTracker.ClearState();
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MusicPlayer] Error in ChangeState patch: {ex.Message}");
            }
        }

        private static IEnumerator AnnounceMusicPlayerEntry()
        {
            yield return null;
            FFI_ScreenReaderMod.SpeakText(T("Music Player"), true);

            float elapsed = 0f;

            while (elapsed < 2f)
            {
                yield return null;
                elapsed += Time.deltaTime;

                try
                {
                    IntPtr focusedPtr = MusicPlayerStateTracker.CachedFocusedPtr;
                    if (focusedPtr != IntPtr.Zero &&
                        MusicPlayerReader.ReadContentFromPointer(focusedPtr, out string name, out int bgmId, out int idx, out int playTime))
                    {
                        string entry = MusicPlayerReader.ReadSongEntry(name, bgmId, idx, playTime);
                        if (!string.IsNullOrEmpty(entry))
                            FFI_ScreenReaderMod.SpeakText(entry, false);
                        MusicPlayerStateTracker.SuppressContentChange = false;
                        yield break;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[MusicPlayer] Error announcing entry song: {ex.Message}");
                    break;
                }
            }

            MusicPlayerStateTracker.SuppressContentChange = false;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 2: Song navigation
        // ─────────────────────────────────────────────────────────────────────────

        public static void SetFocus_Postfix(ExtraSoundListContentController __instance, bool isFocus)
        {
            try
            {
                if (!isFocus) return;
                if (!MusicPlayerStateTracker.IsInMusicPlayer) return;
                if (MusicPlayerStateTracker.SuppressContentChange)
                {
                    try
                    {
                        if (__instance != null)
                            MusicPlayerStateTracker.CachedFocusedPtr = __instance.Pointer;
                    }
                    catch { }
                    return;
                }

                IntPtr ptr;
                try
                {
                    if (__instance == null) return;
                    ptr = __instance.Pointer;
                }
                catch { return; }
                if (ptr == IntPtr.Zero) return;

                if (!MusicPlayerReader.ReadContentFromPointer(ptr, out string musicName, out int bgmId, out int index, out int playTime))
                    return;

                string entry = MusicPlayerReader.ReadSongEntry(musicName, bgmId, index, playTime);
                if (!string.IsNullOrEmpty(entry))
                {
                    AnnouncementDeduplicator.AnnounceIfNew(
                        AnnouncementContexts.MUSIC_LIST_ENTRY, entry);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MusicPlayer] Error in SetFocus patch: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 3: Play All toggle
        // ─────────────────────────────────────────────────────────────────────────

        public static void ChangeKeyHelpPlaybackIcon_Postfix(int key)
        {
            try
            {
                if (!MusicPlayerStateTracker.IsInMusicPlayer) return;

                string announcement = key == 0 ? T("Play All On") : T("Play All Off");
                FFI_ScreenReaderMod.SpeakText(announcement, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MusicPlayer] Error in PlaybackIcon patch: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 4: Arrangement toggle
        // ─────────────────────────────────────────────────────────────────────────

        public static void SwitchOriginalArrangeList_Prefix()
        {
            if (MusicPlayerStateTracker.IsInMusicPlayer)
                MusicPlayerStateTracker.SuppressContentChange = true;
        }

        public static unsafe void SwitchOriginalArrangeList_Postfix(ExtraSoundListController __instance)
        {
            try
            {
                if (!MusicPlayerStateTracker.IsInMusicPlayer) return;

                IntPtr instancePtr = __instance.Pointer;
                if (instancePtr == IntPtr.Zero) return;

                int listType = *(int*)((byte*)instancePtr.ToPointer() + MusicPlayerStateTracker.OFFSET_CURRENT_LIST_TYPE);
                string toggleLabel = listType == 1 ? T("Original") : T("Arrangement");
                FFI_ScreenReaderMod.SpeakText(toggleLabel, true);

                AnnouncementDeduplicator.Reset(AnnouncementContexts.MUSIC_LIST_ENTRY);
                IntPtr focusedPtr = MusicPlayerStateTracker.CachedFocusedPtr;
                if (focusedPtr != IntPtr.Zero &&
                    MusicPlayerReader.ReadContentFromPointer(focusedPtr, out string name, out int bgmId, out int idx, out int playTime))
                {
                    string entry = MusicPlayerReader.ReadSongEntry(name, bgmId, idx, playTime);
                    if (!string.IsNullOrEmpty(entry))
                        FFI_ScreenReaderMod.SpeakText(entry, false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MusicPlayer] Error in SwitchOriginalArrangeList patch: {ex.Message}");
            }
            finally
            {
                MusicPlayerStateTracker.SuppressContentChange = false;
            }
        }
    }
}
