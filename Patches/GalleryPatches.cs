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
    /// Tracks gallery scene state.
    /// Mirrors MusicPlayerStateTracker pattern.
    /// </summary>
    public static class GalleryStateTracker
    {
        public static bool IsInGallery { get; set; } = false;
        public static bool SuppressContentChange { get; set; } = false;
        public static IntPtr CachedFocusedPtr { get; set; } = IntPtr.Zero;
        public static int PreviousState { get; set; } = 0;

        public static void ClearState()
        {
            IsInGallery = false;
            SuppressContentChange = false;
            CachedFocusedPtr = IntPtr.Zero;
            PreviousState = 0;
            MenuStateRegistry.Reset(MenuStateRegistry.GALLERY);
        }
    }

    /// <summary>
    /// Patches for the Gallery (Extra Gallery) extras menu.
    /// Uses manual Harmony patching to avoid silent failures from attribute-based patches.
    /// </summary>
    internal static class GalleryManualPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch 1: SubSceneManagerExtraGallery.ChangeState
                var changeStateMethod = AccessTools.Method(
                    typeof(SubSceneManagerExtraGallery), "ChangeState");
                if (changeStateMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(GalleryManualPatches), nameof(ChangeState_Postfix));
                    harmony.Patch(changeStateMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Gallery] SubSceneManagerExtraGallery.ChangeState not found");
                }

                // Patch 2: GalleryTopListController.SetFocusContent
                var setFocusMethod = AccessTools.Method(
                    typeof(GalleryTopListController), "SetFocusContent");
                if (setFocusMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(GalleryManualPatches), nameof(SetFocusContent_Postfix));
                    harmony.Patch(setFocusMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Gallery] GalleryTopListController.SetFocusContent not found");
                }

                MelonLogger.Msg("[Gallery] Patches applied");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Gallery] Error applying patches: {ex.Message}");
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
                    case 1: // View
                        if (GalleryStateTracker.PreviousState == 0) // First entry from Init
                        {
                            GalleryStateTracker.IsInGallery = true;
                            GalleryStateTracker.SuppressContentChange = true;
                            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.GALLERY);
                            CoroutineManager.StartManaged(AnnounceGalleryEntry());
                        }
                        GalleryStateTracker.PreviousState = 1;
                        break;

                    case 2: // Details — image opened
                        FFI_ScreenReaderMod.SpeakText(T("Image open"), true);
                        GalleryStateTracker.PreviousState = 2;
                        break;

                    case 3: // GotoTitle — leaving gallery
                        GalleryStateTracker.ClearState();
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Gallery] Error in ChangeState patch: {ex.Message}");
            }
        }

        // Entry announce state (round 2, 2026-09-24 — replaces a 2-second poll). "Gallery" is spoken one
        // frame after entry; the focused entry follows as soon as SetFocusContent has reported it — read
        // here if it already has, otherwise by the SetFocusContent postfix when it does.
        private static bool _entryHeaderSpoken;

        private static IEnumerator AnnounceGalleryEntry()
        {
            _entryHeaderSpoken = false;
            yield return null;
            FFI_ScreenReaderMod.SpeakText(T("Gallery"), true);
            _entryHeaderSpoken = true;
            TryAnnounceEntryItem();
        }

        /// <summary>
        /// Speaks the cached focused entry after the "Gallery" header, then ends the entry suppression so
        /// navigation announces normally. Returns false (suppression kept) until an entry can be read.
        /// </summary>
        private static bool TryAnnounceEntryItem()
        {
            try
            {
                if (!GalleryStateTracker.SuppressContentChange || !_entryHeaderSpoken) return false;
                IntPtr focusedPtr = GalleryStateTracker.CachedFocusedPtr;
                if (focusedPtr == IntPtr.Zero ||
                    !GalleryReader.ReadContentFromPointer(focusedPtr, out int number, out string name))
                    return false;

                GalleryStateTracker.SuppressContentChange = false;
                string entry = GalleryReader.ReadListEntry(number, name);
                if (!string.IsNullOrEmpty(entry))
                    FFI_ScreenReaderMod.SpeakText(entry, false);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Gallery] Error announcing entry item: {ex.Message}");
                GalleryStateTracker.SuppressContentChange = false;
                return true;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 2: List navigation
        // ─────────────────────────────────────────────────────────────────────────

        public static void SetFocusContent_Postfix(GalleryTopListController __instance, bool isFocus)
        {
            try
            {
                if (!isFocus) return;
                if (!GalleryStateTracker.IsInGallery) return;

                IntPtr ptr;
                try
                {
                    if (__instance == null) return;
                    ptr = __instance.Pointer;
                }
                catch { return; }
                if (ptr == IntPtr.Zero) return;

                if (GalleryStateTracker.SuppressContentChange)
                {
                    // Entry: cache the focus; once "Gallery" has been spoken, this read ends the entry.
                    GalleryStateTracker.CachedFocusedPtr = ptr;
                    TryAnnounceEntryItem();
                    return;
                }

                if (!GalleryReader.ReadContentFromPointer(ptr, out int number, out string name))
                    return;

                string entry = GalleryReader.ReadListEntry(number, name);
                if (!string.IsNullOrEmpty(entry))
                {
                    FFI_ScreenReaderMod.SpeakText(entry, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Gallery] Error in SetFocusContent patch: {ex.Message}");
            }
        }
    }
}
