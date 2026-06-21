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
using EquipmentCommandView = Il2CppLast.UI.KeyInput.EquipmentCommandView;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFI_ScreenReader.Patches
{
    /// <summary>
    /// Announces the focused command in the item / equipment / magic command bar, on OPEN and on
    /// NAVIGATION, with a "(n of N)" position. Each controller's per-frame <c>UpdateController</c> is
    /// patched and announces whenever the focused index CHANGES (open = first focus; nav = each move),
    /// reading the command identity from the ENUM/data (never on-screen Text) so it is immune to the
    /// pre-localization placeholder. The reader is gated to the bar's COMMAND phase (Item/Equip state 1,
    /// Magic state 4) and resets its last-index on leaving, so re-entry re-announces.
    ///
    /// The generic cursor reader is suppressed during command-bar phases (see IsCommandBarActive, called
    /// from CursorNavigation_Postfix) so it cannot double-announce these moves.
    /// </summary>
    public static class CommandBarPatches
    {
        private static bool isPatched = false;

        // Command-phase state-machine tags.
        private const int STATE_ITEM_COMMAND = 1;   // ItemWindowController command-select
        private const int STATE_EQUIP_COMMAND = 1;  // EquipmentWindowController command
        private const int STATE_MAGIC_COMMAND = 4;  // AbilityWindowController command (Use/Forget bar)

        // Last announced focused index per bar; -1 = nothing announced (re-announce on next change).
        private const int NO_INDEX = -1;
        private static int _lastItemIndex = NO_INDEX;
        private static int _lastEquipIndex = NO_INDEX;
        private static int _lastMagicIndex = NO_INDEX;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                // Per-frame UpdateController announces the focused command whenever its index changes.
                TryPatchPostfix(harmony, typeof(ItemCommandController), "UpdateController", Type.EmptyTypes, nameof(Item_UpdateController_Postfix));
                TryPatchPostfix(harmony, typeof(EquipmentCommandController), "UpdateController", Type.EmptyTypes, nameof(Equip_UpdateController_Postfix));
                // Magic command bar's UpdateController takes a bool (isUseCommand).
                TryPatchPostfix(harmony, typeof(AbilityCommandController), "UpdateController", new Type[] { typeof(bool) }, nameof(Magic_UpdateController_Postfix));

                // Window SetActive(false) resets the last-index (belt-and-suspenders re-announce on reopen).
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

        // ── Reset on window close ──
        public static void Item_SetActive_Postfix(bool __0) { try { if (!__0) _lastItemIndex = NO_INDEX; } catch { } }
        public static void Equip_SetActive_Postfix(bool __0) { try { if (!__0) _lastEquipIndex = NO_INDEX; } catch { } }

        // ── Item command bar (Use/Sort/Key Items) ──
        public static void Item_UpdateController_Postfix(object __instance)
        {
            try
            {
                IntPtr p = (__instance as ItemCommandController)?.Pointer ?? IntPtr.Zero;
                if (p == IntPtr.Zero)
                    return;

                // Only while the item window is in its command-select phase; otherwise re-arm.
                var win = UnityEngine.Object.FindObjectOfType<ItemWindowController>();
                if (win == null || StateMachineHelper.ReadState(win.Pointer, IL2CppOffsets.MenuStateMachine.Item) != STATE_ITEM_COMMAND)
                {
                    _lastItemIndex = NO_INDEX;
                    return;
                }

                IntPtr cursorPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.ItemSelectCursor);
                IntPtr listPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.ItemContentList);
                if (cursorPtr == IntPtr.Zero || listPtr == IntPtr.Zero)
                    return;
                int index = new GameCursor(cursorPtr).Index;
                var contents = new Il2CppSystem.Collections.Generic.List<ItemCommandContentView>(listPtr);
                if (index < 0 || index >= contents.Count)
                    return;
                if (index == _lastItemIndex)
                    return;
                var view = contents[index];
                if (view == null)
                    return;
                var data = view.Data;
                if (data == null)
                    return;   // focus not set yet — retry next frame (don't store index)
                string name = CommandBarReader.GetItemCommandName(data.Id);
                if (string.IsNullOrEmpty(name))
                    return;
                _lastItemIndex = index;
                CommandBarReader.Announce(name, index, contents.Count);
            }
            catch { }
        }

        // ── Equipment command bar (Equip/Optimal/Remove All) ──
        // Name from the focused-command cash (placeholder-proof, tracks the cursor); index/count from
        // the controller's own cursor + contents list.
        public static void Equip_UpdateController_Postfix(object __instance)
        {
            try
            {
                var ctrl = __instance as EquipmentCommandController;
                if (ctrl == null)
                    return;
                IntPtr p = ctrl.Pointer;
                if (p == IntPtr.Zero)
                    return;

                var win = UnityEngine.Object.FindObjectOfType<EquipmentWindowController>();
                if (win == null || StateMachineHelper.ReadState(win.Pointer, IL2CppOffsets.MenuStateMachine.Equip) != STATE_EQUIP_COMMAND)
                {
                    _lastEquipIndex = NO_INDEX;
                    return;
                }

                IntPtr cursorPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.EquipSelectCursor);
                IntPtr listPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.EquipContentList);
                if (cursorPtr == IntPtr.Zero || listPtr == IntPtr.Zero)
                    return;
                int index = new GameCursor(cursorPtr).Index;
                var contents = new Il2CppSystem.Collections.Generic.List<EquipmentCommandView>(listPtr);
                int count = contents.Count;
                if (index < 0 || index >= count)
                    return;
                if (index == _lastEquipIndex)
                    return;
                string name = CommandBarReader.GetEquipmentCommandName(ctrl.EquipmentCommandIdCash);
                if (string.IsNullOrEmpty(name))
                    return;
                _lastEquipIndex = index;
                CommandBarReader.Announce(name, index, count);
            }
            catch { }
        }

        // ── Magic command bar (Use/Forget) ──
        // AbilityWindowController.State.Command (4) is the Use/Forget bar. Announce on focus change.
        public static void Magic_UpdateController_Postfix(object __instance)
        {
            try
            {
                var ctrl = __instance as AbilityCommandController;
                if (ctrl == null || ctrl.gameObject == null || !ctrl.gameObject.activeInHierarchy)
                    return;

                var win = UnityEngine.Object.FindObjectOfType<AbilityWindowController>();
                if (win == null || StateMachineHelper.ReadState(win.Pointer, IL2CppOffsets.MenuStateMachine.Magic) != STATE_MAGIC_COMMAND)
                {
                    _lastMagicIndex = NO_INDEX;   // not in the command bar — re-arm for next entry
                    return;
                }

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
                if (index == _lastMagicIndex)
                    return;
                var view = contents[index];
                if (view == null)
                    return;
                var data = view.Data;
                if (data == null)
                    return;   // focus not set yet — retry next frame
                string name = CommandBarReader.GetAbilityCommandName(data.Id);
                if (string.IsNullOrEmpty(name))
                    return;
                _lastMagicIndex = index;
                CommandBarReader.Announce(name, index, contents.Count);
            }
            catch { }
        }

        /// <summary>
        /// True iff a command bar is in its command-select phase (Item=1, Equip=1, Magic=4). The generic
        /// cursor reader suppresses on this so it doesn't double the UpdateController announces. Called
        /// only after the cheaper list-phase ShouldSuppress checks, so it isn't hit on heavy scrolling.
        /// </summary>
        public static bool IsCommandBarActive()
        {
            try
            {
                var item = UnityEngine.Object.FindObjectOfType<ItemWindowController>();
                if (item != null && StateMachineHelper.ReadState(item.Pointer, IL2CppOffsets.MenuStateMachine.Item) == STATE_ITEM_COMMAND)
                    return true;
                var equip = UnityEngine.Object.FindObjectOfType<EquipmentWindowController>();
                if (equip != null && StateMachineHelper.ReadState(equip.Pointer, IL2CppOffsets.MenuStateMachine.Equip) == STATE_EQUIP_COMMAND)
                    return true;
                var magic = UnityEngine.Object.FindObjectOfType<AbilityWindowController>();
                if (magic != null && StateMachineHelper.ReadState(magic.Pointer, IL2CppOffsets.MenuStateMachine.Magic) == STATE_MAGIC_COMMAND)
                    return true;
            }
            catch { }
            return false;
        }
    }
}
