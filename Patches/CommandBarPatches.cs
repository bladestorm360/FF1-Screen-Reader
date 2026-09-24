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
    /// NAVIGATION, with a "(n of N)" position, whenever the focused index CHANGES (open = first focus;
    /// nav = each move), reading the command identity from the ENUM/data (never on-screen Text) so it is
    /// immune to the pre-localization placeholder. The reader is gated to the bar's COMMAND phase
    /// (Item/Equip state 1, Magic state 4) and its last-index is reset on command-state entry, so re-entry
    /// re-announces.
    ///
    /// Event-driven (round 2, 2026-09-24; replaces per-frame UpdateController postfixes). A read is
    /// scheduled by the bar's own cursor-set method — ItemCommandController.SetCommandSelectCursor
    /// (0x49AB00), EquipmentCommandController.SetCursor(int) (0x471C60), AbilityCommandController
    /// .SetCommandSelectCursor (0x3762E0); each is called from ResetCursor (open), the move callback and a
    /// click — and by the command-state entry (CommandSelectInit / CommandInit), which covers back-out from
    /// a deeper screen where the cursor does not move. The read runs one frame later (the per-frame
    /// UpdateFocus has refreshed the focus by then) with a short bounded retry while the focused data is
    /// not populated yet.
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

        // Frames a scheduled read may wait for the focused command's data to be populated.
        private const int READ_MAX_FRAMES = 10;

        // Read generations: a newer event for the same bar supersedes a pending read.
        private static int _itemReadGen;
        private static int _equipReadGen;
        private static int _magicReadGen;

        // Cached window controllers so the reads (and IsCommandBarActive) don't FindObjectOfType on every
        // event; re-found only when null/destroyed/INACTIVE. The active check
        // is essential: FindObjectOfType returns only ACTIVE objects, so a cached-but-inactive window (after
        // a back-out) must count as a miss — otherwise reading its stale state machine falsely reports a
        // command bar. (`cache == null` short-circuits before `.gameObject` for a destroyed object; mirrors
        // BattleCommandPatches.cachedTargetController.)
        private static ItemWindowController _itemWin;
        private static EquipmentWindowController _equipWin;
        private static AbilityWindowController _magicWin;

        private static ItemWindowController ItemWin()
        {
            if (_itemWin == null || _itemWin.gameObject == null || !_itemWin.gameObject.activeInHierarchy)
                _itemWin = UnityEngine.Object.FindObjectOfType<ItemWindowController>();
            return _itemWin;
        }
        private static EquipmentWindowController EquipWin()
        {
            if (_equipWin == null || _equipWin.gameObject == null || !_equipWin.gameObject.activeInHierarchy)
                _equipWin = UnityEngine.Object.FindObjectOfType<EquipmentWindowController>();
            return _equipWin;
        }
        private static AbilityWindowController MagicWin()
        {
            if (_magicWin == null || _magicWin.gameObject == null || !_magicWin.gameObject.activeInHierarchy)
                _magicWin = UnityEngine.Object.FindObjectOfType<AbilityWindowController>();
            return _magicWin;
        }

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                // Cursor-set events (open via ResetCursor, move callback, click) schedule a focus read.
                TryPatchPostfix(harmony, typeof(ItemCommandController), "SetCommandSelectCursor", Type.EmptyTypes, nameof(Item_CursorSet_Postfix));
                TryPatchPostfix(harmony, typeof(EquipmentCommandController), "SetCursor", new Type[] { typeof(int) }, nameof(Equip_CursorSet_Postfix));
                TryPatchPostfix(harmony, typeof(AbilityCommandController), "SetCommandSelectCursor", Type.EmptyTypes, nameof(Magic_CursorSet_Postfix));

                // Window SetActive(false) resets the last-index (belt-and-suspenders re-announce on reopen).
                TryPatchPostfix(harmony, typeof(ItemWindowController), "SetActive", new Type[] { typeof(bool) }, nameof(Item_SetActive_Postfix));
                TryPatchPostfix(harmony, typeof(EquipmentWindowController), "SetActive", new Type[] { typeof(bool) }, nameof(Equip_SetActive_Postfix));

                // Command-state ENTRY: the command-state Init fires on every entry into the command bar (from
                // char-select AND from a sub-list back-out, where the cursor does not move), so reset the
                // dedup there and schedule a read to guarantee a re-announce.
                // Magic = AbilityWindowController.CommandInit, Item = ItemWindowController.CommandSelectInit.
                TryPatchPostfix(harmony, typeof(AbilityWindowController), "CommandInit", Type.EmptyTypes, nameof(Magic_CommandInit_Postfix));
                TryPatchPostfix(harmony, typeof(ItemWindowController), "CommandSelectInit", Type.EmptyTypes, nameof(Item_CommandSelectInit_Postfix));
                TryPatchPostfix(harmony, typeof(EquipmentWindowController), "CommandInit", Type.EmptyTypes, nameof(Equip_CommandInit_Postfix));

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

        // ── Command-bar (re)entry ── reset the dedup and read, so the bar re-announces the focused command
        // every time it regains focus (returning from a sub-list, or re-entering from char-select).
        public static void Magic_CommandInit_Postfix(object __instance)
        {
            _lastMagicIndex = NO_INDEX;
            IntPtr cmd = ReadChild(__instance as AbilityWindowController, IL2CppOffsets.CommandBar.MagicWindowCommandController);
            if (cmd != IntPtr.Zero) ScheduleMagicRead(new AbilityCommandController(cmd));
        }

        public static void Item_CommandSelectInit_Postfix(object __instance)
        {
            _lastItemIndex = NO_INDEX;
            IntPtr cmd = ReadChild(__instance as ItemWindowController, IL2CppOffsets.CommandBar.ItemWindowCommandController);
            if (cmd != IntPtr.Zero) ScheduleItemRead(new ItemCommandController(cmd));
        }

        public static void Equip_CommandInit_Postfix(object __instance)
        {
            _lastEquipIndex = NO_INDEX;
            IntPtr cmd = ReadChild(__instance as EquipmentWindowController, IL2CppOffsets.CommandBar.EquipWindowCommandController);
            if (cmd != IntPtr.Zero) ScheduleEquipRead(new EquipmentCommandController(cmd));
        }

        private static IntPtr ReadChild(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase window, int offset)
        {
            try
            {
                if (window == null || window.Pointer == IntPtr.Zero) return IntPtr.Zero;
                return IL2CppFieldReader.ReadPointerSafe(window.Pointer, offset);
            }
            catch { return IntPtr.Zero; }
        }

        // ── Cursor-set events (open / move / click) ──
        public static void Item_CursorSet_Postfix(object __instance) => ScheduleItemRead(__instance as ItemCommandController);
        public static void Equip_CursorSet_Postfix(object __instance) => ScheduleEquipRead(__instance as EquipmentCommandController);
        public static void Magic_CursorSet_Postfix(object __instance) => ScheduleMagicRead(__instance as AbilityCommandController);

        private static void ScheduleItemRead(ItemCommandController ctrl)
        {
            if (ctrl == null) return;
            int gen = ++_itemReadGen;
            CoroutineManager.StartManaged(ReadRoutine(() => gen == _itemReadGen, () => TryReadItem(ctrl)));
        }

        private static void ScheduleEquipRead(EquipmentCommandController ctrl)
        {
            if (ctrl == null) return;
            int gen = ++_equipReadGen;
            CoroutineManager.StartManaged(ReadRoutine(() => gen == _equipReadGen, () => TryReadEquip(ctrl)));
        }

        private static void ScheduleMagicRead(AbilityCommandController ctrl)
        {
            if (ctrl == null) return;
            int gen = ++_magicReadGen;
            CoroutineManager.StartManaged(ReadRoutine(() => gen == _magicReadGen, () => TryReadMagic(ctrl)));
        }

        /// <summary>
        /// Runs a read one frame after the event, retrying (bounded) only while it reports "not ready".
        /// Stops early if a newer event for the same bar superseded it.
        /// </summary>
        private static System.Collections.IEnumerator ReadRoutine(Func<bool> stillCurrent, Func<bool> tryRead)
        {
            for (int i = 0; i < READ_MAX_FRAMES; i++)
            {
                yield return null;
                if (!stillCurrent()) yield break;
                bool done;
                try { done = tryRead(); }
                catch { done = true; }
                if (done) yield break;
            }
        }

        // ── Item command bar (Use/Sort/Key Items) ── returns false only to retry (focus data not set yet).
        private static bool TryReadItem(ItemCommandController ctrl)
        {
            IntPtr p = ctrl?.Pointer ?? IntPtr.Zero;
            if (p == IntPtr.Zero)
                return true;

            // Only while the item window is in its command-select phase.
            var win = ItemWin();
            if (win == null || StateMachineHelper.ReadState(win.Pointer, IL2CppOffsets.MenuStateMachine.Item) != STATE_ITEM_COMMAND)
            {
                _lastItemIndex = NO_INDEX;
                return true;
            }

            IntPtr cursorPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.ItemSelectCursor);
            IntPtr listPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.ItemContentList);
            if (cursorPtr == IntPtr.Zero || listPtr == IntPtr.Zero)
                return false;
            int index = new GameCursor(cursorPtr).Index;
            var contents = new Il2CppSystem.Collections.Generic.List<ItemCommandContentView>(listPtr);
            if (index < 0 || index >= contents.Count)
                return false;
            if (index == _lastItemIndex)
                return true;
            var view = contents[index];
            if (view == null)
                return false;
            var data = view.Data;
            if (data == null)
                return false;   // focus not set yet — retry (don't store index)
            string name = CommandBarReader.GetItemCommandName(data.Id);
            if (string.IsNullOrEmpty(name))
                return false;
            _lastItemIndex = index;
            CommandBarReader.Announce(name, index, contents.Count);
            return true;
        }

        // ── Equipment command bar (Equip/Optimal/Remove All) ──
        // Name from the focused-command cash (placeholder-proof, refreshed by the bar's per-frame
        // UpdateFocus, hence the one-frame-deferred read); index/count from its cursor + contents list.
        private static bool TryReadEquip(EquipmentCommandController ctrl)
        {
            if (ctrl == null)
                return true;
            IntPtr p = ctrl.Pointer;
            if (p == IntPtr.Zero)
                return true;

            var win = EquipWin();
            if (win == null || StateMachineHelper.ReadState(win.Pointer, IL2CppOffsets.MenuStateMachine.Equip) != STATE_EQUIP_COMMAND)
            {
                _lastEquipIndex = NO_INDEX;
                return true;
            }

            IntPtr cursorPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.EquipSelectCursor);
            IntPtr listPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.EquipContentList);
            if (cursorPtr == IntPtr.Zero || listPtr == IntPtr.Zero)
                return false;
            int index = new GameCursor(cursorPtr).Index;
            var contents = new Il2CppSystem.Collections.Generic.List<EquipmentCommandView>(listPtr);
            int count = contents.Count;
            if (index < 0 || index >= count)
                return false;
            if (index == _lastEquipIndex)
                return true;
            string name = CommandBarReader.GetEquipmentCommandName(ctrl.EquipmentCommandIdCash);
            if (string.IsNullOrEmpty(name))
                return false;
            _lastEquipIndex = index;
            CommandBarReader.Announce(name, index, count);
            return true;
        }

        // ── Magic command bar (Use/Forget) ──
        // AbilityWindowController.State.Command (4) is the Use/Forget bar.
        private static bool TryReadMagic(AbilityCommandController ctrl)
        {
            if (ctrl == null || ctrl.gameObject == null || !ctrl.gameObject.activeInHierarchy)
                return true;

            var win = MagicWin();
            if (win == null || StateMachineHelper.ReadState(win.Pointer, IL2CppOffsets.MenuStateMachine.Magic) != STATE_MAGIC_COMMAND)
            {
                _lastMagicIndex = NO_INDEX;   // not in the command bar — re-arm for next entry
                return true;
            }

            IntPtr p = ctrl.Pointer;
            if (p == IntPtr.Zero)
                return true;
            IntPtr cursorPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.MagicSelectCursor);
            IntPtr listPtr = Marshal.ReadIntPtr(p, IL2CppOffsets.CommandBar.MagicContentList);
            if (cursorPtr == IntPtr.Zero || listPtr == IntPtr.Zero)
                return false;
            int index = new GameCursor(cursorPtr).Index;
            var contents = new Il2CppSystem.Collections.Generic.List<AbilityCommandContentView>(listPtr);
            if (index < 0 || index >= contents.Count)
                return false;
            if (index == _lastMagicIndex)
                return true;
            var view = contents[index];
            if (view == null)
                return false;
            var data = view.Data;
            if (data == null)
                return false;   // focus not set yet — retry
            string name = CommandBarReader.GetAbilityCommandName(data.Id);
            if (string.IsNullOrEmpty(name))
                return false;
            _lastMagicIndex = index;
            CommandBarReader.Announce(name, index, contents.Count);
            return true;
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
                var item = ItemWin();
                if (item != null && StateMachineHelper.ReadState(item.Pointer, IL2CppOffsets.MenuStateMachine.Item) == STATE_ITEM_COMMAND)
                    return true;
                var equip = EquipWin();
                if (equip != null && StateMachineHelper.ReadState(equip.Pointer, IL2CppOffsets.MenuStateMachine.Equip) == STATE_EQUIP_COMMAND)
                    return true;
                var magic = MagicWin();
                if (magic != null && StateMachineHelper.ReadState(magic.Pointer, IL2CppOffsets.MenuStateMachine.Magic) == STATE_MAGIC_COMMAND)
                    return true;
            }
            catch { }
            return false;
        }
    }
}
