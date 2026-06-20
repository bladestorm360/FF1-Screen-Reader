using System;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using FFI_ScreenReader.Menus;
using FFI_ScreenReader.Utils;

using ItemCommandController = Il2CppLast.UI.KeyInput.ItemCommandController;
using EquipmentCommandController = Il2CppLast.UI.KeyInput.EquipmentCommandController;
using AbilityCommandController = Il2CppSerial.FF1.UI.KeyInput.AbilityCommandController;
using AbilityCommandContentView = Il2CppSerial.FF1.UI.KeyInput.AbilityCommandContentView;
using AbilityWindowController = Il2CppSerial.FF1.UI.KeyInput.AbilityWindowController;
using ItemWindowController = Il2CppLast.UI.KeyInput.ItemWindowController;
using EquipmentWindowController = Il2CppLast.UI.KeyInput.EquipmentWindowController;
using ItemCommandContentView = Il2CppLast.UI.KeyInput.ItemCommandContentView;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Announces the INITIALLY-focused command when the item / equipment / magic command bar opens.
    /// Each controller's per-frame <c>UpdateController</c> is patched but only reads once per open, then
    /// disarms — so it polls harmlessly until the focused command's ID is set, then announces once.
    /// The command identity is read from the controller's ENUM/data (never on-screen Text), so it is
    /// immune to the pre-localization placeholder. Navigation keeps flowing through the generic cursor
    /// reader; this fires only on open.
    ///
    /// Item/equip arm on their window SetActive. Magic is different: the Use/Forget bar appears only
    /// AFTER a character-select phase, so it self-validates the AbilityWindowController state machine
    /// (read only while in the Command state) instead of arming on a state-transition that fires too
    /// early. (The FIELD command bar was removed — MainMenuController.focusId is the SELECTED command,
    /// not the focused one, so it announced the wrong thing on selection; field initial-focus is still
    /// an open problem, tracked separately.)
    /// </summary>
    public static class CommandBarPatches
    {
        private static bool isPatched = false;

        private static bool _itemArmed = false;
        private static bool _equipArmed = false;

        // Armed when a command bar (re)opens; cleared on leave / a successful read.
        public static void ArmItem() => _itemArmed = true;
        public static void ArmEquip() => _equipArmed = true;
        public static void ClearItem() => _itemArmed = false;
        public static void ClearEquip() => _equipArmed = false;
        public static void ClearAll() { _itemArmed = false; _equipArmed = false; _magicCmdOpenRead = false; }

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                // Read-on-open: per-frame UpdateController announces the focused command once while armed.
                TryPatchPostfix(harmony, typeof(ItemCommandController), "UpdateController", Type.EmptyTypes, nameof(Item_UpdateController_Postfix));
                TryPatchPostfix(harmony, typeof(EquipmentCommandController), "UpdateController", Type.EmptyTypes, nameof(Equip_UpdateController_Postfix));
                // Magic command bar's UpdateController takes a bool (isUseCommand).
                TryPatchPostfix(harmony, typeof(AbilityCommandController), "UpdateController", new Type[] { typeof(bool) }, nameof(Magic_UpdateController_Postfix));

                // Arm/clear triggers — item/equip on their window SetActive.
                TryPatchPostfix(harmony, typeof(ItemWindowController), "SetActive", new Type[] { typeof(bool) }, nameof(Item_SetActive_Postfix));
                TryPatchPostfix(harmony, typeof(EquipmentWindowController), "SetActive", new Type[] { typeof(bool) }, nameof(Equip_SetActive_Postfix));

                isPatched = true;
                MelonLogger.Msg("[CommandBar] Command-bar patches applied");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[CommandBar] Error applying patches: {ex.Message}");
            }
        }

        private static void TryPatchPostfix(HarmonyLib.Harmony harmony, Type type, string method, Type[] args, string postfixName)
        {
            try
            {
                var target = AccessTools.Method(type, method, args);
                if (target != null)
                    harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(CommandBarPatches), postfixName)));
                else
                    MelonLogger.Warning($"[CommandBar] {type.Name}.{method} not found");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[CommandBar] Error patching {type.Name}.{method}: {ex.Message}");
            }
        }

        // ── Arm/clear triggers ──
        public static void Item_SetActive_Postfix(bool __0) { try { if (__0) ArmItem(); else ClearItem(); } catch { } }
        public static void Equip_SetActive_Postfix(bool __0) { try { if (__0) ArmEquip(); else ClearEquip(); } catch { } }

        // ── Read-on-open ──

        // Item command bar: no cash field — selectCursor.Index → contentList[index].Data.Id.
        public static void Item_UpdateController_Postfix(object __instance)
        {
            if (!_itemArmed)
                return;
            try
            {
                IntPtr p = (__instance as ItemCommandController)?.Pointer ?? IntPtr.Zero;
                if (p == IntPtr.Zero)
                    return;
                IntPtr cursorPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.ItemSelectCursor);
                IntPtr listPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.ItemContentList);
                if (cursorPtr == IntPtr.Zero || listPtr == IntPtr.Zero)
                    return;
                int index = new GameCursor(cursorPtr).Index;
                var contents = new Il2CppSystem.Collections.Generic.List<ItemCommandContentView>(listPtr);
                if (index < 0 || index >= contents.Count)
                    return;
                var view = contents[index];
                if (view == null)
                    return;
                var data = view.Data;
                if (data == null)
                    return;
                string name = CommandBarReader.GetItemCommandName(data.Id);
                if (string.IsNullOrEmpty(name))
                    return;
                _itemArmed = false;
                CommandBarReader.Announce(name);
            }
            catch { }
        }

        // Equipment command bar: read the public EquipmentCommandIdCash property directly.
        public static void Equip_UpdateController_Postfix(object __instance)
        {
            if (!_equipArmed)
                return;
            try
            {
                var ctrl = __instance as EquipmentCommandController;
                if (ctrl == null)
                    return;
                string name = CommandBarReader.GetEquipmentCommandName(ctrl.EquipmentCommandIdCash);
                if (string.IsNullOrEmpty(name))
                    return;
                _equipArmed = false;
                CommandBarReader.Announce(name);
            }
            catch { }
        }

        // ── Magic command bar (Use/Forget) ──
        // AbilityWindowController.State.Command (4) is the Use/Forget bar; earlier phases (character
        // select, spell lists) are other states. We announce once per Command-state entry, reading the
        // live state each frame (not a state-transition arm, which fires before the bar is ready).
        private const int STATE_MAGIC_COMMAND = 4;
        private static bool _magicCmdOpenRead = false;

        public static void Magic_UpdateController_Postfix(object __instance)
        {
            try
            {
                var ctrl = __instance as AbilityCommandController;
                if (ctrl == null || ctrl.gameObject == null || !ctrl.gameObject.activeInHierarchy)
                    return;

                var win = UnityEngine.Object.FindObjectOfType<AbilityWindowController>();
                if (win == null)
                {
                    _magicCmdOpenRead = false;
                    return;
                }
                int state = StateMachineHelper.ReadState(win.Pointer, IL2CppOffsets.MenuStateMachine.Magic);
                if (state != STATE_MAGIC_COMMAND)
                {
                    _magicCmdOpenRead = false;   // left the command bar — re-arm for next entry
                    return;
                }
                if (_magicCmdOpenRead)
                    return;                      // already announced this entry

                // Read the FOCUSED command from the cursor (like the item bar) — CommandData is a cached
                // property that's null/stale on open, so it never announced.
                IntPtr p = ctrl.Pointer;
                if (p == IntPtr.Zero)
                    return;
                IntPtr cursorPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.MagicSelectCursor);
                IntPtr listPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.MagicContentList);
                if (cursorPtr == IntPtr.Zero || listPtr == IntPtr.Zero)
                    return;
                int index = new GameCursor(cursorPtr).Index;
                var contents = new Il2CppSystem.Collections.Generic.List<AbilityCommandContentView>(listPtr);
                if (index < 0 || index >= contents.Count)
                    return;
                var view = contents[index];
                if (view == null)
                    return;
                var data = view.Data;
                if (data == null)
                    return;                      // focus not set yet — retry next frame
                string name = CommandBarReader.GetAbilityCommandName(data.Id);
                if (string.IsNullOrEmpty(name))
                    return;
                _magicCmdOpenRead = true;
                CommandBarReader.Announce(name);
            }
            catch { }
        }
    }
}
