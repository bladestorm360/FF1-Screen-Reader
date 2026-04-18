using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Core;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

using ShopMagicTargetSelectController = Il2CppLast.UI.KeyInput.ShopMagicTargetSelectController;
using GameCursor = Il2CppLast.UI.Cursor;
using MasterManager = Il2CppLast.Data.Master.MasterManager;
using Content = Il2CppLast.Data.Master.Content;
using MessageManager = Il2CppLast.Management.MessageManager;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Patches for magic shop spell slot selection.
    /// Handles character selection, spell slot navigation, and "no learners" detection.
    /// </summary>
    public static class ShopMagicPatches
    {
        private const int OFFSET_SLOT_CHARACTER_ID = IL2CppOffsets.ShopMagic.CharacterId;
        private const int OFFSET_SLOT_TYPE = IL2CppOffsets.ShopMagic.SlotType;
        private const int OFFSET_SLOT_VIEW = IL2CppOffsets.ShopMagic.View;
        private const int OFFSET_IS_FOUND_EQUIP_SLOT = IL2CppOffsets.ShopMagic.IsFoundEquipSlot;
        private const int OFFSET_SLOT_CONTENT_ID = IL2CppOffsets.ShopMagic.ContentId;

        // Track last character to only announce name when switching characters
        private static int lastAnnouncedCharacterId = -1;

        // Track if we already announced "no characters can learn" to prevent duplicates
        private static bool announcedNoLearnersThisSession = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(ShopMagicTargetSelectController);

                // Patch SelectContent(Cursor, List<ShopGetMagicContentController>) - called during navigation
                MethodInfo selectContentMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "SelectContent")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 2 && parameters[0].ParameterType.Name == "Cursor")
                        {
                            selectContentMethod = method;
                            break;
                        }
                    }
                }

                if (selectContentMethod != null)
                {
                    harmony.Patch(selectContentMethod,
                        postfix: new HarmonyMethod(typeof(ShopMagicPatches), nameof(MagicSelectContent_Postfix)));
                }
                else
                {
                    MelonLogger.Warning("[Shop] Could not find ShopMagicTargetSelectController.SelectContent");
                }

                // Also patch SetFocus to announce initial slot when menu opens
                var setFocusMethod = controllerType.GetMethod("SetFocus", new Type[] { typeof(bool) });
                if (setFocusMethod != null)
                {
                    harmony.Patch(setFocusMethod,
                        postfix: new HarmonyMethod(typeof(ShopMagicPatches), nameof(MagicTargetSetFocus_Postfix)));
                }

                // Patch Show() to detect when no characters can learn the spell
                MethodInfo showMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "Show")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 5)
                        {
                            showMethod = method;
                            break;
                        }
                    }
                }

                if (showMethod != null)
                {
                    harmony.Patch(showMethod,
                        postfix: new HarmonyMethod(typeof(ShopMagicPatches), nameof(MagicTargetShow_Postfix)));
                }
                else
                {
                    MelonLogger.Warning("[Shop] Could not find ShopMagicTargetSelectController.Show with 5 params");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch magic target: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when SetFocus changes on magic target select.
        /// Resets tracking when menu opens/closes.
        /// </summary>
        public static void MagicTargetSetFocus_Postfix(ShopMagicTargetSelectController __instance, bool isFocus)
        {
            try
            {
                if (!isFocus)
                {
                    AnnouncementDeduplicator.Reset(AnnouncementContexts.SHOP_SLOT);
                    return;
                }

                ShopMenuTracker.IsShopMenuActive = true;
                ShopMenuTracker.IsInMagicSlotSelection = true;
                AnnouncementDeduplicator.Reset(AnnouncementContexts.SHOP_SLOT);
                lastAnnouncedCharacterId = -1;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in MagicTargetSetFocus_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when the magic shop spell selection grid opens (Show method).
        /// Checks isFoundEquipSlot to detect when no characters can learn the spell.
        /// </summary>
        public static void MagicTargetShow_Postfix(ShopMagicTargetSelectController __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                ShopMenuTracker.IsShopMenuActive = true;
                ShopMenuTracker.IsInMagicSlotSelection = true;

                AnnouncementDeduplicator.Reset(AnnouncementContexts.SHOP_SLOT);
                lastAnnouncedCharacterId = -1;
                announcedNoLearnersThisSession = false;

                IntPtr controllerPtr = __instance.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return;

                bool isFoundEquipSlot = IL2CppFieldReader.ReadBool(controllerPtr, OFFSET_IS_FOUND_EQUIP_SLOT);

                if (!isFoundEquipSlot && !announcedNoLearnersThisSession)
                {
                    announcedNoLearnersThisSession = true;
                    FFI_ScreenReaderMod.SpeakText(T("No characters can learn this spell"), interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in MagicTargetShow_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when navigating between spell slots in magic shop.
        /// Only announces character name when switching to a different character.
        /// </summary>
        public static void MagicSelectContent_Postfix(
            ShopMagicTargetSelectController __instance,
            GameCursor targetCursor,
            Il2CppSystem.Collections.Generic.List<Il2CppLast.UI.KeyInput.ShopGetMagicContentController> contentList)
        {
            try
            {
                ShopMenuTracker.IsShopMenuActive = true;
                ShopMenuTracker.IsInMagicSlotSelection = true;

                if (__instance == null || targetCursor == null || contentList == null)
                    return;

                int cursorIndex = targetCursor.Index;

                if (cursorIndex < 0 || cursorIndex >= contentList.Count)
                    return;

                var slotController = contentList[cursorIndex];
                if (slotController == null)
                    return;

                IntPtr slotPtr = slotController.Pointer;
                if (slotPtr == IntPtr.Zero)
                    return;

                int characterId = IL2CppFieldReader.ReadInt32(slotPtr, OFFSET_SLOT_CHARACTER_ID);
                int slotType = IL2CppFieldReader.ReadInt32(slotPtr, OFFSET_SLOT_TYPE);

                string spellName = GetSpellSlotName(slotPtr);
                if (string.IsNullOrEmpty(spellName))
                {
                    spellName = T("Empty");
                }

                string announcement;
                bool characterChanged = (characterId != lastAnnouncedCharacterId);

                if (characterChanged)
                {
                    string characterName = GetCharacterNameById(characterId);
                    if (string.IsNullOrEmpty(characterName))
                    {
                        characterName = string.Format(T("Character {0}"), characterId);
                    }

                    announcement = string.Format(T("{0}: Slot {1}: {2}"), characterName, slotType, spellName);
                    lastAnnouncedCharacterId = characterId;
                }
                else
                {
                    announcement = string.Format(T("Slot {0}: {1}"), slotType, spellName);
                }

                AnnouncementHelper.AnnounceIfNew(AnnouncementContexts.SHOP_SLOT, announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in MagicSelectContent_Postfix: {ex.Message}");
            }
        }

        private static string GetCharacterNameById(int characterId)
        {
            try
            {
                var userDataManager = Il2CppLast.Management.UserDataManager.Instance();
                if (userDataManager == null)
                    return null;

                var partyList = userDataManager.GetOwnedCharactersClone(false);
                if (partyList == null)
                    return null;

                foreach (var charData in partyList)
                {
                    if (charData != null && charData.Id == characterId)
                    {
                        return charData.Name;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Shop Magic] Error getting character name: {ex.Message}");
            }
            return null;
        }

        private static string GetSpellSlotName(IntPtr slotControllerPtr)
        {
            try
            {
                int contentId = IL2CppFieldReader.ReadInt32(slotControllerPtr, OFFSET_SLOT_CONTENT_ID);

                if (contentId == 0)
                    return null;

                var masterManager = MasterManager.Instance;
                if (masterManager == null)
                    return null;

                var content = masterManager.GetData<Content>(contentId);
                if (content == null)
                    return null;

                string mesIdName = content.MesIdName;
                if (string.IsNullOrEmpty(mesIdName))
                    return null;

                var messageManager = MessageManager.Instance;
                if (messageManager == null)
                    return null;

                string abilityName = messageManager.GetMessage(mesIdName, false);
                if (!string.IsNullOrEmpty(abilityName))
                {
                    return TextUtils.StripIconMarkup(abilityName);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Shop Magic] Error getting spell name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Resets magic shop tracking state.
        /// Called from ShopPatches.ResetQuantityTracking().
        /// </summary>
        public static void ResetState()
        {
            lastAnnouncedCharacterId = -1;
            announcedNoLearnersThisSession = false;
        }
    }
}
