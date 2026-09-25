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

**HitType:** -1=Non, 0=Hit, 1=Critical, 2=Miss, 3=Zero ("X: 0 damage"), 4=Recovery ("Recovered X HP"), 5=MPHit ("X MP damage"), 6=MPRecovery ("Recovered X MP"), 7=RecoveryCondition (never emitted by FF1's calc, so no branch; cures are announced by the status-removal hook, see Round 2)

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

**Title Screen:** the "Title" scene load arms the press prompt; the `SystemIndicator.Hide` postfix (called by `TitleWindowController.UpdateNone` right before it shows the prompt) reads it one frame later; `SetEnableMainMenu(true)` disarms and clears states (see Round 2)

**Field Menu (initial focus):** `FieldMenuPatches` postfixes `KeyInput.MainMenuController.Show(bool)` → gated coroutine reads `commandMenuController.selectCursor` (the non-Touch `Last.UI.CommandMenuController` carries the generic cursor; Touch variant does not) → reuses `MenuTextDiscovery.WaitAndReadCursor` (same reader navigation uses). Gate on `MenuManager.IsOpen` (checked one frame later) so it never reads during a map/asset load. Never read `MainMenuController.focusId` — that's the SELECTED, not focused, command (prior attempt removed for this)

**New Game Grid:** `characterIndex = cursorIndex / 2`, `isClassField = cursorIndex % 2 == 1` (indices 0-7 = 4 chars × name/class, 8+ = Done)

**Performance:** Static Vector3 directions (avoid allocs); IList\<Direction\> (avoid ToArray); single-pass lookups; O(1) reverse mapping; pre-allocated buffers (wallDirectionsBuffer, AudioEngine beacon scratch 32KB); early-return bitmask checks; wall-tone loop submits pre-generated sustain buffers (no per-tick synthesis)

---

## Open-issues pass (2026-09-23, session 2)

Not yet verified in game. **No new Harmony hooks** were added in this pass (so no new RVA-sharing risk); every change sits in existing hooks, key handlers or readers.

**Per-frame scene scan.** `InputManager.DetermineContext` runs every frame (for `ControllerRouter.Update`) and again on each key press. `IsOnValidMap(onDemand)`: on a `GameObjectCache` miss the per-frame caller now calls `GameObjectCache.Refresh<FieldPlayerController>()` (= `FindObjectOfType`) at most once per 30 frames (`FieldScanIntervalFrames`, `Time.frameCount`); the key-press caller refreshes immediately. Cost: after a scene load the field context (and `IsFieldActive`) can lag by ≤0.5 s until a scan or key press finds the new controller; `DelayedInitialScan` also refreshes it 0.5 s after each load.

**Tab (stuck-battle fallback).** `HandleTabKey` clears `IsInBattle` only when `BattleStateHelper.IsBattleControllerAlive()` is false: `FindObjectOfType<Il2CppLast.Battle.BattleController>()` (MonoBehaviour, TypeDefIndex 9653; `FindObjectOfType` skips inactive objects) plus `isActiveAndEnabled`. On-demand (Tab press) only. A lookup exception counts as "alive" so it can never clear a real battle. Silent either way; `[Battle] Tab:` log line says which branch ran.

**Controller vs keyboard state.**
- Unplug: `ControllerRouter.Update` used to `return` when `!GamepadManager.IsAvailable`, so a MOD_MODE left `SuppressGameInput` true forever (keyboard locked out of the game). Now `ReleaseControllerOwnedState()` runs in that branch: MOD_MODE → NORMAL; MOD_MENU → NORMAL only if `ModMenu.IsOpen` is false (an open mod menu stays open and keyboard-drivable, and its own `IsOpen` keeps the game suppressed as before). Also clears `consumedButtons`, `leftTriggerWasActive`, `wasLeftStickActive`.
- F8: `ModMenu.Open()` / `Close()` now call `ControllerRouter.SyncWithModMenu(bool)`: open → MOD_MENU (D-pad/stick navigate the menu, A toggles, B/Start close, LT/right stick no longer act underneath); close → NORMAL if it was MOD_MENU. Covers every path (F8, Escape, "Close Menu" item, Start, B) and works with no gamepad attached (the old reset lived only in `HandleModMenuState`, which never runs without a gamepad).

**Value-0 battle views — settled offline (capstone on GameAssembly.dll, scripts in the session scratch folder).**
- `BattleBasicFunction.CreateDamageView(data, value, hitType, isRecovery)` RVA 0xA2FBF0. Every caller (`CreateViewEntity` 0xA30030, `AfterViewDamage`, `CreateAttackViewEntity`, `CreateAfterViewEntity`, `CreateAfterAttackViewEntity`, `ViewEntity`, `BattleContinuousFunction`/`BattleFunction36.CreateViewEntity`) passes `value = ICalcResult.GetValue(false)` (interface slot 3) and `hitType = ICalcResult.GetHitType()` (slot 5) of the target's entry in the function's calc-result dictionary (field 0x10). `CalcResult.SetStatus` (0x360B60) stores the hitType unchanged; FF1's `SerialBattleUtility.CheckFunctionCalcToResult` (0x3818A0) only swaps in a Recovery result, never rewrites the type.
- The only `ICalcControllerProvider` is `CalcControllerProvider`: `GetFixedStatus(h)` → (0, h); `GetRecoveryCondition(id)` → (0, **Miss**); `GetAddConditionStatus(id)` → (0, `CalcExecuteFF1.AddConditionExection` 0x5692E0, which returns only Hit or Miss); `GetMagicStatus` → (damage, Hit) or Miss; `GetFightStatus` → `PhysicalExecution` 0x56F7B0 tuple (Hit / Critical / Miss); `GetUniqueStatus` → `UniqueExection` 0x573EC0 (Hit / Miss); recovery getters → Recovery / Miss.
- `AddConditionFunction.Calc` 0xA2B8B0 (all FF1 buffs/debuffs: FunctionType 9) → Miss when resisted, else `GetFixedStatus(Hit)` or `GetAddConditionStatus`. `RecoveryConditionFunction.Calc` 0x8564C0 (status cures, FunctionType 11) → target has the condition: `GetFixedStatus(Hit)`; otherwise `GetRecoveryCondition` → Miss.
- `HitType.Zero` is set in exactly two places: `DamageAggregater.CheckUndead` 0x578F42 (a Recovery result on a reverse-recovery target that computes 0; a non-zero one is **negated and keeps HitType Recovery**) and `MagicAbsorptionFunction.Calc` 0x8528F7 (0 MP drained). A global scan of all 37 interface `SetStatus` sites and 8 direct `CalcResult.SetStatus` sites found no constant 7 (`RecoveryCondition`) anywhere.
- Result: buffs/debuffs never carry Zero/RecoveryCondition, so per the shared spec Zero now speaks "{target}: 0 damage" (the normal damage key) and RecoveryCondition "{target}: cured" (unreachable in FF1, kept for parity — removed in Round 2). Value-0 **Hit** stays silent — it is both a landed buff (announced by the `BattleConditionController.Add` postfix) and an FF1 status cure, which therefore remains silent. Announcing cures would need a condition-removal hook, e.g. private `BattleConditionController.Remove(BattleUnitData, int, bool)` RVA 0x3D9E90 — not added (it would also fire on natural expiry and battle-end cleanup).
- Watch item: `CheckUndead` can produce (−N, Recovery) — healing an undead enemy would be read "X: Recovered -N HP". Unchanged; needs an in-game check (Cure/Heal on an undead enemy).

**H key in battle.** `AnnounceCharacterStatus` read `OwnedCharacterData.Parameter`, which the battle only writes back at the end (`BattleController.SaveParameter` 0x3E4760 → `FixedStatusInfo.SetCharacterParameter` 0xA3CBB0 → `OwnedCharacterData.SetParameter`). It now reads the live `BattleUnitDataInfo.Parameter` of the matching `BattlePlayerData` (`Il2CppLast.Battle.BattlePlugManager.Instance()` 0x410940 → `GetPlayerUnits()` 0x4102F0, matched on `ownedCharacterData.Id`) — the same source the target reader already uses for current HP — falling back to the owned parameter. **Reverted in Round 2:** both are the same object during battle (see Round 2, Task C).

**Localization sweep.** 147 new `T()` keys (all already present in `mod_text.json` at the end of the pass; `modtext_check ff1`: 0/0/0). Areas: battle damage/heal/miss/cured, "{0}'s turn", entity names and type names (chest open state, NPC/shop, exits, warp tiles, save points, event types, 37 dialogue-classifier object labels), path "No movement needed" + intercardinals, waypoint default name and counts, bestiary (summary, groups, formations, fallbacks), save slots (Autosave/Quicksave/File/Level/Time/Empty), character/status/item/magic HP-MP-Level labels, equipment slot fallbacks, controls screen (mouse buttons, Left Stick, "used for mod menu", assign prompts), controller button names (PlayStation shapes, D-pad directions, View/Menu/Minus/Plus/Home, Nintendo stick clicks, "Button {0}"), condition-name fallbacks (made literal so modtext_check sees them), "Game Over", "Press any button", puzzle "empty" / "Row {0}, Column {1}". Internal keys stay English: `EventEntity.EventTypeName` is still "ToLayer" etc. (`ToLayerFilter` matches it); only the spoken type goes through `EventEntity.LocalizeTypeName`. New-game job names now come from the game (`MasterManager.GetData<Job>(id).MesIdName` → `MessageManager`), English list only as fallback. English wording changes: layer entities now say "Layer transition" (was "ToLayer"); "No description available" → "No description"; status list "Lv. N" → "Level N".
Left in English on purpose: language names in `ConfigMenuReader` (match the game dropdown's style), hardware-printed button names (Start/Select/Back/Share/Options/Create/Guide, letters and L1/LB/LT…), map floor fallbacks "{n}F"/"B{n}", `MenuPosition`'s malformed-translation fallback, and the English job-name *matching* lists in `JobSelectionPatches` (matching, not speech). `SetMessage_Postfix` still decides interrupt by `Contains("defeated")` (English-only; speech itself is the game's text).

**BugReports.txt (FF1 list, March 2026) — status.**
1. Pathfinder entity count: already fixed — `EntityNavigationManager.FormatCurrentEntity` appends "({0} of {1})".
2. Enemy target not announced: already fixed — `BattleCommandPatches.SelectContent_Enemy_Postfix` (cursor moves) + `EnemysUpdate`/`EnemysInit` initial-focus read.
3. Character info not updating in battle: fixed now (H key live battle parameter, above). Target lists already read the battle parameter.
4. Pathfinder broken after a map transition unless used first: already addressed in code — `EntityScanner.EnsureCorrectMap` / `ScanEntities` track their own map id (set by the scene-load `DelayedInitialScan`), `RefreshEntitiesIfNeeded` force-rescans on a map change and refreshes the `FieldPlayerController` cache, `InputManager.IsOnValidMap` self-heals it. Needs an in-game confirmation.
6. H reads all characters: already fixed — `BattleCommandState.CurrentActor`.
7. Opened chests not updating: already fixed — `TreasureChestEntity.IsOpened` reads `CheckIfTreasureOpened` live on every announce (no rescan needed).

Multi-hit Damage now defaults to "With hit count", matching FF2–FF5 (coordinator commit 8328d4f): stored as the new `MultiHitDamage` MelonPreferences entry (default 1) instead of the old `DamageDisplay` entry, which every install had already written with the old "Total only" default, so everyone moves to the new default once. `PreferencesManager.DamageDisplay` falls back to 1.
Pre-existing, outside this pass: `TitleScreenPatches.WaitForPressPromptThenSpeak` polls `FindObjectOfType<TitleWindowController>` every 0.15 s during the title load, and `BattleCommandPatches` postfixes the per-frame `EnemysUpdate`/`PlayerUpdate` (one-shot gated) — both outside the four files CLAUDE.md allows to poll. (Both converted in Round 2.)

**Map-label translations (offline tools).** `tools/extract_entities.py` (UnityPy) sweeps every `map_*.bundle` in the game's Addressables folder: the Tiled `entity_default` and the entity assets in each map's `package`. It keeps the Japanese `PropertyEntity.Name` labels FF1 actually speaks. Object types come from the detector chain: Event, ToLayer, Entity, NPC, AnimEntity, ShopNPC and TelepoPoint are kept; GotoMap, TreasureBox, SavePoint, OpenTrigger, MinimapIcon, RelayInteraction and geometry are excluded. Each label goes through `ff1_translate()`, a step-for-step Python port of `EntityTranslator.Translate`.
- `missing <out.json>` lists only the labels the mod would fail to translate, keyed on the name the mod logs as untranslated.
- `gamedict <out.json>` builds Japanese → 11-language official names from the `system` and `story_cha` message tables.
- `selftest` checks the port against hand-traced cases; `check-official <gamedict.json>` lists entries that differ from an exact official string.
- `tools/apply_translations.py apply <batch.json> [--dry]` merges a reviewed batch `{jp: {lang: text}}` into `translation.json` without changing its format (2-space indent, CRLF, raw UTF-8, new keys appended). It rejects keys ending in digits or ①–⑳, which the lookup strips before the first try, and checks the result with a port of `ModTextTranslator.ParseNestedJson`.

Result of the sweep: 63 bundles, 430 spoken labels, 360 already covered. 65 keys were added (`translation.json` 119 → 184 entries), so 0 labels are missing. 38 existing entries (186 values) were aligned with the game's official names, including proper nouns inside longer labels: Sarda → Sadda, Smith → Smyth, English Fairy → Faerie, the fiend and orb names.

After a game update: run `python tools\extract_entities.py selftest`, then `missing %TEMP%\ff1_missing.json` and `gamedict %TEMP%\ff1_gamedict.json`. Translate each reported key, taking official names from the gamedict but checking context (shop strings are menu headers; speaker labels are bare names). Put them in a batch, run `apply_translations.py apply batch.json --dry`, then without `--dry`, and repeat `missing` until it reports 0. If `EntityTranslator.Translate` changes, update `ff1_translate()` and its selftest cases first. `EntityNames.json` and `FF1_translations.json` at the repo root are legacy; nothing loads them.

**mod_text.json completed.** 288 keys were added in all 12 languages: the 157 keys that were missing before this pass, plus the 131 new keys from the localization sweep above. `modtext_check ff1` now reports 512 entries, 461 keys used, and 0 missing. Game terms use FF1 PR's own wording, read from the game's `message_assets` tables. 92 existing values were aligned the same way, so old and new strings don't mix terms. That covered stats, HP/MP, Class, Name and status names; for example de Konstitution/Präzision, es Entereza/Puntería, and fr Sagesse/Vigueur. Chosen abbreviations:
- **ru:** HP = ОЗ (official). MP = "MP", because the official ОМ is read aloud as "ohm".
- **es:** HP = VIT (official).
- **de:** HP = LP (official).

The bare stat keys (Strength … Evasion, HP, MP, Level, Experience, Next Level) are no longer referenced by any `T()` call; the status screen uses the "Strength: {0}" forms. The main-menu command bar now uses a separate "Order" key with the game's official wording (MSG_SYSTEM_046: ならびかえ, Position, Formation, 隊形…). "Sort" stays for the item menu's Organize command. **Native-speaker check wanted:** th, ko, ru.

---

## Round 2 (2026-09-24)

Not yet verified in game. Settled offline with capstone on GameAssembly.dll (scripts in the session scratch folder). Every new hook's `// RVA:` occurs once in dump.cs.

### Status removal ("{unit}: {condition} removed")

- **Why not `BattleConditionController.Remove`.** `private void Remove(BattleUnitData, int id, bool isNegate)` is RVA **0x3DD1D0** (0x3D9E90 is `BattleEndRecoveryCondition`, the next method in dump.cs). In FF1 its only callers are `InterruptRemoveCondition(unit, ConditionType)` 0x3DC570 (Dispel-type uniques, `UpdateAlwaysEscape`, `ActSelectSpSwordSky`), `InterruptRemoveCondition(unit, int)` 0x3DC6D0 (Rampage) and `BattleEndRecoveryCondition` 0x3D9E90. Item/spell cures (`RecoveryConditionFunction.UpdateParameter` 0x856A30: `List<Condition>.Remove` on the target's `CurrentConditionList` 0x88), `Recovery(unit, ConditionUntilType)` 0x3DCAB0 (wear-off) and `Cancellation` 0x3DA300 edit the condition list directly and never call it.
- **Where every removal ends.** `BattlePlayController.BattleMainUpdate` → `UpdateStatusInPlayController` 0x40C850 → (when no act is staging) `CheckAddCondition()` 0x3DA700 → per unit `CheckConditionFunction` 0x3DAD70: counts `ConfirmedConditionList()` (virtual slot 31) vs the unit's `BattleConditionFunction` list (`BattleUnitDataInfo` +0x28), runs `ConflictCondition` for new ids (KO/Stone cancel other statuses here), then `RemoveConditionFunction` 0x3DCC70 and `AddConditionFunction` 0x3D8690 (→ `Add` 0x3D88F0, the existing "X: Poison" hook). `Remove` also tail-calls `CheckConditionFunction`. `RemoveConditionFunction` runs every frame (not hooked); for each id whose condition count fell below its function count it does `list.LastOrDefault(f => f.condition.Id == id)`, calls the function's `Remove()` (vtable slot 14) and removes it.
- **Hook:** postfix on that predicate, `BattleConditionController.<>c__DisplayClass45_0.<RemoveFunction>b__0(BattleConditionFunction f)` **0x3EC600** (48 bytes: `f.condition(+0x30).Id == this.id(+0x10)`). Its delegate is built only inside the removal loop (0x3DCF1B; `RemoveFunction` 0x3DD080 has no callers), so it runs only for real removals — never per frame. Found by reflection (nested type with a bool method containing "RemoveFunction" taking a `BattleConditionFunction`). `__result == true` marks the function being removed; unit = function +0x10, condition = function +0x30.
- **Symmetry with Add:** a function exists only if `Add` created it (and announced it), so a status negated/cancelled before the sync never produces "X: Poison removed". `isNegate` only matters inside `Remove` (the permanent/equipment-condition path) and is irrelevant downstream.
- **Silent when:** inside `BattleEndRecoveryCondition` (prefix/postfix flag; callers `BattleController.StartWinResult` and `EndEscapeFadeOut`; flag also cleared on battle start); `ConditionUtility.IsOutBattle(parameter)` 0x364750 is true, or the confirmed list still holds KO (ConditionType 5) — statuses cleared by KO/Stone (a revive removes KO first, so "KO removed" speaks); the id is still in the confirmed list (stacked copy); no name (hidden/internal); same (unit, id) already spoken this frame.
- **Wording:** `T("{0}: {1} removed")`, `interrupt: false`; unit name via `BattleMessagePatches.GetUnitName`, condition name via `BattleConditionController_Add_Patch.ResolveConditionName` — the same lookups the Add announcement uses (condition object → `MagicMenuState.GetConditionName`, then master data by id).
- **Removed:** the value-0 `RecoveryCondition` → "{0}: cured" branch and its mod_text key. FF1 had no `[Battle] value-0 view:` diagnostic line. `HitType.Zero` → "0 damage" is unchanged.
- **Fallback if it stays silent in game:** a prefix on `RemoveConditionFunction` 0x3DCC70 that diffs its two `Dictionary<int,int>` arguments would see the same removals, but it runs every frame for every unit, so it was not used.

### Per-frame and polling sweep

| File (before) | Kind | Action |
|---|---|---|
| TitleScreenPatches.cs `WaitForPressPromptThenSpeak` | 0.15 s `WaitForSeconds` poll of `FindObjectOfType<TitleWindowController>` | Converted: arm on the "Title" scene load; postfix on `SystemIndicator.Hide` 0x4FF4A0 (called by KeyInput `TitleWindowController.UpdateNone` 0x7D82A0 right before `SafeActiveSet(view.startParent, true)`; `InitNone` 0x7D60F0 hides it on every None entry) reads `startText` one frame later. Other Hide callers (`SceneTitleScreen.CreateInstance` +0x15B, field loads) find the prompt inactive and keep the arm. The old "never fired on return" came from arming on the boot-only `SplashController.InitializeTitle` (commit 3fe2e3a). |
| BattleCommandPatches.cs `EnemysUpdate`/`PlayerUpdate` postfixes | per-frame state update | Converted: read in `EnemysInit` 0x446160 / `PlayerInit` 0x448D80. `StateMachine.Change` 0xFE04A0 always runs exit+init; the target `UpdateController` 0x44CCC0 calls it when `nextState` differs. Init sets `selectCursor.Index` (+0xC0 → +0x18) from `prevEnemyIndex`/`prevPlayerIndex` and uses the target list, so the read normally succeeds in the postfix; a ≤10-frame retry covers a list not yet ready. `ShowWindow(false)` cancels a pending read. |
| CommandBarPatches.cs Item/Equip/Magic `UpdateController` postfixes | per-frame, index-change reader | Converted: `ItemCommandController.SetCommandSelectCursor` 0x49AB00, `EquipmentCommandController.SetCursor(int)` 0x471C60, `AbilityCommandController.SetCommandSelectCursor` 0x3762E0 (each called from `ResetCursor`, the move callback and a click) plus the existing command-state Inits (which now also read; command controller from window +0x38 / +0x38 / +0x48). Read one frame later (Equip's `EquipmentCommandIdCash` is refreshed by the bar's per-frame `UpdateFocus`), ≤10-frame retry while data is unset; index dedup kept. |
| ItemMenuPatches.cs `ItemListController`/`ItemUseController` `UpdateController` postfixes | per-frame consume of an Init-armed one-shot | Converted: the Init postfixes start a bounded read (≤30 tries, the old cap). No event exists for "list populated". |
| MagicMenuPatches.cs `AbilityContentListController.UpdateController` postfix | per-frame focus-state restore + `FindObjectOfType<AbilityWindowController>` + Init-armed consume | Converted: `UseListInit`/`ForgetInit` start a bounded routine (≤30 tries) that marks the spell list focused, reads the character from the window's own status controller (+0x50 → +0x48) and list controller (+0x58), and speaks the focused spell. |
| PopupPatches.cs `CommonPopup.UpdateFocus`, `GameOverSelectPopup.UpdateFocus`, `GameOverLoadPopup.UpdateCommand` | per-frame (UpdateFocus runs at the top of every `UpdateSelect` 0x72B010) | Converted: `SetCommandSelectCursor` of each popup — 0x72A700 (Open, move callback `<UpdateSelect>b__2/b__4`, click), 0x48E030, 0x6EDF30. `CommonPopup.Open` calls it after base `Popup.Open`, whose postfix sets the message-first suppression flag, so the open order is unchanged. Game-over readers store the index only once the button text is set, with a ≤3-frame retry. |
| BestiaryPatches.cs `LibraryMenuController.UpdateController` postfix | per-frame `selectState` watcher | Converted: `ChangeState` 0x719910 is inlined (no callers); its copies call `SetupEnlargedMapKeyHelp` 0x719E00 (→ map) / `SetupKeyHelp` 0x71A170 (→ list) just before writing `selectState` (+0x44), so their postfixes compare the state one frame later. |
| BestiaryPatches.cs `AnnounceFormation` | 3 s `FindObjectOfType<ArBattleTopController>` poll | Converted: one check a frame after `ChangeState(5)`, else the `ArBattleTopController.InitMonsterPartyList` 0x336AB0 postfix (called from its `SetActive`) reads the formation. The "Formation view" timeout fallback is gone. |
| GalleryPatches.cs / MusicPlayerPatches.cs entry item | 2 s `Time.deltaTime` poll for the cached focus | Converted: the header coroutine reads the cached focus if `SetFocusContent`/`SetFocus` already reported it; otherwise that postfix reads it when it does. |
| Utils/AnnouncementHelper.cs `DelayedSpeakSeconds` | `WaitForSeconds` helper | Removed (unused). |
| ConfigMenuPatches.cs `ConfigController.UpdateController` postfix | per-frame consume of a re-announce armed after a Quit/Title popup or the bestiary | Converted (follow-up). Popup close: `PopupClose` starts the read at once (the config menu is live again). Bestiary: `MenuExtraLibraryUi.GotoMenu` passes `FieldMap.CreateReturnMonsterLibraryArguemtns` 0x2F4840 ("MenuReturnMonsterLibrary"); `FieldMap.InitMenu` 0x2F5FC0 (Menu-state entry, from `StateInit`) takes its library-return branch — `FadeManager.FadeIn` with the completion callback `FieldMap.<InitMenu>b__92_0` **0x2FA9C0** (unique; built only in that branch; switches FieldMap's menu state machine to ViewMenu = 1; the MenuState machine has no Init methods). The library exit (`SubSceneManagerMainGame.ChangeState` leaving states 19/20) arms; the callback's postfix reads the focused row. Each read: at most 10 frames, one `FindObjectOfType<ConfigController>` per try; cancelled on config `SetActive(false)`. |
| Core/Handlers/GameToggleAnnouncer.cs `Poll()` (from InputManager.Update) | per-frame read of encounter / auto-dash flags | Converted (follow-up). Hooks the CLIENT setters, which are separate, unique bodies: `CheatSettingsClient.SetIsEnableEncount(bool)` **0x6D3BC0** and `ConfigClient.SetIsAutoDash(int)` **0x9AD550** (the data setter `CheatSettingsData.set_IsEnableEncount` 0x392230 is shared by 22 bodies). Callers: `FieldMap.UpdatePlayerStatePlay` 0x2FB220 (the field toggle keys: `GetKeyDown` → `SetIsAutoDash` at +0x6F7, `SetIsEnableEncount` at +0x9B3), `ConfigActualDetailsControllerBase` (KeyInput/Touch config menu) and `SaveSlotManager.<GotoLoadSaveData>` (save load). Prefix records the old value (`UserDataManager.CheatSettingsData.IsEnableEncount` / `Config.IsAutoDash`); postfix speaks only when `__0` differs AND `SubSceneManagerMainGame.GetCurrentState()` 0x7B6A80 == Player (3). `FieldMap.UpdatePlayer` 0x2FBE80 (the Player state's update) is the only path into the player state machine and `UpdatePlayerStatePlay`; `UpdateTransport` does not run it; the config menu runs in state Menu (5). The manager instance is cached from the existing `SubSceneManagerMainGame.ChangeState` postfix (`__instance`) and dropped on the "Title" scene load. `InputManager.Update` no longer calls the poll; its scene-load `Reset()` is gone. |
| FFI_ScreenReaderMod.cs `DelayedInitialScan` | `WaitForSeconds(0.5)` after scene load | Kept: map-transition/entity-scan core; the scanner also self-heals on key press (`EnsureCorrectMap`). |
| ModMenu.cs, TextInputWindow.cs, ConfirmationDialog.cs | `WaitForSeconds` 0.1–0.3 s | Kept: speech pacing inside the mod's own UI; no game event exists. |
| BattleResultPatches.cs `MonitorExpCounterAnimation` | 0.1 s poll | Kept: EXP-counter audio loop (allowed core). |
| ScrollMessagePatches.cs timed line speaking | `Time.time` pacing | Kept: pacing matched to the scroll speed (content timing, not state polling). |
| InputPassthroughPatches.cs `GetKey*`/`GetAnyKey`; MapTransitionPatches.cs; MovementSoundPatches.cs; AudioLoopManager | per-frame | Kept: input / map-transition / audio core (CLAUDE.md allowed files). |
| NewGamePatches.cs `CharacterContentListController.UpdateView` | — | Not per-frame: called from `InitSelect` and the random-name key branch of `UpdateSelect`. |
| VehicleLandingPatches.cs `MapUIManager.SwitchLandable` | — | Not per-frame: called from `FieldController.UpdateStateSwitchLandable` (per step and on script end). |
| InputManager.cs `IsOnValidMap` rescan | throttled `FindObjectOfType` (≤ once per 30 frames off-field) | Kept: controller-router context (allowed file); from session 2. |

### Double-fix audit (commit ad44a13)

- **H key:** `BattleStatusControl.CreateBattlePlayerData` 0x8649E0 writes `BattleUnitDataInfo.Parameter = ownedCharacterData.parameter` (field 0x78, same object; `get_Parameter` 0x2C85F0 is `mov rax,[rcx+0x78]`). `CurrentActor` is that same `OwnedCharacterData` (`BattleInfomationController.SetCommandSelected` 0x3CB3F0 passes `selectedPlayer(+0x88).ownedCharacterData(+0x38)` to `SetCommandData`). `OwnedCharacterData.SetParameter` 0x3B8E60 (the only writer that replaces field 0x78, from `SetCharacterParameter`/`SaveLoseCharacterParameter`) runs at battle end. So the old read was already live; `FindBattleParameter` was removed and the H key reads `charData.Parameter` again.
- **Mod menu controller state:** `ControllerRouter.OpenModMenu`/`CloseModMenu` set the state and then called `ModMenu.Open/Close`, which now call `SyncWithModMenu` — the same write twice. The explicit writes were removed; `HandleModMenuState` still self-heals a closed menu.
- No overlap found in the other ad44a13 changes (Tab guard, per-frame scan throttle, unplug release, localization, value-0 Zero, job names, condition fallbacks).

### In-game checks for the two follow-up conversions

1. Field toggles: on the field, press the walk/run key (F1 / L3) twice → "Run", then "Walk" (the current state each time). Press the encounter toggle (R3 / keyboard binding) twice → "Encounters off", then "Encounters on". Inside a town and on the world map both.
2. Same toggles in the config menu (Auto Dash, and Encounters if the config/cheat menu lists it) → only the config row is read ("Auto Dash: On"); no extra "Run"/"Walk" or "Encounters …".
3. Load a save whose Auto Dash / Encounters differ from the current session → no "Run"/"Walk"/"Encounters …" during the load.
4. Config menu → Return to Title (or Quit) → cancel → the focused config row is read again ("Return to Title (n of N)" or its row name).
5. Config menu → Bestiary → back out to the config menu → after the fade-in, the focused config row is read once.
6. If 5 stays silent, check the log for "FieldMap InitMenu fade-in callback not found". The menu entry / back-out re-reads were not changed by ad44a13; the Round 2 conversions keep their dedups (command-bar index, popup index, `_lastSpellAnnouncement`).

## L3+R3 chord (2026-09-25)

Not yet verified in game. Pressing both stick clicks on the field toggles Stick Click Normalization, whichever way it is set, so the player can switch between the mod functions and the game functions without opening the mod menu.

- **`ControllerRouter.UpdateStickClicks`**, run every frame right after `HandleStateTransitions`, replaces the old L3/R3 block in `HandleNormalField`. A click that starts in NORMAL on the active field (not during the 15-puzzle) is tracked in `stickClickButton`. Both stick buttons are consumed from that press until both are up.
- **Both held** → `ToggleStickClickNormalization` once per press (`stickChordFired`). Releasing afterwards does nothing.
- **A lone click resolves on release.** With normalization off, L3 toggles beacon navigation and R3 the pathfinding filter, as before but on release instead of press. With it on, the router hands the game a synthetic press: `IsStickPulseDown` for one frame (`GetKeyDown` and `GetKey`), then `IsStickPulseUp` for one frame (`GetKeyUp`). `InputPassthroughPatches.GetSDLKey*` OR these in for `ACTION_STICK_L`/`ACTION_STICK_R` through `StickButtonFor`. Without the deferral the first click of a chord would already toggle encounters or walk/run in the game.
- A tracked click acts only if the player is still on the field in NORMAL when it resolves: Back (mod mode) or a menu or battle opening mid-press cancels it. Mod mode keeps its own Back + L3/R3 (normalization on). Off the field, stick clicks go to the game unchanged.
- `ReleaseControllerOwnedState` (controller unplugged) clears the tracked click and any pending pulse.

In-game checks:
1. Field, normalization off: L3 → "Beacon navigation on/off"; R3 → "Pathfinding filter on/off", each on release.
2. L3 + R3 together → "Stick click normalization on". Beacons and the filter do not change. Again → "… off".
3. Normalization on: L3 alone and R3 alone still toggle the game's encounters and walk/run (one toggle per click, spoken by the F1/F3 announcements).
4. Normalization on: L3 + R3 → "Stick click normalization off", with no encounter or walk/run toggle.
5. Back, then L3 or R3 with normalization on → the mod-mode toggles still work.

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
