using System;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using Il2CppLast.Map;
using FFI_ScreenReader.Patches;
using FFI_ScreenReader.Menus;
using FFI_ScreenReader.Core.Handlers;
using FFI_ScreenReader.Utils;
using static FFI_ScreenReader.Utils.ModTextTranslator;

namespace FFI_ScreenReader.Core
{
    public enum ControllerState
    {
        Normal,
        ModMode,
        ModMenu
    }

    /// <summary>
    /// Central controller routing — state machine that decides where each SDL input goes.
    /// SDL consumes ALL controller buttons. Default behavior is pass to game.
    /// Mod functions consume specific buttons; everything else passes through via InputPassthroughPatches.
    /// </summary>
    public static class ControllerRouter
    {
        public static ControllerState State { get; private set; } = ControllerState.Normal;

        /// <summary>Current game context, set each frame by InputManager.</summary>
        public static KeyContext CurrentGameContext { get; set; } = KeyContext.Global;

        /// <summary>
        /// True when the player is actively on the field with no menu or battle overlay.
        /// Single source of truth — used by ControllerRouter, InputPassthroughPatches,
        /// and FFI_ScreenReaderMod audio suppression. Computed once per frame in Update().
        /// </summary>
        public static bool IsFieldActive { get; private set; } = false;

        /// <summary>
        /// True when the game should receive no input at all.
        /// </summary>
        public static bool SuppressGameInput =>
            State == ControllerState.ModMode
            || State == ControllerState.ModMenu
            || ModMenu.IsOpen
            || TextInputWindow.IsOpen
            || ConfirmationDialog.IsOpen;

        /// <summary>Buttons consumed by the mod this frame (not passed to game).</summary>
        private static readonly bool[] consumedButtons = new bool[SDL3.SDL_GAMEPAD_BUTTON_COUNT];

        public static bool IsButtonConsumed(int btn) =>
            btn >= 0 && btn < SDL3.SDL_GAMEPAD_BUTTON_COUNT && consumedButtons[btn];

        // --- Input device tracking for context-aware help ---
        public enum LastInputDevice { Keyboard, Controller }
        public static LastInputDevice LastDevice { get; private set; } = LastInputDevice.Keyboard;

        /// <summary>Called by InputManager when keyboard input is detected.</summary>
        public static void NotifyKeyboardInput() => LastDevice = LastInputDevice.Keyboard;

        // --- Field state tracking ---
        private static bool leftTriggerWasActive = false;
        private static bool wasLeftStickActive = false;

        // --- Field stick clicks (see UpdateStickClicks) ---
        private static int stickClickButton = -1;   // the stick click being tracked, -1 if none
        private static bool stickChordFired;        // L3+R3 already toggled during this press

        // Synthetic press handed to the game for a lone stick click while Stick Click
        // Normalization is on: "down" (GetKeyDown + GetKey) on the frame the click resolves,
        // "up" (GetKeyUp) on the next frame. Read by InputPassthroughPatches.
        private static int pulseButton = -1;
        private static bool pulseDownPhase;

        public static bool IsStickPulseDown(int btn) => btn >= 0 && btn == pulseButton && pulseDownPhase;
        public static bool IsStickPulseUp(int btn) => btn >= 0 && btn == pulseButton && !pulseDownPhase;

        // =====================================================================
        // Main update — called from InputManager.Update() every frame
        // =====================================================================

        public static void Update(KeyContext gameContext)
        {
            CurrentGameContext = gameContext;

            // Compute field-active state once per frame (single source of truth)
            // This runs even without a gamepad so audio suppression works for keyboard-only users.
            IsFieldActive = gameContext == KeyContext.Field
                && !MenuStateRegistry.AnyActive()
                && !BattleStateHelper.IsInBattle;

            if (!GamepadManager.IsAvailable)
            {
                // Gamepad gone (unplugged, or never present): release controller-owned state so a
                // leftover MOD_MODE can't keep SuppressGameInput set and lock the keyboard out of the
                // game. A mod menu that is still open stays open (keyboard can drive and close it).
                ReleaseControllerOwnedState();
                return;
            }

            // Track that controller is being used
            for (int i = 0; i < SDL3.SDL_GAMEPAD_BUTTON_COUNT; i++)
            {
                if (GamepadManager.IsButtonPressed(i))
                {
                    LastDevice = LastInputDevice.Controller;
                    break;
                }
            }

            // Clear consumed flags — all buttons start as "pass to game"
            Array.Clear(consumedButtons, 0, consumedButtons.Length);

            // State transitions (Start → mod menu, Back → mod mode)
            HandleStateTransitions();

            // Field L3 / R3 and the L3+R3 chord
            UpdateStickClicks();

            // Route inputs based on current state
            switch (State)
            {
                case ControllerState.Normal:
                    HandleNormalState(gameContext);
                    break;
                case ControllerState.ModMode:
                    HandleModModeState();
                    break;
                case ControllerState.ModMenu:
                    HandleModMenuState();
                    break;
            }
        }

        public static void Reset()
        {
            State = ControllerState.Normal;
            Array.Clear(consumedButtons, 0, consumedButtons.Length);
        }

        /// <summary>
        /// Called every frame while no gamepad is available. MOD_MODE exists only for the controller,
        /// so it is dropped; MOD_MENU is kept only while the mod menu is actually open (it mirrors the
        /// menu, which the keyboard can still navigate and close). Cheap no-op once released.
        /// </summary>
        private static void ReleaseControllerOwnedState()
        {
            if (State == ControllerState.ModMode
                || (State == ControllerState.ModMenu && !ModMenu.IsOpen))
            {
                MelonLogger.Msg($"[ControllerRouter] No gamepad: leaving {State}, game input restored");
                State = ControllerState.Normal;
            }

            // Stale edge/consume state must not carry over to a reconnected controller.
            leftTriggerWasActive = false;
            wasLeftStickActive = false;
            stickClickButton = -1;
            stickChordFired = false;
            pulseButton = -1;
            Array.Clear(consumedButtons, 0, consumedButtons.Length);
        }

        /// <summary>
        /// Keeps the controller state in step with the mod menu however it was opened or closed
        /// (F8 / Escape / "Close Menu" on the keyboard, Start / B on the controller). Opening puts the
        /// router in MOD_MENU (D-pad, stick and LT drive the menu instead of acting underneath it);
        /// closing returns it to NORMAL. Called from ModMenu.Open/Close.
        /// </summary>
        internal static void SyncWithModMenu(bool menuOpen)
        {
            if (menuOpen)
                State = ControllerState.ModMenu;
            else if (State == ControllerState.ModMenu)
                State = ControllerState.Normal;
        }

        // =====================================================================
        // Context-aware controls announcement (RB on controller, Shift+I on keyboard)
        // =====================================================================

        /// <summary>
        /// Announces controls for the current context. Called by RB (controller)
        /// or Shift+I (keyboard). Reads game key help in game menus, mod controls elsewhere.
        /// </summary>
        public static void AnnounceContextControls()
        {
            if (State == ControllerState.ModMode)
            {
                AnnounceModModeControls();
            }
            else if (State == ControllerState.ModMenu || ModMenu.IsOpen)
            {
                AnnounceModMenuControls();
            }
            else
            {
                // On field or in game menus — read game's built-in key help
                KeyHelpReader.AnnounceKeyHelp();
            }
        }

        private static void AnnounceModModeControls()
        {
            string back = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_BACK);
            string west = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_WEST);

            if (MessageWindowPatches.IsInDialogue)
            {
                FFI_ScreenReaderMod.SpeakText(
                    string.Format(T("{0} to repeat dialogue. {1} to cancel."), west, back),
                    interrupt: true);
            }
            else if (BattleStateHelper.IsInBattle)
            {
                FFI_ScreenReaderMod.SpeakText(
                    string.Format(T("{0} for party HP. {1} to cancel."), west, back),
                    interrupt: true);
            }
            else
            {
                string north = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_NORTH);
                string south = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_SOUTH);
                FFI_ScreenReaderMod.SpeakText(
                    string.Format(T("{0} for Gil. {1} for location. {2} for vehicle. Right stick to teleport. {3} to cancel."),
                    west, north, south, back),
                    interrupt: true);
            }
        }

        private static void AnnounceModMenuControls()
        {
            string confirm = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_SOUTH);
            string close = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_EAST);
            string start = ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_START);

            FFI_ScreenReaderMod.SpeakText(
                string.Format(T("D-pad or Left Stick Up, Down to navigate. Left, Right to adjust values. {0} to toggle. {1} or {2} to close."),
                confirm, close, start),
                interrupt: true);
        }

        // =====================================================================
        // State transitions
        // =====================================================================

        private static void HandleStateTransitions()
        {
            // Start → mod menu toggle. Close is always allowed; open is gated to field-only.
            if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_START))
            {
                ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_START);

                if (State == ControllerState.ModMenu)
                    CloseModMenu();
                else if (IsFieldActive)
                    OpenModMenu();
                else
                    SpeakModMenuUnavailable();
                return;
            }

            // Back/Select → mod mode toggle (Normal ↔ ModMode)
            if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_BACK))
            {
                ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_BACK);

                if (State == ControllerState.Normal)
                {
                    State = ControllerState.ModMode;
                    FFI_ScreenReaderMod.SpeakText(T("Mod"), interrupt: true);
                }
                else if (State == ControllerState.ModMode)
                {
                    State = ControllerState.Normal;
                    FFI_ScreenReaderMod.SpeakText(T("Cancelled"), interrupt: true);
                }
            }
        }

        private static void OpenModMenu()
        {
            ModMenu.Open(); // speaks "Mod menu" + first item; SyncWithModMenu(true) sets MOD_MENU
        }

        /// <summary>
        /// Announces why the mod menu can't be opened. Shared by Start-button and F8 entry points.
        /// </summary>
        internal static void SpeakModMenuUnavailable()
        {
            string reason;
            if (BattleStateHelper.IsInBattle)                 reason = T("Unavailable in battle");
            else if (MenuStateRegistry.AnyActive())           reason = T("Unavailable in menu");
            else                                              reason = T("Unavailable here");
            FFI_ScreenReaderMod.SpeakText(reason, interrupt: true);
        }

        private static void CloseModMenu()
        {
            ModMenu.Close(); // speaks "Mod menu closed"; SyncWithModMenu(false) sets NORMAL
        }

        // =====================================================================
        // NORMAL state
        // =====================================================================

        private static void HandleNormalState(KeyContext context)
        {
            // Face buttons (A/B/X/Y) interrupt queued speech — same as Enter on keyboard.
            if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_SOUTH)
             || GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_EAST)
             || GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_WEST)
             || GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_NORTH))
                FFI_ScreenReaderMod.InterruptSpeech();

            // 15-puzzle: right-stick up announces the current cursor row/column.
            // Checked before the field branch so it works regardless of computed context.
            if (PuzzleGameState.IsActive)
            {
                if (GamepadManager.RStickUpPressed) PuzzlePatches.AnnouncePosition();
                return;
            }

            if (IsFieldActive)
                HandleNormalField();
            else
                HandleNormalNonField(context);

            // LB/RB always pass through to game (used for tab switching in status/menus)
        }

        // IsFieldActive is now computed once per frame in Update() and exposed as a public property.

        private static void HandleNormalField()
        {
            var mod = FFI_ScreenReaderMod.Instance;
            if (mod == null) return;

            // L3 / R3 are handled by UpdateStickClicks.

            // Interrupt speech on any navigation input
            bool leftStickActive = GamepadManager.LeftStickX != 0f || GamepadManager.LeftStickY != 0f;
            bool leftStickJustMoved = leftStickActive && !wasLeftStickActive;
            wasLeftStickActive = leftStickActive;

            bool anyNavInput = leftStickJustMoved
                || GamepadManager.DpadUpPressed || GamepadManager.DpadDownPressed
                || GamepadManager.DpadLeftPressed || GamepadManager.DpadRightPressed
                || GamepadManager.RStickUpPressed || GamepadManager.RStickDownPressed
                || GamepadManager.RStickLeftPressed || GamepadManager.RStickRightPressed;

            if (anyNavInput)
                FFI_ScreenReaderMod.InterruptSpeech();

            // D-pad → waypoint navigation (consumed). The callees mark the tracker.
            if (GamepadManager.DpadUpPressed) { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_UP); WaypointHandler.CyclePrevious(); }
            if (GamepadManager.DpadDownPressed) { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_DOWN); WaypointHandler.CycleNext(); }
            if (GamepadManager.DpadLeftPressed) { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_LEFT); WaypointHandler.CyclePreviousCategory(); }
            if (GamepadManager.DpadRightPressed) { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_RIGHT); WaypointHandler.CycleNextCategory(); }

            // Right stick → entity scanner (callees mark the tracker).
            if (GamepadManager.RStickUpPressed) mod.CyclePrevious();
            if (GamepadManager.RStickDownPressed) mod.CycleNext();
            if (GamepadManager.RStickLeftPressed) mod.CyclePreviousCategory();
            if (GamepadManager.RStickRightPressed) mod.CycleNextCategory();

            // Left trigger → pathfind to last selected target (or restart beacon in beacon nav mode)
            if (GamepadManager.LeftTrigger > 0.5f && !leftTriggerWasActive)
            {
                switch (NavigationTargetTracker.LastKind)
                {
                    case NavigationTargetTracker.Kind.Waypoint:
                        WaypointHandler.PathfindToCurrentWaypoint();
                        break;
                    case NavigationTargetTracker.Kind.Entity:
                        if (PreferencesManager.AudioBeaconsEnabled) mod.RestartEntityBeacon();
                        else mod.AnnounceCurrentEntity();
                        break;
                    default:
                        FFI_ScreenReaderMod.SpeakText(T("No target selected"), interrupt: true);
                        break;
                }
            }
            leftTriggerWasActive = GamepadManager.LeftTrigger > 0.5f;
        }

        private static void HandleNormalNonField(KeyContext context)
        {
            // Right stick up → read description (I key equivalent)
            if (GamepadManager.RStickUpPressed)
                GlobalHotkeyHandler.HandleItemDetailsKey(FFI_ScreenReaderMod.Instance);

            // Right stick down → read controls (Shift+I equivalent)
            if (GamepadManager.RStickDownPressed)
                KeyHelpReader.AnnounceKeyHelp();

            // Right stick left → usable by classes (U key equivalent)
            if (GamepadManager.RStickLeftPressed)
                FFI_ScreenReader.Menus.UsableByAnnouncer.AnnounceForCurrentContext();

            // D-pad and left stick → virtual buffer navigation in Status/Bestiary
            if (context == KeyContext.Status)
            {
                if (GamepadManager.DpadUpPressed || GamepadManager.LeftStickUpPressed)
                { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_UP); StatusNavigationReader.NavigatePrevious(); }
                if (GamepadManager.DpadDownPressed || GamepadManager.LeftStickDownPressed)
                { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_DOWN); StatusNavigationReader.NavigateNext(); }
            }
            else if (context == KeyContext.BestiaryDetail)
            {
                if (GamepadManager.DpadUpPressed || GamepadManager.LeftStickUpPressed)
                { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_UP); BestiaryNavigationReader.NavigatePrevious(); }
                if (GamepadManager.DpadDownPressed || GamepadManager.LeftStickDownPressed)
                { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_DOWN); BestiaryNavigationReader.NavigateNext(); }
            }
            else if (context == KeyContext.KeyHelp)
            {
                if (GamepadManager.DpadUpPressed || GamepadManager.LeftStickUpPressed)
                { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_UP); KeyHelpReader.NavigatePrevious(); }
                if (GamepadManager.DpadDownPressed || GamepadManager.LeftStickDownPressed)
                { ConsumeButton(SDL3.SDL_GAMEPAD_BUTTON_DPAD_DOWN); KeyHelpReader.NavigateNext(); }
            }
        }

        // =====================================================================
        // Field stick clicks — L3, R3 and the L3+R3 chord
        // =====================================================================

        /// <summary>
        /// A stick click that starts in NORMAL on the active field is resolved on RELEASE, so both
        /// clicks together can make the L3+R3 chord without the first acting alone. The chord
        /// toggles Stick Click Normalization whatever its value. A lone click then does its job:
        /// normalization off → L3 beacon navigation, R3 pathfinding filter; on → the click goes to
        /// the game as a one-frame press (encounters / walk-run). Both clicks are consumed from the
        /// first press until both are up, and act only if the player is still on the field in
        /// NORMAL. Mod mode and every other screen keep their own stick-click handling.
        /// </summary>
        private static void UpdateStickClicks()
        {
            // Retire the previous synthetic press: down → up → none.
            if (pulseButton >= 0)
            {
                if (pulseDownPhase) pulseDownPhase = false;
                else pulseButton = -1;
            }

            const int L3 = SDL3.SDL_GAMEPAD_BUTTON_LEFT_STICK;
            const int R3 = SDL3.SDL_GAMEPAD_BUTTON_RIGHT_STICK;

            if (stickClickButton < 0)
            {
                bool l3Down = GamepadManager.IsButtonPressed(L3);
                if (!l3Down && !GamepadManager.IsButtonPressed(R3)) return;
                if (State != ControllerState.Normal || !IsFieldActive || PuzzleGameState.IsActive) return;

                stickClickButton = l3Down ? L3 : R3;
                stickChordFired = false;
            }

            ConsumeButton(L3);
            ConsumeButton(R3);

            bool l3Held = GamepadManager.IsButtonHeld(L3);
            bool r3Held = GamepadManager.IsButtonHeld(R3);
            bool canAct = State == ControllerState.Normal && IsFieldActive;

            if (l3Held && r3Held)
            {
                if (!stickChordFired && canAct)
                {
                    stickChordFired = true;
                    FFI_ScreenReaderMod.Instance?.ToggleStickClickNormalization();
                }
                return;
            }
            if (l3Held || r3Held) return;

            // Both up: the press is over.
            int button = stickClickButton;
            stickClickButton = -1;
            if (stickChordFired || !canAct) return;

            if (PreferencesManager.StickClickNormalizationEnabled)
            {
                pulseButton = button;
                pulseDownPhase = true;
            }
            else if (button == L3)
                FFI_ScreenReaderMod.Instance?.ToggleAudioBeacons();
            else
                FFI_ScreenReaderMod.Instance?.TogglePathfindingFilter();
        }

        // =====================================================================
        // MOD_MODE — face buttons → mod info, then auto-deactivate
        // =====================================================================

        private static void HandleModModeState()
        {
            // All buttons consumed in mod mode
            for (int i = 0; i < SDL3.SDL_GAMEPAD_BUTTON_COUNT; i++)
                consumedButtons[i] = true;

            var mod = FFI_ScreenReaderMod.Instance;
            if (mod == null) return;

            // Dialogue takes precedence over battle/field — if a message window is up,
            // the user wants to repeat the message, not check HP or Gil.
            if (MessageWindowPatches.IsInDialogue)
            {
                if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_WEST))
                { MessageWindowPatches.RepeatLastDialogue(); State = ControllerState.Normal; return; }
            }
            else if (BattleStateHelper.IsInBattle)
            {
                // Battle mod mode: X = party HP check
                if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_WEST))
                { mod.AnnounceCharacterStatus(); State = ControllerState.Normal; return; }
            }
            else
            {
                // Field mod mode: X=Gil, Y=Location, A=Vehicle
                if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_WEST))
                { mod.AnnounceGilAmount(); State = ControllerState.Normal; return; }

                if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_NORTH))
                { mod.AnnounceCurrentMap(); State = ControllerState.Normal; return; }

                if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_SOUTH))
                { GlobalHotkeyHandler.AnnounceCurrentVehicle(); State = ControllerState.Normal; return; }

                // When Stick Click Normalization is on, the stick-click mod functions move
                // here so the player can still reach them via mod button + R3/L3.
                if (PreferencesManager.StickClickNormalizationEnabled)
                {
                    if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_RIGHT_STICK))
                    { mod.TogglePathfindingFilter(); State = ControllerState.Normal; return; }

                    if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_LEFT_STICK))
                    { mod.ToggleAudioBeacons(); State = ControllerState.Normal; return; }
                }

                // Right stick → teleport (field only)
                if (GamepadManager.RStickUpPressed)
                { mod.TeleportInDirection(new Vector2(0, 16)); State = ControllerState.Normal; return; }

                if (GamepadManager.RStickDownPressed)
                { mod.TeleportInDirection(new Vector2(0, -16)); State = ControllerState.Normal; return; }

                if (GamepadManager.RStickLeftPressed)
                { mod.TeleportInDirection(new Vector2(-16, 0)); State = ControllerState.Normal; return; }

                if (GamepadManager.RStickRightPressed)
                { mod.TeleportInDirection(new Vector2(16, 0)); State = ControllerState.Normal; return; }
            }

            // Right stick down → announce mod mode controls (always available)
            // Note: in field, right stick down triggers teleport south above instead
            if (BattleStateHelper.IsInBattle && GamepadManager.RStickDownPressed)
                AnnounceModModeControls();
        }

        // =====================================================================
        // MOD_MENU — controller navigates mod menu, all game input suppressed
        // =====================================================================

        private static void HandleModMenuState()
        {
            // All buttons consumed
            for (int i = 0; i < SDL3.SDL_GAMEPAD_BUTTON_COUNT; i++)
                consumedButtons[i] = true;

            if (!ModMenu.IsOpen)
            {
                State = ControllerState.Normal;
                return;
            }

            bool up = GamepadManager.DpadUpPressed || GamepadManager.LeftStickUpPressed;
            bool down = GamepadManager.DpadDownPressed || GamepadManager.LeftStickDownPressed;
            bool left = GamepadManager.DpadLeftPressed || GamepadManager.LeftStickLeftPressed;
            bool right = GamepadManager.DpadRightPressed || GamepadManager.LeftStickRightPressed;

            if (up) ModMenu.NavigatePrevious();
            if (down) ModMenu.NavigateNext();
            if (left) ModMenu.AdjustCurrentItem(-1);
            if (right) ModMenu.AdjustCurrentItem(1);

            if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_SOUTH))
                ModMenu.ToggleCurrentItem();

            if (GamepadManager.IsButtonPressed(SDL3.SDL_GAMEPAD_BUTTON_EAST))
                CloseModMenu();

            // Right stick down → announce mod menu controls
            if (GamepadManager.RStickDownPressed)
                AnnounceModMenuControls();
        }

        // =====================================================================
        // Field movement feed (kept for potential future use)
        // =====================================================================

        private static void FeedLeftStickToGame()
        {
            if (GamepadManager.LeftStickX == 0f && GamepadManager.LeftStickY == 0f)
                return;

            try
            {
                var fpc = GameObjectCache.Get<FieldPlayerController>();
                var keyController = fpc?.TryCast<FieldPlayerKeyController>();
                if (keyController != null)
                    keyController.OnTouchPadCallback(new Vector2(GamepadManager.LeftStickX, GamepadManager.LeftStickY));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[ControllerRouter] FeedLeftStick error: {ex.Message}");
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private static void ConsumeButton(int btn)
        {
            if (btn >= 0 && btn < SDL3.SDL_GAMEPAD_BUTTON_COUNT)
                consumedButtons[btn] = true;
        }
    }
}
