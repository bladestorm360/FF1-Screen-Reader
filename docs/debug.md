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

### Battle Controllers
| Controller | Field | Offset |
|------------|-------|--------|
| BattleAbilityInfomationControllerBase | stateMachine | 0x28 |
| BattleAbilityInfomationControllerBase | selectedBattlePlayerData | 0x30 |
| BattleAbilityInfomationControllerBase | dataList | 0x70 |
| BattleAbilityInfomationControllerBase | contentList | 0x78 |
| BattleUnitData | BattleUnitDataInfo | 0x28 |
| BattleUnitDataInfo | Parameter | 0x10 |

### Shop
| Controller | Field | Offset |
|------------|-------|--------|
| ShopMagicTargetSelectController | isFoundEquipSlot | 0x70 |

### Battle Pause (FF1-specific offset differs from FF3!)
| Controller | Field | Offset | Notes |
|------------|-------|--------|-------|
| BattleUIManager | pauseController | 0x98 | **FF1=0x98, FF3=0x90** |
| BattlePauseController | isActivePauseMenu | 0x71 | Same as FF3 |

### Popups (KeyInput namespace)
| Type | Field | Offset |
|------|-------|--------|
| CommonPopup | titleText (IconTextView) | 0x38 |
| CommonPopup | messageText (Text) | 0x40 |
| CommonPopup | selectCursor | 0x68 |
| CommonPopup | commandList | 0x70 |
| ChangeMagicStonePopup | nameText | 0x28 |
| ChangeMagicStonePopup | descriptionText | 0x30 |
| ChangeMagicStonePopup | commandList | 0x58 |
| GameOverSelectPopup | commandList | 0x40 |
| InfomationPopup | titleText (IconTextView) | 0x28 |
| InfomationPopup | messageText | 0x30 |
| SavePopup | messageText | 0x40 |
| SavePopup | commandList | 0x60 |
| IconTextView | nameText | 0x20 |
| CommonCommand | text | 0x18 |

### Save/Load Controllers
| Controller | Field | Offset |
|------------|-------|--------|
| LoadGameWindowController | savePopup | 0x58 |
| LoadWindowController | savePopup | 0x28 |
| SaveWindowController | savePopup | 0x28 |
| InterruptionWindowController | savePopup | 0x38 |

### New Game (Serial.FF1.UI.KeyInput namespace)
| Controller | Field | Offset |
|------------|-------|--------|
| NewGameWindowController | stateMachine | 0x28 |
| NewGameWindowController | newGamePopup | 0xD0 |
| NewGameWindowController | autoNameIndex | 0x100 |
| NewGamePopup | messageText | 0x30 |
| NewGamePopup | commandList | 0x40 |

---

## Enum Values

### State Machines
**EquipmentWindowController:** STATE_COMMAND = 1
**AbilityWindowController:** STATE_NONE = 0, STATE_COMMAND = 4
**ItemWindowController:** STATE_COMMAND_SELECT = 1, STATE_USE_SELECT = 2, STATE_IMPORTANT_SELECT = 3, STATE_ORGANIZE_SELECT = 4, STATE_TARGET_SELECT = 5
**ShopController:** STATE_SELECT_COMMAND = 1, STATE_SELECT_PRODUCT = 2, STATE_SELECT_SELL_ITEM = 3

### HitType
| Value | Type | Announcement |
|-------|------|--------------|
| 2 | Miss | "Miss" |
| 4 | Recovery | "Recovered X HP" |
| 5 | MPHit | "X MP damage" |
| 6 | MPRecovery | "Recovered X MP" |

### ConditionType
| Value | Condition |
|-------|-----------|
| 5 | UnableFight (KO) |
| 6 | Silence |
| 7 | Sleep |
| 8 | Paralysis |
| 9 | Blind |
| 10 | Poison |
| 11 | Mineralization (Stone) |
| 12 | Confusion |

### TransportationType
| Value | Type |
|-------|------|
| 1 | Player (walk/disembark) |
| 2 | Ship |
| 3 | Airship |
| 5 | Canoe |
| 7 | LowFlying |

### Battle Command Indices
Attack (0), Magic (1), Items (2), Defend/Run (3)

---

## State Classes

Each menu has a state class with `ShouldSuppress()` to prevent double announcements:

| Class | Purpose |
|-------|---------|
| ConfigMenuState | Config menu |
| ItemMenuState | Item menu + targets |
| EquipMenuState | Equipment |
| MagicMenuState | Spells + targets |
| StatusMenuState | Character status |
| ShopMenuTracker | Shop lists |
| BattleCommandState | Battle commands |
| BattleTargetState | Enemy/ally targeting |
| BattleItemMenuState | Battle items |
| BattleMagicMenuState | Battle magic |
| PopupState | Confirmation popups + button reading |
| SaveLoadMenuState | Save/load confirmations |
| BattlePauseState | Battle pause detection (memory read) |
| MenuStateRegistry | Centralized state tracking |

---

## Architecture Patterns

### Menu State Clearing
All menu controllers need `SetActive(false)` postfix that clears state unconditionally. Do NOT check `menuManager.IsOpen`.

### Battle State
- Set `IsInBattle = true` in BattleStartPatches; clear on victory/defeat/escape
- Guard patches with `if (!IsInBattle) return;`

### Dialogue Page Reading
- `newPageLineList` contains END indices (inclusive). Convert: `[0, 2]` → pages start at lines `[0, 1, 3]`
- Patch both `PlayingInit` (all pages) and `NewPageInputWaitInit` (intermediate)

### Stale Callback Prevention
Track `LastSelectedCommandIndex` in BattleCommandState. Magic/item patches check this to block stale callbacks from close animations.

### Config Menu Value Changes
- `SetFocus` handles navigation (up/down) → announces "Setting: Value"
- `SwitchArrowSelectTypeProcess` / `SwitchSliderTypeProcess` handle toggles/sliders (left/right) → announces just the new value
- Do NOT duplicate value-change logic in SetFocus

### Popup Handling
- `PopupState` tracks active popup pointer and commandList offset
- Base `Popup.Open()` postfix uses `TryCast<T>()` for IL2CPP-safe type detection
- 1-frame coroutine delay before reading text (lets UI populate)
- `PopupState.ShouldSuppress()` returns true when popup has buttons (suppresses MenuTextDiscovery)
- Button reading via `CommonPopup.UpdateFocus` postfix in PopupPatches
- `CursorNavigation_Postfix` calls `PopupPatches.ReadCurrentButton()` when popup/save-load is active

### Battle Pause Menu Detection
- Cursor path contains "curosr_parent" (game typo) when in pause menu — build full 3-level path
- Check BEFORE `BattleCommandState.ShouldSuppress()` (still "in battle" during pause)
- When detected, read via `MenuTextDiscovery.WaitAndReadCursor()` directly
- `BattlePauseState.IsActive` reads memory directly (no patches needed)

### Map Transition Detection
`MapTransitionPatches` hooks `FieldMapSequenceController.ChangeState()` and checks map ID on field-related states (`ChangeMap`, `FieldReady`, `Player`). Announces "Entering {MapName}" whenever `currentMapId != lastAnnouncedMapId`, including on first run (load-game from title screen). No first-run skip — title screen uses different states so field states only fire on actual player-driven transitions.

`LocationMessageTracker` prevents duplicate announcements: after "Entering Cornelia 1F" is announced, the bare "Cornelia 1F" from `FadeMessageManager.Play` is suppressed via content-based matching. Also suppresses location-like text (1-4 words, no punctuation) when no map transition is active (e.g., opening the menu).

### Map Transition Fade Detection
`MapTransitionPatches` suppresses wall tones during screen fades by polling `FadeManager.IsFadeFinish()` via cached reflection. No Harmony patches on FadeManager (avoids IL2CPP trampoline issues with Nullable params).

| Method | When True | Meaning |
|--------|-----------|---------|
| `IsStateFadeOut()` | During fade-to-black | Screen going dark |
| `IsStateFadeIn()` | Normal visible gameplay | Screen is "faded in" |
| `IsFadeFinish()` | No fade active | Normal idle state |

**Key:** Check `!IsFadeFinish()` — `false` during normal play (tones play), `true` during fades (tones suppressed). Do NOT use `IsStateFadeIn()` (true during normal play).

### Entity Scanner Filter Order
In `EntityScanner.ConvertToNavigableEntity()`:
1. Player filter — skip FieldPlayer entities
2. VehicleTypeMap check — BEFORE residentchara filter (vehicles use ResidentCharaEntity GameObjects)
3. Residentchara filter — skip party followers
4. Visual effects filter — skip non-interactive elements
5. Inactive filter — skip inactive objects
6. Type-specific detection (exits, treasures, NPCs, save points, fallback vehicle layers)

### Entity Name Resolution
All entity types try `GetEntityNameFromProperty()` first for localized names. It accesses `PropertyEntity.Name` and resolves message IDs via `MessageManager.GetMessage()`. Falls back to GameObject-based naming (`CleanObjectName`, `GetInteractiveObjectName`, etc.) only if property name is empty.

### NPC Detection
Uses `TryCast<FieldNonPlayer>()` for type-based validation (not string matching). `CanAction` check filters non-interactive NPCs. Names resolved via `GetEntityNameFromProperty()`.

### Entity Translation System
Translates Japanese entity names to English using `UserData/FFI_ScreenReader/FF1_translations.json`.
- `EntityTranslator.Initialize()` loads translations from JSON at startup
- `EntityScanner.ConvertToNavigableEntity()` calls `EntityTranslator.Translate(name)` after name resolution
- **Prefix stripping:** Names with `\d+:` or `SC\d+:` prefixes (e.g., `6:村人(おじいさん)`) are stripped before lookup. The base Japanese text is matched against translations, and the prefix is re-attached to the result (e.g., `6: Old Man`). Exact matches are tried first for backward compatibility.
- Untranslated base names (prefix-stripped, deduplicated) tracked per map in `untranslatedNamesByMap`
- Hotkey `'` dumps untranslated base names to `EntityNames.json` for manual translation
- JSON format: `{"Japanese": "English"}` key-value pairs — use base names without numeric prefixes as keys

### Title Screen ("Press Any Button")
- `SplashController.InitializeTitle` captures text silently, sets `isTitleScreenTextPending` flag
- `SystemIndicator.Hide` speaks stored text when loading indicator hidden
- `TitleMenuCommandController.SetEnableMainMenu(true)` clears all states when title menu activates

### Save/Load Popup Flow
- Controllers call `SetPopupActive(bool)` or `SetEnablePopup(bool)`
- Postfix sets `SaveLoadMenuState` and `PopupState` for button navigation
- 2-frame coroutine delay reads `SavePopup.messageText`; state cleared on popup close

### New Game Character Creation (FF1-specific)
- Uses `Serial.FF1.UI.KeyInput.NewGameWindowController` (not Last namespace)
- `CharacterContentListController` and `NameContentListController` are in `Last.UI.KeyInput`
- `SetTargetSelectContent(int)` fires on slot navigation; `SetFocus(int)` fires on name cycling; `UpdateView` detects name changes
- Track `lastTargetIndex` and `lastSlotNames[]` for change detection

**Grid encoding (character-major):** 2-column × 5-row grid, `flat index = characterIndex * 2 + commandIndex`

| Index | Field | Character |
|-------|-------|-----------|
| 0 | LW1 Name | char=0, cmd=0 |
| 1 | LW1 Class | char=0, cmd=1 |
| 2 | LW2 Name | char=1, cmd=0 |
| 3 | LW2 Class | char=1, cmd=1 |
| 4 | LW3 Name | char=2, cmd=0 |
| 5 | LW3 Class | char=2, cmd=1 |
| 6 | LW4 Name | char=3, cmd=0 |
| 7 | LW4 Class | char=3, cmd=1 |
| 8+ | Done | — |

**DecodeGridIndex:** `characterIndex = cursorIndex / 2`, `isClassField = cursorIndex % 2 == 1`

**Navigation:** DOWN stays in column (step by 2); RIGHT wraps within row pair. Game navigates column-first: LW1 Name → LW1 Class → LW3 Name → LW3 Class → Done.

**Data structures:** `ContentList` and `Characterstatuses` both have 4 items indexed by `characterIndex` (not cursor index). Job lookup uses `Characterstatuses[charIdx].JobId` or `ContentList[charIdx].Data.CharacterStatusId` → MasterManager.

---

## Known Limitations

**Accuracy/evasion stat gains** - Not hooked due to complexity. All other stats announce correctly on level up.

---

## Version History

- **2026-01-26** - Fixed load-game not announcing map name: removed first-run silent-store branch in `CheckMapTransition()`. Field states only fire on player-driven transitions, so the skip was unnecessary. `MapTransitionPatches.cs`
- **2026-01-26** - Entity translation prefix stripping: `\d+:` and `SC\d+:` prefixes stripped before lookup, re-attached to translated output. Base names deduplicated in untranslated tracking. `EntityTranslator.cs`
- **2026-01-26** - East wall tone frequency raised to 220Hz (from 200Hz) for pitch-based L/R distinction. `SoundPlayer.cs`
- **2026-01-26** - Fixed NewGame grid navigation: changed DecodeGridIndex from row-major to character-major encoding (`cursorIndex / 2`). `NewGamePatches.cs`
- **2026-01-25** - Fixed inverted FadeManager check: replaced `IsStateFadeOut() || IsStateFadeIn()` with `!IsFadeFinish()`. `MapTransitionPatches.cs`
- **2026-01-25** - Collision sound: require 2+ consecutive hits at same position, increased cooldown to 300ms. `MovementSoundPatches.cs`
- **2026-01-25** - Wall bump sound finalized: 55Hz, 60ms, 0.27f volume. `SoundPlayer.cs`
- **2026-01-25** - Entity translation system: JSON dictionary for Japanese→English names, `'` hotkey dumps untranslated. `EntityTranslator.cs`, `EntityScanner.cs`
- **2026-01-24** - Interactive object name resolution: prioritize `GetEntityNameFromProperty()` for localized names. `EntityScanner.cs`
- **2026-01-24** - NPC detection: replaced string matching with `TryCast<FieldNonPlayer>()` + `CanAction` check. `EntityScanner.cs`
- **2026-01-24** - Vehicle scanning: moved VehicleTypeMap check before residentchara filter. `EntityScanner.cs`
- **2026-01-24** - Popup architecture: removed CharacterCreationPatches, moved UpdateFocus to PopupPatches, added save/load button reading, fixed battle pause cursor path. Multiple files.
- **2026-01-23** - Popup reading port from FF3: PopupPatches, BattlePausePatches, SaveLoadPatches, NewGamePatches, MenuStateRegistry, title screen reading.
- **2026-01-23** - Config menu: removed duplicate value-change from SetFocus; BGM/SFX sliders use raw values.
- **2026-01-23** - Removed Time.time: entity scan now event-driven, all ShouldAnnounce() uses string-only deduplication (8 files).
- **2026-01-15** - Performance: replaced assembly scanning with typeof(), removed debug logging, added entityMap cache, V key vehicle state, stale callback prevention.

---

## Command Workarounds

| Failed Command | Workaround |
|----------------|------------|
| `cmd.exe //c build_and_deploy.bat` | Use PowerShell: `powershell.exe -Command "& 'path\build_and_deploy.bat'"` |
| `sed -n 'X,Yp' file` | Use `grep -n` or Read tool |
