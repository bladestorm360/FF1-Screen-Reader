using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.EventSystems;
using MelonLoader;
using FFI_ScreenReader.Core.Handlers;
using FFI_ScreenReader.Utils;
using FFI_ScreenReader.Patches;
using FFI_ScreenReader.Menus;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Core
{
    /// <summary>
    /// Manages all input handling for the screen reader mod.
    /// Keyboard: SDL scancodes (replaces Unity Input.GetKeyDown).
    /// Controller: delegated to ControllerRouter.
    /// </summary>
    public class InputManager
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private static IntPtr gameWindowHandle = IntPtr.Zero;

        private readonly FFI_ScreenReaderMod mod;
        private readonly KeyBindingRegistry registry = new KeyBindingRegistry();

        public InputManager(FFI_ScreenReaderMod mod)
        {
            this.mod = mod;
            InitializeBindings();
        }

        /// <summary>
        /// Registers a field-only binding with a "Not available in battle" fallback for the Battle context.
        /// </summary>
        private void RegisterFieldWithBattleFeedback(KeyCode key, KeyModifier modifier, Action action, string description)
        {
            registry.Register(key, modifier, KeyContext.Field, action, description);
            registry.Register(key, modifier, KeyContext.Battle, NotAvailableInBattle, description + " (battle blocked)");
        }

        private static void NotAvailableInBattle()
        {
            FFI_ScreenReaderMod.SpeakText(T("Not available in battle"), interrupt: true);
        }

        private void InitializeBindings()
        {
            // --- Status screen: arrow key navigation ---
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.Status, StatusNavigationReader.JumpToBottom, "Jump to bottom stat");
            registry.Register(KeyCode.DownArrow, KeyModifier.Shift, KeyContext.Status, StatusNavigationReader.JumpToNextGroup, "Jump to next stat group");
            registry.Register(KeyCode.DownArrow, KeyModifier.None, KeyContext.Status, StatusNavigationReader.NavigateNext, "Next stat");
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.Status, StatusNavigationReader.JumpToTop, "Jump to top stat");
            registry.Register(KeyCode.UpArrow, KeyModifier.Shift, KeyContext.Status, StatusNavigationReader.JumpToPreviousGroup, "Jump to previous stat group");
            registry.Register(KeyCode.UpArrow, KeyModifier.None, KeyContext.Status, StatusNavigationReader.NavigatePrevious, "Previous stat");
            registry.Register(KeyCode.R, KeyContext.Status, StatusNavigationReader.ReadCurrentStat, "Read current stat");

            // --- Bestiary detail: arrow key navigation ---
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToBottom, "Jump to bottom stat (bestiary)");
            registry.Register(KeyCode.DownArrow, KeyModifier.Shift, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToNextGroup, "Jump to next group (bestiary)");
            registry.Register(KeyCode.DownArrow, KeyModifier.None, KeyContext.BestiaryDetail, BestiaryNavigationReader.NavigateNext, "Next stat (bestiary)");
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToTop, "Jump to top stat (bestiary)");
            registry.Register(KeyCode.UpArrow, KeyModifier.Shift, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToPreviousGroup, "Jump to previous group (bestiary)");
            registry.Register(KeyCode.UpArrow, KeyModifier.None, KeyContext.BestiaryDetail, BestiaryNavigationReader.NavigatePrevious, "Previous stat (bestiary)");
            registry.Register(KeyCode.R, KeyContext.BestiaryDetail, BestiaryNavigationReader.ReadCurrentStat, "Read current stat (bestiary)");

            // --- Field: entity navigation (brackets + backslash) — with battle feedback ---
            RegisterFieldWithBattleFeedback(KeyCode.LeftBracket, KeyModifier.Shift, mod.CyclePreviousCategory, "Previous entity category");
            RegisterFieldWithBattleFeedback(KeyCode.LeftBracket, KeyModifier.None, mod.CyclePrevious, "Previous entity");
            RegisterFieldWithBattleFeedback(KeyCode.RightBracket, KeyModifier.Shift, mod.CycleNextCategory, "Next entity category");
            RegisterFieldWithBattleFeedback(KeyCode.RightBracket, KeyModifier.None, mod.CycleNext, "Next entity");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.Ctrl, mod.ToggleToLayerFilter, "Toggle layer filter");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.Shift, mod.TogglePathfindingFilter, "Toggle pathfinding filter");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.None, mod.AnnounceCurrentEntity, "Announce current entity");

            // --- Field: pathfinding alternate keys (J/K/L/P) — with battle feedback ---
            RegisterFieldWithBattleFeedback(KeyCode.J, KeyModifier.Shift, mod.CyclePreviousCategory, "Previous entity category (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.J, KeyModifier.None, mod.CyclePrevious, "Previous entity (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.K, KeyModifier.None, mod.AnnounceEntityOnly, "Announce entity name (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.L, KeyModifier.Shift, mod.CycleNextCategory, "Next entity category (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.L, KeyModifier.None, mod.CycleNext, "Next entity (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.P, KeyModifier.Shift, mod.TogglePathfindingFilter, "Toggle pathfinding filter (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.P, KeyModifier.None, mod.AnnounceCurrentEntity, "Announce current entity (alt)");

            // --- Field: waypoint keys ---
            registry.Register(KeyCode.Comma, KeyModifier.Shift, KeyContext.Field, () => WaypointHandler.CyclePreviousCategory(), "Previous waypoint category");
            registry.Register(KeyCode.Comma, KeyModifier.None, KeyContext.Field, () => WaypointHandler.CyclePrevious(), "Previous waypoint");
            registry.Register(KeyCode.Period, KeyModifier.Ctrl, KeyContext.Field, () => WaypointHandler.RenameCurrentWaypoint(), "Rename waypoint");
            registry.Register(KeyCode.Period, KeyModifier.Shift, KeyContext.Field, () => WaypointHandler.CycleNextCategory(), "Next waypoint category");
            registry.Register(KeyCode.Period, KeyModifier.None, KeyContext.Field, () => WaypointHandler.CycleNext(), "Next waypoint");
            registry.Register(KeyCode.Slash, KeyModifier.CtrlShift, KeyContext.Field, () => WaypointHandler.ClearAllWaypointsForMap(), "Clear all waypoints for map");
            registry.Register(KeyCode.Slash, KeyModifier.Ctrl, KeyContext.Field, () => WaypointHandler.RemoveCurrentWaypoint(), "Remove current waypoint");
            registry.Register(KeyCode.Slash, KeyModifier.Shift, KeyContext.Field, () => WaypointHandler.AddNewWaypointWithNaming(), "Add waypoint with name");
            registry.Register(KeyCode.Slash, KeyModifier.None, KeyContext.Field, () => WaypointHandler.PathfindToCurrentWaypoint(), "Pathfind to waypoint");

            // --- Field: teleport (Ctrl+Arrow) ---
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.Field, () => mod.TeleportInDirection(new Vector2(0, 16)), "Teleport north");
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.Field, () => mod.TeleportInDirection(new Vector2(0, -16)), "Teleport south");
            registry.Register(KeyCode.LeftArrow, KeyModifier.Ctrl, KeyContext.Field, () => mod.TeleportInDirection(new Vector2(-16, 0)), "Teleport west");
            registry.Register(KeyCode.RightArrow, KeyModifier.Ctrl, KeyContext.Field, () => mod.TeleportInDirection(new Vector2(16, 0)), "Teleport east");

            // --- Global: info/announcements ---
            registry.Register(KeyCode.G, KeyContext.Global, mod.AnnounceGilAmount, "Announce Gil");
            registry.Register(KeyCode.H, KeyContext.Global, mod.AnnounceCharacterStatus, "Announce character status");
            registry.Register(KeyCode.M, KeyModifier.Shift, KeyContext.Global, mod.ToggleMapExitFilter, "Toggle map exit filter");
            registry.Register(KeyCode.M, KeyModifier.None, KeyContext.Global, mod.AnnounceCurrentMap, "Announce current map");
            registry.Register(KeyCode.Tab, KeyContext.Global, HandleTabKey, "Clear battle state fallback");

            // --- Global: V key (movement state) ---
            registry.Register(KeyCode.V, KeyContext.Global, () => GlobalHotkeyHandler.AnnounceCurrentVehicle(), "Announce vehicle state");

            // --- Global: Shift+I key (context-aware controls) ---
            registry.Register(KeyCode.I, KeyModifier.Shift, KeyContext.Global, ControllerRouter.AnnounceContextControls, "Announce controls");

            // --- Global: I key (cascading menu priority) ---
            registry.Register(KeyCode.I, KeyContext.Global, () => GlobalHotkeyHandler.HandleItemDetailsKey(mod), "Item details");

            // --- Field-only toggles (blocked in battle with feedback) ---
            RegisterFieldWithBattleFeedback(KeyCode.Quote, KeyModifier.None, mod.ToggleFootsteps, "Toggle footsteps");
            RegisterFieldWithBattleFeedback(KeyCode.Semicolon, KeyModifier.None, mod.ToggleWallTones, "Toggle wall tones");
            RegisterFieldWithBattleFeedback(KeyCode.Alpha9, KeyModifier.None, mod.ToggleAudioBeacons, "Toggle audio beacons");

            // --- Field-only category shortcuts ---
            RegisterFieldWithBattleFeedback(KeyCode.K, KeyModifier.Shift, mod.ResetToAllCategory, "Reset to All category");
            RegisterFieldWithBattleFeedback(KeyCode.Equals, KeyModifier.None, mod.CycleNextCategory, "Next entity category (global)");
            RegisterFieldWithBattleFeedback(KeyCode.Minus, KeyModifier.None, mod.CyclePreviousCategory, "Previous entity category (global)");

            // Sort for correct modifier precedence
            registry.FinalizeRegistration();
        }

        public void Update()
        {
            // Skip all input when game window is not focused
            if (gameWindowHandle == IntPtr.Zero)
            {
                try { gameWindowHandle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; }
                catch { } // Process handle may not be available yet
            }
            if (gameWindowHandle != IntPtr.Zero && GetForegroundWindow() != gameWindowHandle)
                return;

            // Poll SDL gamepad state every frame
            GamepadManager.Update();

            // Suppress Unity legacy Input when mod is consuming.
            // Safe because mod reads keyboard via GetAsyncKeyState (unaffected by ResetInputAxes).
            // This + InputSystemManager patches = complete game keyboard suppression.
            if (ControllerRouter.SuppressGameInput)
                Input.ResetInputAxes();

            // Controller routing + context state runs FIRST every frame.
            // ControllerRouter.Update computes IsFieldActive (used by audio, passthrough, etc.)
            // and handles gamepad state machine if a controller is connected.
            KeyContext context = DetermineContext();
            ControllerRouter.Update(context);

            // Modal dialogs consume all keyboard input when open
            if (TextInputWindow.HandleInput())
                return;
            if (ConfirmationDialog.HandleInput())
                return;

            // Mod menu keyboard input
            if (ModMenu.HandleInput())
                return;

            // --- Keyboard dispatch via GetAsyncKeyState (independent of Unity Input + InputSystemManager) ---
            // Mod reads keyboard via GamepadManager (GetAsyncKeyState — hardware state, no window focus needed).
            // Game keyboard suppressed by InputSystemManager patches when SuppressGameInput is true.
            if (!GamepadManager.AnyKeyboardKeyDown())
                return;

            // Track that keyboard was the last input device
            ControllerRouter.NotifyKeyboardInput();

            // Skip hotkeys when player is typing in a text field
            if (IsInputFieldFocused())
                return;

            // F8 to open mod menu (unavailable in battle)
            if (GamepadManager.IsKeyCodePressed(KeyCode.F8))
            {
                if (!BattleStateHelper.IsInBattle)
                    ModMenu.Open();
                else
                    FFI_ScreenReaderMod.SpeakText(T("Unavailable in battle"), interrupt: true);
                return;
            }

            // Handle function keys (F1/F3/F5)
            HandleFunctionKeyInput();

            // Determine active context
            KeyContext activeContext = DetermineContext();
            KeyModifier currentModifiers = GetCurrentModifiers();

            // Dispatch all registered keyboard bindings
            DispatchRegisteredBindings(activeContext, currentModifiers);
        }

        private KeyContext DetermineContext()
        {
            var statusTracker = StatusNavigationTracker.Instance;
            if (statusTracker.IsNavigationActive && statusTracker.ValidateState())
                return KeyContext.Status;

            var bestiaryTracker = BestiaryNavigationTracker.Instance;
            if (bestiaryTracker.IsNavigationActive && bestiaryTracker.ValidateState())
                return KeyContext.BestiaryDetail;

            if (BattleStateHelper.IsInBattle)
                return KeyContext.Battle;

            // Only return Field if player controller exists
            // (prevents Field keys from firing on title screen, boot screen, etc.)
            try
            {
                var pc = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
                if (pc?.fieldPlayer != null)
                    return KeyContext.Field;
            }
            catch { }

            return KeyContext.Global;
        }

        private KeyModifier GetCurrentModifiers()
        {
            bool shift = GamepadManager.IsKeyCodeHeld(KeyCode.LeftShift) || GamepadManager.IsKeyCodeHeld(KeyCode.RightShift);
            bool ctrl = GamepadManager.IsKeyCodeHeld(KeyCode.LeftControl) || GamepadManager.IsKeyCodeHeld(KeyCode.RightControl);

            if (ctrl && shift) return KeyModifier.CtrlShift;
            if (ctrl) return KeyModifier.Ctrl;
            if (shift) return KeyModifier.Shift;
            return KeyModifier.None;
        }

        private void DispatchRegisteredBindings(KeyContext activeContext, KeyModifier currentModifiers)
        {
            foreach (var key in registry.RegisteredKeys)
            {
                if (GamepadManager.IsKeyCodePressed(key))
                    registry.TryExecute(key, currentModifiers, activeContext);
            }
        }

        private void HandleFunctionKeyInput()
        {
            if (GamepadManager.IsKeyCodePressed(KeyCode.F1))
            {
                CoroutineManager.StartUntracked(FunctionKeyHandler.AnnounceWalkRunState());
                return;
            }

            if (GamepadManager.IsKeyCodePressed(KeyCode.F3))
            {
                CoroutineManager.StartUntracked(FunctionKeyHandler.AnnounceEncounterState());
                return;
            }

            if (GamepadManager.IsKeyCodePressed(KeyCode.F5))
            {
                if (BattleStateHelper.IsInBattle)
                {
                    int current = PreferencesManager.EnemyHPDisplay;
                    int next = (current + 1) % 3;
                    PreferencesManager.SetEnemyHPDisplay(next);
                    string[] options = { T("Numbers"), T("Percentage"), T("Hidden") };
                    FFI_ScreenReaderMod.SpeakText(string.Format(T("Enemy HP: {0}"), options[next]), interrupt: true);
                }
                else
                {
                    FFI_ScreenReaderMod.SpeakText(T("Only available in battle"), interrupt: true);
                }
            }
        }

        private static void HandleTabKey()
        {
            if (BattleStateHelper.IsInBattle)
                BattleStateHelper.ForceClearBattleState();
        }

        private bool IsInputFieldFocused()
        {
            try
            {
                if (EventSystem.current == null)
                    return false;

                var currentObj = EventSystem.current.currentSelectedGameObject;
                if (currentObj == null)
                    return false;

                return currentObj.TryGetComponent(out UnityEngine.UI.InputField inputField);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error checking input field state: {ex.Message}");
                return false;
            }
        }
    }
}
