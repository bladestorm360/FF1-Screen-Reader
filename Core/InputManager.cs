using System;
using System.Collections;
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
        private readonly FFI_ScreenReaderMod mod;
        private readonly KeyBindingRegistry registry = new KeyBindingRegistry();

        public InputManager(FFI_ScreenReaderMod mod)
        {
            this.mod = mod;
            InitializeBindings();
        }

        private void RegisterFieldOnly(KeyCode key, KeyModifier modifier, Action action, string description)
        {
            // Field-only action. Off-field (menu/battle/title) the active context is never
            // Field, so this binding has no match and dispatch silently does nothing.
            registry.Register(key, modifier, KeyContext.Field, action, description);
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

            // --- Key-help / controls display: arrow navigation (flat list, no groups) ---
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.KeyHelp, KeyHelpReader.JumpToBottom, "Jump to last control");
            registry.Register(KeyCode.DownArrow, KeyModifier.None, KeyContext.KeyHelp, KeyHelpReader.NavigateNext, "Next control");
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.KeyHelp, KeyHelpReader.JumpToTop, "Jump to first control");
            registry.Register(KeyCode.UpArrow, KeyModifier.None, KeyContext.KeyHelp, KeyHelpReader.NavigatePrevious, "Previous control");

            // --- Field: entity navigation (brackets + backslash) — with battle feedback ---
            RegisterFieldOnly(KeyCode.LeftBracket, KeyModifier.Shift, mod.CyclePreviousCategory, "Previous entity category");
            RegisterFieldOnly(KeyCode.LeftBracket, KeyModifier.None, mod.CyclePrevious, "Previous entity");
            RegisterFieldOnly(KeyCode.RightBracket, KeyModifier.Shift, mod.CycleNextCategory, "Next entity category");
            RegisterFieldOnly(KeyCode.RightBracket, KeyModifier.None, mod.CycleNext, "Next entity");
            RegisterFieldOnly(KeyCode.Backslash, KeyModifier.Ctrl, mod.ToggleToLayerFilter, "Toggle layer filter");
            RegisterFieldOnly(KeyCode.Backslash, KeyModifier.Shift, mod.TogglePathfindingFilter, "Toggle pathfinding filter");
            RegisterFieldOnly(KeyCode.Backslash, KeyModifier.None, () =>
            {
                NavigationTargetTracker.MarkEntity();
                if (PreferencesManager.AudioBeaconsEnabled) mod.RestartEntityBeacon();
                else mod.AnnounceCurrentEntity();
            }, "Announce current entity / restart beacon");

            // --- Field: manual entity rescan (backtick) ---
            RegisterFieldOnly(KeyCode.BackQuote, KeyModifier.None, mod.ForceEntityRescan, "Force entity rescan");

            // --- Field: pathfinding alternate keys (J/K/L/P) — with battle feedback ---
            RegisterFieldOnly(KeyCode.J, KeyModifier.Shift, mod.CyclePreviousCategory, "Previous entity category (alt)");
            RegisterFieldOnly(KeyCode.J, KeyModifier.None, mod.CyclePrevious, "Previous entity (alt)");
            RegisterFieldOnly(KeyCode.K, KeyModifier.None, mod.AnnounceEntityOnly, "Announce entity name (alt)");
            RegisterFieldOnly(KeyCode.L, KeyModifier.Shift, mod.CycleNextCategory, "Next entity category (alt)");
            RegisterFieldOnly(KeyCode.L, KeyModifier.None, mod.CycleNext, "Next entity (alt)");
            RegisterFieldOnly(KeyCode.P, KeyModifier.Ctrl, mod.ToggleToLayerFilter, "Toggle layer filter (alt)");
            RegisterFieldOnly(KeyCode.P, KeyModifier.Shift, mod.TogglePathfindingFilter, "Toggle pathfinding filter (alt)");
            RegisterFieldOnly(KeyCode.P, KeyModifier.None, () =>
            {
                NavigationTargetTracker.MarkEntity();
                if (PreferencesManager.AudioBeaconsEnabled) mod.RestartEntityBeacon();
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
            RegisterFieldOnly(KeyCode.Quote, KeyModifier.None, mod.ToggleFootsteps, "Toggle footsteps");
            RegisterFieldOnly(KeyCode.Semicolon, KeyModifier.None, mod.ToggleWallTones, "Toggle wall tones");
            RegisterFieldOnly(KeyCode.F6, KeyModifier.None, mod.ToggleAudioBeacons, "Toggle audio beacons");

            // --- Field-only category shortcuts ---
            RegisterFieldOnly(KeyCode.K, KeyModifier.Shift, mod.ResetToAllCategory, "Reset to All category");
            RegisterFieldOnly(KeyCode.Equals, KeyModifier.None, mod.CycleNextCategory, "Next entity category (global)");
            RegisterFieldOnly(KeyCode.Minus, KeyModifier.None, mod.CyclePreviousCategory, "Previous entity category (global)");

            // Sort for correct modifier precedence
            registry.FinalizeRegistration();
        }

        public void Update()
        {
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
            KeyContext context = DetermineContext(onDemand: false);
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

            // Game-context hotkeys below only fire when the game window is the foreground
            // window, so mod functions don't trigger while the player is in another app.
            // Placed AFTER the modals so the now-virtual dialogs/menu keep working even when
            // the game window isn't foreground.
            if (!WindowsFocusHelper.IsGameWindowFocused())
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

            // Handle function keys (F5/F7) — bare keypress only
            if (!anyModifierHeld)
                HandleFunctionKeyInput();

            // Determine active context
            KeyContext activeContext = DetermineContext(onDemand: true);
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

        /// <summary>
        /// Determines the active key context.
        /// <paramref name="onDemand"/> = true for a key press (may rescan the scene immediately);
        /// false for the per-frame caller, which rescans for a missing FieldPlayerController at most
        /// once every <see cref="FieldScanIntervalFrames"/> frames.
        /// </summary>
        private KeyContext DetermineContext(bool onDemand)
        {
            // The key-help / controls overlay (config controls + post-new-game) takes priority while shown.
            if (KeyHelpReader.IsScreenActive)
                return KeyContext.KeyHelp;

            var statusTracker = StatusNavigationTracker.Instance;
            if (statusTracker.IsNavigationActive && statusTracker.ValidateState())
                return KeyContext.Status;

            var bestiaryTracker = BestiaryNavigationTracker.Instance;
            if (bestiaryTracker.IsNavigationActive && bestiaryTracker.ValidateState())
                return KeyContext.BestiaryDetail;

            if (BattleStateHelper.IsInBattle)
                return KeyContext.Battle;

            // Field keys only fire while actively on a field map with no menu open.
            // Otherwise fall through to Global so field/entity/waypoint/toggle hotkeys
            // are silent no-ops off-field, while Global info keys still work everywhere.
            if (IsOnValidMap(onDemand) && !MenuStateRegistry.AnyActive())
                return KeyContext.Field;

            return KeyContext.Global;
        }

        // Per-frame scene-scan throttle: where no FieldPlayerController exists (title screen, battle
        // intro, loading), the per-frame caller would otherwise run FindObjectOfType every frame.
        private const int FieldScanIntervalFrames = 30;
        private static int lastFieldScanFrame = -FieldScanIntervalFrames;

        private static bool IsOnValidMap(bool onDemand)
        {
            // Self-heal the cache (like every other FieldPlayerController reader) so a cleared or
            // stale entry can't wedge the field context into Global and silently disable field hotkeys.
            try
            {
                var pc = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
                if (pc == null)
                {
                    // A cache miss triggers a scene scan. Key presses scan immediately; the per-frame
                    // caller scans at most once every FieldScanIntervalFrames frames (a field map that
                    // appears is picked up within half a second, or at once on the next key press).
                    int frame = Time.frameCount;
                    if (!onDemand && frame - lastFieldScanFrame < FieldScanIntervalFrames)
                        return false;
                    lastFieldScanFrame = frame;
                    pc = GameObjectCache.Refresh<Il2CppLast.Map.FieldPlayerController>();
                }
                return pc?.fieldPlayer != null;
            }
            catch { }
            return false;
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

        private static bool IsBufferContext(KeyContext ctx)
            => ctx == KeyContext.Status || ctx == KeyContext.BestiaryDetail || ctx == KeyContext.KeyHelp;

        private void DispatchRegisteredBindings(KeyContext activeContext, KeyModifier currentModifiers)
        {
            foreach (var key in registry.RegisteredKeys)
            {
                if (GamepadManager.IsKeyCodePressed(key))
                    registry.TryExecute(key, currentModifiers, activeContext);
            }

            // WASD as alternative arrow keys — ONLY in navigation-buffer contexts, so game WASD
            // movement and letter hotkeys in other contexts are untouched. Reuses the arrow bindings,
            // so modifiers carry (Shift+W = Shift+Up = previous group, etc.). Left/Right (A/D) are
            // unregistered in these contexts and harmlessly no-op.
            if (IsBufferContext(activeContext))
            {
                if (GamepadManager.IsKeyCodePressed(KeyCode.W)) registry.TryExecute(KeyCode.UpArrow, currentModifiers, activeContext);
                if (GamepadManager.IsKeyCodePressed(KeyCode.S)) registry.TryExecute(KeyCode.DownArrow, currentModifiers, activeContext);
                if (GamepadManager.IsKeyCodePressed(KeyCode.A)) registry.TryExecute(KeyCode.LeftArrow, currentModifiers, activeContext);
                if (GamepadManager.IsKeyCodePressed(KeyCode.D)) registry.TryExecute(KeyCode.RightArrow, currentModifiers, activeContext);
            }
        }

        private void HandleFunctionKeyInput()
        {
            if (GamepadManager.IsKeyCodePressed(KeyCode.F7))
                FFI_ScreenReaderMod.Instance?.ToggleAutoDetail();

            if (GamepadManager.IsKeyCodePressed(KeyCode.F5))
            {
                // Enemy HP Display is a battle feature, so gate on in-battle (not IsFieldActive,
                // which is false during battle). FF1 is job-less but still shows enemy HP in battle.
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
                    ControllerRouter.SpeakModMenuUnavailable();
                }
            }
        }

        /// <summary>
        /// Tab: fallback that clears a STUCK in-battle flag (battle ended without the end hooks firing).
        /// Only clears when no live BattleController exists; during a real battle it does nothing, so a
        /// Tab press mid-battle can no longer silence battle speech.
        /// </summary>
        private static void HandleTabKey()
        {
            if (!BattleStateHelper.IsInBattle)
                return;

            if (BattleStateHelper.IsBattleControllerAlive())
            {
                MelonLogger.Msg("[Battle] Tab: battle is live (BattleController active), battle state kept");
                return;
            }

            MelonLogger.Msg("[Battle] Tab: no live BattleController, clearing stuck battle state");
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
