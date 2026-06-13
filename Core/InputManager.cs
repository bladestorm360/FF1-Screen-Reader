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

            // --- Bestiary detail: arrow key navigation ---
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToBottom, "Jump to bottom stat (bestiary)");
            registry.Register(KeyCode.DownArrow, KeyModifier.Shift, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToNextGroup, "Jump to next group (bestiary)");
            registry.Register(KeyCode.DownArrow, KeyModifier.None, KeyContext.BestiaryDetail, BestiaryNavigationReader.NavigateNext, "Next stat (bestiary)");
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToTop, "Jump to top stat (bestiary)");
            registry.Register(KeyCode.UpArrow, KeyModifier.Shift, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToPreviousGroup, "Jump to previous group (bestiary)");
            registry.Register(KeyCode.UpArrow, KeyModifier.None, KeyContext.BestiaryDetail, BestiaryNavigationReader.NavigatePrevious, "Previous stat (bestiary)");

            // --- Field: entity navigation (brackets + backslash) — with battle feedback ---
            RegisterFieldWithBattleFeedback(KeyCode.LeftBracket, KeyModifier.Shift, mod.CyclePreviousCategory, "Previous entity category");
            RegisterFieldWithBattleFeedback(KeyCode.LeftBracket, KeyModifier.None, mod.CyclePrevious, "Previous entity");
            RegisterFieldWithBattleFeedback(KeyCode.RightBracket, KeyModifier.Shift, mod.CycleNextCategory, "Next entity category");
            RegisterFieldWithBattleFeedback(KeyCode.RightBracket, KeyModifier.None, mod.CycleNext, "Next entity");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.Ctrl, mod.ToggleToLayerFilter, "Toggle layer filter");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.Shift, mod.TogglePathfindingFilter, "Toggle pathfinding filter");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.None, () =>
            {
                NavigationTargetTracker.MarkEntity();
                if (FFI_ScreenReaderMod.AudioBeaconsEnabled) mod.RestartBeacon();
                else mod.AnnounceCurrentEntity();
            }, "Announce current entity / restart beacon");

            // --- Field: manual entity rescan (backtick) ---
            RegisterFieldWithBattleFeedback(KeyCode.BackQuote, KeyModifier.None, mod.ForceEntityRescan, "Force entity rescan");

            // --- Field: pathfinding alternate keys (J/K/L/P) — with battle feedback ---
            RegisterFieldWithBattleFeedback(KeyCode.J, KeyModifier.Shift, mod.CyclePreviousCategory, "Previous entity category (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.J, KeyModifier.None, mod.CyclePrevious, "Previous entity (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.K, KeyModifier.None, mod.AnnounceEntityOnly, "Announce entity name (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.L, KeyModifier.Shift, mod.CycleNextCategory, "Next entity category (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.L, KeyModifier.None, mod.CycleNext, "Next entity (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.P, KeyModifier.Shift, mod.TogglePathfindingFilter, "Toggle pathfinding filter (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.P, KeyModifier.None, () =>
            {
                NavigationTargetTracker.MarkEntity();
                if (FFI_ScreenReaderMod.AudioBeaconsEnabled) mod.RestartBeacon();
                else mod.AnnounceCurrentEntity();
            }, "Announce current entity / restart beacon (alt)");

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

            // --- Global: R key (repeat current dialogue / message) ---
            registry.Register(KeyCode.R, KeyContext.Global, () =>
            {
                if (MessageWindowPatches.IsInDialogue)
                    MessageWindowPatches.RepeatLastDialogue();
            }, "Repeat dialogue");

            // --- Global: Shift+I key (context-aware controls) ---
            registry.Register(KeyCode.I, KeyModifier.Shift, KeyContext.Global, ControllerRouter.AnnounceContextControls, "Announce controls");

            // --- Global: I key (cascading menu priority) ---
            registry.Register(KeyCode.I, KeyContext.Global, () => GlobalHotkeyHandler.HandleItemDetailsKey(mod), "Item details");

            // --- Global: U key (usable-by classes) ---
            registry.Register(KeyCode.U, KeyContext.Global, UsableByAnnouncer.AnnounceForCurrentContext, "Usable by classes");

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

            // Light per-frame poll for the two game-side toggles (encounters / auto-dash) —
            // announces on change so menu/controller toggles all surface through the screen reader.
            GameToggleAnnouncer.Poll();

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

            // Per-frame footstep tile-crossing poll (field-active gated, silent in vehicles).
            // Cadence naturally tracks actual movement speed — walk slower than dash than vehicles.
            FFI_ScreenReader.Patches.MovementSoundPatches.PollFootsteps();

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

            // Modifier-bare hotkeys only fire when no modifier is held — so OS shortcuts
            // like Alt+F4 (close), Ctrl+F4 (game's own bindings), Shift+F4 don't trigger
            // the screen reader's F-keys. Shift+M / Shift+K etc. still work because they
            // have explicit Shift bindings in the registry.
            bool anyModifierHeld = IsAnyModifierHeld();

            // F8 to open mod menu — gated to field-only via ControllerRouter.IsFieldActive
            // (blocks battle, in-game menus, title screen). Rejection wording lives in
            // ControllerRouter.SpeakModMenuUnavailable so Start-button and F8 stay in sync.
            if (!anyModifierHeld && GamepadManager.IsKeyCodePressed(KeyCode.F8))
            {
                if (ControllerRouter.IsFieldActive)
                    ModMenu.Open();
                else
                    ControllerRouter.SpeakModMenuUnavailable();
                return;
            }

            // Handle function keys (F4/F5/F6) — bare keypress only
            if (!anyModifierHeld)
                HandleFunctionKeyInput();

            // Determine active context
            KeyContext activeContext = DetermineContext();
            KeyModifier currentModifiers = GetCurrentModifiers();

            // Alt held with no registered Alt-binding → skip dispatch so Alt+U etc. don't
            // accidentally trigger the unmodified U binding. (Shift/Ctrl are already
            // routed through currentModifiers and matched exactly by the registry.)
            if (IsAltHeld())
                return;

            // Dispatch all registered keyboard bindings
            DispatchRegisteredBindings(activeContext, currentModifiers);
        }

        private static bool IsAltHeld()
        {
            return GamepadManager.IsKeyCodeHeld(KeyCode.LeftAlt)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightAlt);
        }

        private static bool IsAnyModifierHeld()
        {
            return GamepadManager.IsKeyCodeHeld(KeyCode.LeftShift)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightShift)
                || GamepadManager.IsKeyCodeHeld(KeyCode.LeftControl)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightControl)
                || GamepadManager.IsKeyCodeHeld(KeyCode.LeftAlt)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightAlt);
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
            if (GamepadManager.IsKeyCodePressed(KeyCode.F7))
                FFI_ScreenReaderMod.Instance?.ToggleAutoDetail();

            if (GamepadManager.IsKeyCodePressed(KeyCode.F5))
            {
                if (ControllerRouter.IsFieldActive)
                {
                    int current = PreferencesManager.EnemyHPDisplay;
                    int next = (current + 1) % 3;
                    PreferencesManager.SetEnemyHPDisplay(next);
                    string[] options = { T("Numbers"), T("Percentage"), T("Hidden") };
                    FFI_ScreenReaderMod.SpeakText(string.Format(T("Enemy HP: {0}"), options[next]), interrupt: true);
                }
                else
                {
                    ControllerRouter.SpeakModMenuUnavailable();
                }
            }

            if (GamepadManager.IsKeyCodePressed(KeyCode.F6))
                FFI_ScreenReaderMod.Instance?.ToggleAudioBeacons();
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
