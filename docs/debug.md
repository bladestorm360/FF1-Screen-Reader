# FF1 Screen Reader - Technical Reference

## IL2CPP Pointer Access

When `AccessTools.Field` returns null, use pointer-based access:
```csharp
IntPtr ptr = ((Il2CppObjectBase)instance).Pointer;
unsafe {
    IntPtr fieldPtr = *(IntPtr*)((byte*)ptr.ToPointer() + OFFSET);
    var list = new Il2CppSystem.Collections.Generic.List<T>(fieldPtr);
}
```

---

## IL2CPP Offsets

### MessageWindowManager
| Field | Offset | Description |
|-------|--------|-------------|
| messageList | 0x88 | Word-wrapped dialogue lines |
| newPageLineList | 0xA0 | END line indices per page |
| spekerValue | 0xA8 | Speaker name (typo in game) |
| currentPageNumber | 0xF8 | Current page (0-based) |

### Menu Controllers (KeyInput namespace)
| Controller | Field | Offset |
|------------|-------|--------|
| EquipmentWindowController | stateMachine | 0x60 |
| ItemWindowController | stateMachine | 0x70 |
| AbilityWindowController | stateMachine | 0x88 |
| AbilityWindowController | statusController | 0x50 |
| AbilityCharaStatusController | targetData | 0x48 |
| ShopController | stateMachine | 0x90 |
| ShopInfoController | view | 0x18 |
| ShopInfoView | descriptionText | 0x38 |
| ShopMagicTargetSelectController | isFoundEquipSlot | 0x70 |
| ConfigControllCommandController | gamePadIconController / keyboardIconController / view | 0x40 / 0x48 / 0x58 |
| ConfigKeyIconController | view | 0x18 |
| ConfigControllCommandView | nameTexts | 0x30 |
| ConfigKeyIconView | iconTextList | 0x30 |
| ConfigKeysSettingController | keydata (KeyConfigData) | 0xC0 |
| OptionController (KeyInput) | selectedItem / selectedDoropDown / isSetting | 0x98 / 0x88 / 0xB8 |
| OptionLanguageContentController (Touch) | view | 0x20 |
| OptionLanguageContentView (Touch) | nameText | 0x18 |

`ConfigKeysSettingController.SelectContent` is **overloaded** (5-arg navigation + 2-arg variant) — patch must pass the explicit `Type[] { int, CustomScrollView, Cursor, IEnumerable<ConfigControllCommandController>, CustomScrollView.WithinRangeType }` or `AccessTools.Method` throws `AmbiguousMatchException` and the patch silently never attaches.

### Battle Controllers
| Controller | Field | Offset |
|------------|-------|--------|
| BattleAbilityInfomationControllerBase | stateMachine | 0x28 |
| BattleAbilityInfomationControllerBase | selectedBattlePlayerData | 0x30 |
| BattleAbilityInfomationControllerBase | dataList | 0x70 |
| BattleAbilityInfomationControllerBase | contentList | 0x78 |
| BattleUnitData | BattleUnitDataInfo | 0x28 |
| BattleUnitDataInfo | Parameter | 0x10 |
| BattleUIManager | pauseController | 0x98 |
| BattlePauseController | isActivePauseMenu | 0x71 |

**Note:** pauseController offset FF1=0x98, FF3=0x90

### Popups (KeyInput namespace)
| Type | Field | Offset |
|------|-------|--------|
| CommonPopup | titleText (IconTextView) | 0x38 |
| CommonPopup | messageText (Text) | 0x40 |
| CommonPopup | selectCursor | 0x68 |
| CommonPopup | commandList | 0x70 |
| ChangeMagicStonePopup | nameText/descriptionText/commandList | 0x28/0x30/0x58 |
| GameOverSelectPopup | commandList | 0x40 |
| InfomationPopup | titleText/messageText | 0x28/0x30 |
| SavePopup | messageText/commandList | 0x40/0x60 |
| IconTextView | nameText | 0x20 |
| CommonCommand | text | 0x18 |

### Save/Load Controllers
| Controller | Field | Offset |
|------------|-------|--------|
| LoadGameWindowController | savePopup | 0x58 |
| LoadWindowController / SaveWindowController | savePopup | 0x28 |
| InterruptionWindowController | savePopup | 0x38 |

### New Game (Serial.FF1.UI.KeyInput namespace)
| Controller | Field | Offset |
|------------|-------|--------|
| NewGameWindowController | stateMachine/newGamePopup/autoNameIndex | 0x28/0xD0/0x100 |
| NewGamePopup | messageText/commandList | 0x30/0x40 |

### 15-Puzzle (Serial.FF1.MiniGame namespace)
| Type | Field | Offset |
|------|-------|--------|
| MiniGamePuzzleController | pieceList/cursorPos/emptyPiece | 0x18/0x40/0x44 |
| PuzzlePiece | index | 0x48 |

`cursorPos`/`emptyPiece` are flat board positions 0-15 (row=pos/4, col=pos%4). Tile numbers are sprites, not Text — derived from `PuzzlePiece.index`. Movement methods: NextPiece/PrevPiece/SkipNextPeice/SkipPrevPeice (game's spelling)/TouchPiece. `cursorPos == emptyPiece` ⇒ empty square.

### FieldController / FootEvent
FieldController.FootEvent=0x120, FootEvent.stepOnTriggerList=0x10

---

## Enum Values

**State Machines:** EquipmentWindowController: STATE_COMMAND=1 | AbilityWindowController: STATE_NONE=0, STATE_COMMAND=4 | ItemWindowController: STATE_COMMAND_SELECT=1, STATE_USE_SELECT=2, STATE_IMPORTANT_SELECT=3, STATE_ORGANIZE_SELECT=4, STATE_TARGET_SELECT=5 | ShopController: STATE_SELECT_COMMAND=1, STATE_SELECT_PRODUCT=2, STATE_SELECT_SELL_ITEM=3

**HitType:** 2=Miss, 4=Recovery ("Recovered X HP"), 5=MPHit ("X MP damage"), 6=MPRecovery ("Recovered X MP")

**ConditionType:** 5=KO, 6=Silence, 7=Sleep, 8=Paralysis, 9=Blind, 10=Poison, 11=Stone, 12=Confusion

**TransportationType:** 1=Player, 2=Ship, 3=Airship, 5=Canoe, 7=LowFlying

**Battle Commands:** Attack=0, Magic=1, Items=2, Defend/Run=3

---

## State Classes

ConfigMenuState, ItemMenuState, EquipMenuState, MagicMenuState, StatusMenuState, ShopMenuTracker, BattleCommandState, BattleTargetState, BattleItemMenuState, BattleMagicMenuState, PopupState, SaveLoadMenuState, BattlePauseState, MenuStateRegistry

All delegate deduplication to `AnnouncementDeduplicator` with context keys (e.g., `BattleCmd.Command`, `Shop.Item`).

---

## Architecture Patterns

**Deduplication:** `AnnouncementDeduplicator.ShouldAnnounce(ctx, val)` → true if changed; `Reset(ctx...)` / `ResetAll()` on transitions; naming: `Category.Subcategory`

**Menu/Battle Lifecycle:** Menu: `SetActive(false)` postfix clears state; Battle: set `IsInBattle=true` on start, clear on end, guard all patches

**Dialogue:** `newPageLineList` = END indices (inclusive); `[0,2]` → pages `[0,1,3]`; patch `PlayingInit` + `NewPageInputWaitInit`

**Config Menu:** `SetFocus` → "Setting: Value"; `SwitchArrow/SliderTypeProcess` → just new value. The **Language row** value comes from `ConfigCommandType.Language` → a self-contained `Language→name` map keyed on `MessageManager.currentLanguage` (the game's `LangugeUtility.GetLanguageMessage` returns EMPTY for the *current* language). **Controls/remap screen:** postfix `ConfigKeysSettingController.SelectContent` (5-arg overload — disambiguate the `Type[]`); announce action `nameTexts` + keyboard binding (`keyboardIconController.iconTextList`, readable key names). The gamepad icon is a sprite glyph with NO text, so when the keyboard binding is empty (= gamepad section) read the LIVE bound button: `keydata` (`KeyConfigData`, 0xC0) → `GetGamePadKeyConfigtDictionary()[command.key]` → Unity `KeyCode` → `JoystickButtonN`→SDL index → `ControllerLabels.GetButtonLabel` → "{action} ({button})". FFPR stores Confirm/Cancel JP-style: JB1=Confirm→`SOUTH` (A/Cross), JB0=Cancel→`EAST` (B/Circle); JB2/3→`WEST`/`NORTH`. **Language dropdown:** KeyInput `OptionController.SetDropDownItemFocus` (event-driven, no dedup) speaks the focused language via `GetFocusedLanguageLabel` (item LabelText → dropdown value → current-language name map for the blank current item), gated on `ConfigMenuState.IsActive` so it can't speak over the title "Press any button." Do NOT hook `OptionController.UpdateSelectLanguage` (empty 0x2698F0 stub → launch crash).

**Audio (SDL3):** One SDL audio device, 7 `SDL_AudioStream`s bound to it (Footstep, WallBump, Beacon + WallTone N/S/E/W) — SDL mixes all bound streams, replacing the old 4× winmm waveOut handles and the manual `MixWavFiles`/`GenerateMixedLoopTone`. Per-stream `SDL_SetAudioStreamGain` replaces per-sample `ScaleSamples` (user volume) and carries the `1/sqrt(N)` headroom on the active wall-tone set. Wall-tone loops have no hardware loop flag: `SoundPlayer.PlayWallTonesLooped` (called ~100ms by `AudioLoopManager`) tops up each active direction stream via `SDL_PutAudioStreamData` when `SDL_GetAudioStreamQueued` drops below ~2 buffers; per-direction sustain buffers are cycle-aligned so re-queued loops are click-free. SDL is init/quit per-subsystem (`SDL_InitSubSystem`/`SDL_QuitSubSystem`) so audio (`AudioEngine`) and gamepad (`GamepadManager`) don't tear each other down — no blanket `SDL_Quit`. Device opens paused → `SDL_ResumeAudioDevice` required.

**Popups:** `PopupState` tracks pointer + offset; `Open()` uses `TryCast<T>()`; 1-frame delay; `ShouldSuppress()` = has buttons

**Battle Pause:** Check cursor path "curosr_parent" (typo) BEFORE `BattleCommandState.ShouldSuppress()`; `BattlePauseState.IsActive` reads memory

**Battle Messages:** "The party was defeated" uses `BattleCommandMessageController.SetMessage` (KeyInput namespace), NOT LineFade. Patch both KeyInput and Touch versions.

**Stale Callbacks:** `LastSelectedCommandIndex` in BattleCommandState blocks magic/item callbacks from close animations

**Map Transitions:** Hook `ChangeState()`, check map ID; `LocationMessageTracker` dedupes; fade: poll `IsFadeFinish()`, suppress wall tones when fading

**Entity Scanner Filter:** Player → VehicleTypeMap → Residentchara → Visual effects → Inactive → GotoMapEventEntity → GotoMap → PropertyTelepoPoint → Chests → NPCs → Save points → Vehicles → Door/stairs → Elevation → EventTriggerEntity → FieldMapObjectDefault → IInteractiveEntity

**Entity Names:** Try `GetEntityNameFromProperty()` first (localized); NPC: `TryCast<FieldNonPlayer>()` + `CanAction`

**Entity Translation:** Dictionary in `EntityTranslator.cs`; prefix `\d+[.:]` or `SC\d+:` stripped, re-attached to result

**Title Screen:** `InitializeTitle` captures text; `SystemIndicator.Hide` speaks; `SetEnableMainMenu(true)` clears states

**New Game Grid:** `characterIndex = cursorIndex / 2`, `isClassField = cursorIndex % 2 == 1` (indices 0-7 = 4 chars × name/class, 8+ = Done)

**Performance:** Static Vector3 directions (avoid allocs); IList\<Direction\> (avoid ToArray); single-pass lookups; O(1) reverse mapping; pre-allocated buffers (wallDirectionsBuffer, AudioEngine beacon scratch 32KB); early-return bitmask checks; wall-tone loop submits pre-generated sustain buffers (no per-tick synthesis)

---

## Known Limitations

- **F1 Walk/Run** — only affects dungeons/towns; world map uses fixed walk speed
- **Battle conditions** — preemptive/back attack disabled in FF1 (`BackAttackDiameter=0`, `PreeMptiveDiameter=2`)

---

## Version History

- **2026-06-18** — Audio backend moved from Windows waveOut to SDL3: new `AudioEngine` opens one SDL audio device with 7 bound `SDL_AudioStream`s and lets SDL mix; deleted `AudioChannel` (winmm), `ToneGenerator.MixWavFiles`/`GenerateMixedLoopTone`, `ScaleSamples`, and `SoundConstants.WaveFlags`. Volume + `1/sqrt(N)` headroom now via `SDL_SetAudioStreamGain`; wall-tone loops driven by per-direction stream top-up from `PlayWallTonesLooped`. `GamepadManager` switched to `SDL_InitSubSystem`/`SDL_QuitSubSystem(GAMEPAD)` (no blanket `SDL_Quit`). Removed dead `PlayWallTone`/`PlayWallTones`/`StopChannel`/`WallToneRequest`/`SoundChannel`. Fixes: keyboard/gamepad **remapping screen** was silent — `ConfigKeysSettingController.SelectContent` gained a 2nd overload in a game update, so the unqualified `AccessTools.Method` threw `AmbiguousMatchException` and the patch never attached; now disambiguated with the 5-arg `Type[]`. Remap rows read action + keyboard key name + the LIVE gamepad button (`keydata.GetGamePadKeyConfigtDictionary()[GameKey]` → `KeyCode` → SDL button → `ControllerLabels`; FFPR stores Confirm/Cancel JP-style so JB1→A/Cross, JB0→B/Circle). New: **Language menu** — row reads "Language: <current>" via a `Language→name` map off `MessageManager.currentLanguage`, and the dropdown announces each option via KeyInput `OptionController.SetDropDownItemFocus`, gated on `ConfigMenuState.IsActive` so it can't speak over the title "Press any button."
- **2026-06-13** — Fixes: mod-menu open/close announcements moved into `ModMenu.Open`/`Close` so every entry/exit path speaks consistently (F8, Escape, "Close Menu" item, controller Start/B) — F8 now says "Mod menu" on open (trimmed from "Mod Menu Open") and keyboard/menu-item closes now say "Mod menu closed". Canoe no longer appears in the entity scanner — `VehicleDetector`/`TransportDetector` skip `TransportTypes.CANOE` (it's an auto-used key item whose map object sits out of bounds).
- **2026-06-13** — Mod-menu toggle "Beacon Destination Announcement" (PreferencesManager `AnnounceOnBeaconRestart`, default off): when on, user-triggered beacon restarts re-announce the current target — entity via `RestartEntityBeacon`/`AnnounceEntityOnly`, waypoint via `WaypointController` + `FormatCurrentWaypoint`. `RestartBeacon` itself unchanged so background restarts stay silent.
- **2026-06-13** — 15-puzzle minigame support: arrowing over a tile speaks the number (or "empty"); on-demand position key (keyboard I / controller right-stick up) speaks row/column. New `PuzzlePatches` postfixes MiniGamePuzzleController movement methods; `PuzzleGameState` (PUZZLE_GAME registry key) gates the position key. One-time `[Puzzle] SNAPSHOT` log dumps board state to confirm pieceList ordering + index→number mapping at runtime.
- **2026-02-02** — Save slot speech now includes timestamp and matches visual order (slot name, date, character/level, location, playtime)
- **2026-01-31** — Scroll message line-by-line announcement (uses scrollTime parameter, timed coroutine speaks lines progressively); Battle defeat message fix (patch BattleCommandMessageController.SetMessage instead of LineFade; removed unused LineFadeClientPlay_Postfix)
- **2026-01-29** — EnsureFieldContext utility integration (MenuStateRegistry short-circuit, GameObjectCache.Refresh fallback, AnnouncementDeduplicator for "Not on map" spam prevention)
- **2026-01-28** — F1 walk/run fix, mod menu cleanup, shop descriptions via UI pointer chain, translation format fixes, wall tone/beacon toggle fixes, beacon first-load fix, performance fixes (static Vector3 direction constants, IList\<Direction\> to avoid ToArray(), config menu single-pass lookup, CoroutineManager O(1) reverse mapping)
- **2026-01-27** — Embedded 241 translations, FootEvent.stepOnTriggerList integration, duplicate map exit name fix
- **2026-01-26** — Teleport tile detection, load-game map announcement fix, translation prefix stripping, E wall tone 220Hz, NewGame grid encoding fix

---

## Command Workarounds

| Failed | Workaround |
|--------|------------|
| `cmd.exe //c build_and_deploy.bat` | `powershell.exe -Command "& 'path\build_and_deploy.bat'"` |
