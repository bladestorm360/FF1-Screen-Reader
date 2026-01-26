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

## Architecture Patterns

### Menu State Clearing
All menu controllers need `SetActive(false)` postfix that clears state unconditionally. Do NOT check `menuManager.IsOpen`.

### Battle State
- Set `IsInBattle = true` in BattleStartPatches
- Clear on victory/defeat/escape
- Guard patches with `if (!IsInBattle) return;`

### Dialogue Page Reading
- `newPageLineList` contains END indices (inclusive)
- Convert: `[0, 2]` → pages start at lines `[0, 1, 3]`
- Patch both `PlayingInit` (all pages) and `NewPageInputWaitInit` (intermediate)

### Stale Callback Prevention
Track `LastSelectedCommandIndex` in BattleCommandState. Magic/item patches check this to block stale callbacks from close animations.

### Config Menu Value Changes
- `SetFocus` patch handles navigation (up/down) → announces "Setting: Value"
- `SwitchArrowSelectTypeProcess` handles toggles (left/right) → announces just the new value
- `SwitchSliderTypeProcess` handles sliders (left/right) → announces just the new value
- Do NOT duplicate value-change logic in SetFocus

### Popup Handling
- `PopupState` tracks active popup pointer and commandList offset
- Base `Popup.Open()` postfix uses `TryCast<T>()` for IL2CPP-safe type detection
- 1-frame coroutine delay before reading text (lets UI populate)
- `PopupState.ShouldSuppress()` returns true when popup has buttons (suppresses MenuTextDiscovery)
- Button reading via `CommonPopup.UpdateFocus` postfix in PopupPatches (not BattlePausePatches)
- `CursorNavigation_Postfix` calls `PopupPatches.ReadCurrentButton()` when popup/save-load is active

### Battle Pause Menu Detection
- Cursor path contains "curosr_parent" (game typo) when in pause menu
- Build full path: grandparent/parent/cursor (3 levels)
- Check BEFORE `BattleCommandState.ShouldSuppress()` (still "in battle" during pause)
- When detected, read via `MenuTextDiscovery.WaitAndReadCursor()` directly
- `BattlePauseState.IsActive` reads memory directly (no patches needed for state)

### Map Transition Fade Detection
`MapTransitionPatches` suppresses wall tones during screen fades (map transitions) by polling `FadeManager.IsFadeFinish()` via cached reflection.

**FadeManager has three mutually exclusive states:**
| Method | When True | Meaning |
|--------|-----------|---------|
| `IsStateFadeOut()` | During fade-to-black animation | Screen going dark |
| `IsStateFadeIn()` | Normal visible gameplay | Screen is "faded in" (visible) |
| `IsFadeFinish()` | Fade completed / no fade active | Normal idle state |

**Key insight:** `IsStateFadeIn()` returns `true` during normal gameplay (screen IS "faded in"), so checking it suppresses tones during normal play. The correct check is `!IsFadeFinish()` — returns `false` during normal gameplay (tones play) and `true` during any fade animation (tones suppressed).

**Implementation:** No Harmony patches on FadeManager (avoids IL2CPP trampoline issues with Nullable params). Uses cached reflection to find `IsFadeFinish` method at init, then polls via `IsScreenFading` property getter.

### Entity Scanner Filter Order
In `EntityScanner.ConvertToNavigableEntity()`, filter order is critical:
1. **Player filter** - Skip FieldPlayer entities
2. **VehicleTypeMap check** - Must come BEFORE residentchara filter (vehicles use ResidentCharaEntity GameObjects)
3. **Residentchara filter** - Skip party followers (comes AFTER vehicle check)
4. **Visual effects filter** - Skip non-interactive elements
5. **Inactive filter** - Skip inactive objects
6. Then type-specific detection (map exits, treasures, NPCs, save points, fallback vehicle layers)

### Entity Name Resolution
All entity types should try `GetEntityNameFromProperty()` first for localized names:
```csharp
string name = GetEntityNameFromProperty(fieldEntity);
if (string.IsNullOrEmpty(name))
    name = FallbackMethod(goName); // CleanObjectName, GetInteractiveObjectName, etc.
```
**How it works:**
- Accesses `PropertyEntity.Name` from the field entity
- If name looks like a message ID, resolves via `MessageManager.GetMessage()`
- Returns properly localized names (including Japanese)
- Falls back to GameObject-based naming only if property name is empty

### NPC Detection (type-based)
NPCs use `TryCast<FieldNonPlayer>()` for proper type validation:
```csharp
var fieldNonPlayer = fieldEntity.TryCast<FieldNonPlayer>();
if (fieldNonPlayer != null)
{
    if (!fieldNonPlayer.CanAction)
        return null; // Skip non-interactive NPCs
    string npcName = GetEntityNameFromProperty(fieldEntity);
    // ...
}
```
**Key points:**
- **Type-based detection** - Only actual `FieldNonPlayer` types are detected (not string matching "npc" or "chara")
- **CanAction check** - Filters out NPCs without dialogue (collision-only entities)
- **GetEntityNameFromProperty()** - Uses `PropertyEntity.Name` for localized names, falls back to MessageManager for message IDs

### Entity Translation System
Translates Japanese entity names to English using a JSON dictionary file.

**Files:**
- **Translation file:** `UserData/FFI_ScreenReader/FF1_translations.json`
- **Untranslated dump:** `UserData/FFI_ScreenReader/EntityNames.json` (generated)

**How it works:**
1. `EntityTranslator.Initialize()` loads translations from JSON at startup
2. `EntityScanner.ConvertToNavigableEntity()` calls `EntityTranslator.Translate(name)` after resolving entity names
3. If translation exists, returns English; otherwise returns original name
4. Japanese names without translations are tracked in `untranslatedNames` HashSet

**Integration point in EntityScanner:**
```csharp
string name = GetEntityNameFromProperty(fieldEntity);
name = EntityTranslator.Translate(name); // Apply translation
```

**Hotkey (' apostrophe):**
- Calls `EntityTranslator.DumpUntranslatedNames()`
- Writes all encountered Japanese names (without translations) to `EntityNames.json`
- Output format is ready for manual translation:
  ```json
  {
    "コウモリ": "",
    "妖精王": ""
  }
  ```

**JSON format:** Simple `{"Japanese": "English"}` key-value pairs. Parser handles escaped quotes.

### Title Screen ("Press Any Button")
- `SplashController.InitializeTitle` captures text silently, sets `isTitleScreenTextPending` flag
- `SystemIndicator.Hide` speaks stored text when loading indicator hidden
- `TitleMenuCommandController.SetEnableMainMenu(true)` clears all states when title menu activates
- Guard flag prevents false triggers from non-title loading sequences

### Save/Load Popup Flow
- Controllers call `SetPopupActive(bool)` or `SetEnablePopup(bool)`
- Postfix sets `SaveLoadMenuState` and `PopupState` for button navigation
- 2-frame coroutine delay reads `SavePopup.messageText`
- State cleared on popup close

### New Game Character Creation (FF1-specific)
- Uses `Serial.FF1.UI.KeyInput.NewGameWindowController` (not Last namespace)
- `CharacterContentListController` and `NameContentListController` are in `Last.UI.KeyInput`
- `SetTargetSelectContent(int)` fires on slot navigation (event-driven)
- `SetFocus(int)` fires on name cycling (event-driven)
- `UpdateView` detects name changes after assignment
- Track `lastTargetIndex` and `lastSlotNames[]` for change detection

---

## Known Limitations

**Accuracy/evasion stat gains** - Not hooked due to complexity. All other stats (HP, Str, etc.) announce correctly on level up.

---

## Version History

### 2026-01-25 - Fix Inverted FadeManager State Check
- **Problem:** Wall tones played ONLY during map transitions and were suppressed during normal gameplay (inverted behavior)
- **Root cause:** `IsScreenFading` checked `IsStateFadeOut() || IsStateFadeIn()` — but `IsStateFadeIn()` returns `true` during normal visible gameplay ("faded in" = screen visible), making `IsScreenFading` true almost always
- **Fix:** Replaced two-method check with `!IsFadeFinish()` — normal gameplay has `IsFadeFinish()=true` so tones play; during fades `IsFadeFinish()=false` so tones suppressed
- **File:** `Patches/MapTransitionPatches.cs` — replaced `isStateFadeOutMethod`/`isStateFadeInMethod` fields with single `isFadeFinishMethod`

### 2026-01-25 - Collision Sound Detection Improvements
- **Problem:** False positive wall bump sounds when walking through door trigger areas
- **Fix:** Require 2+ consecutive collisions at same position before playing sound
- **Changes to MovementSoundPatches.cs:**
  - Increased cooldown from 200ms to 300ms
  - Added consecutive hit tracking (lastCollisionPos, samePositionCount)
  - `REQUIRED_CONSECUTIVE_HITS = 2` - need 2+ hits at same spot
  - `POSITION_TOLERANCE = 1.0f` - positions within 1 unit considered "same"
- **Behavior:** Door triggers cause single collision (no sound); real walls cause repeated collisions (sound plays)

### 2026-01-25 - Wall Bump Sound Finalized
- **Final volume:** 0.27f (reduced 10% from 0.3f)
- **Parameters:** 55 Hz, 60ms duration, soft attack with noise mix
- **File:** `Utils/SoundPlayer.cs` GenerateThudTone call

### 2026-01-25 - Entity Translation System
- **Feature:** Added Japanese→English translation for entity names via JSON dictionary
- **EntityTranslator.cs:** Loads `UserData/FFI_ScreenReader/FF1_translations.json` at startup
- **Integration:** `EntityScanner.ConvertToNavigableEntity()` calls `Translate()` after name resolution
- **Untranslated tracking:** Japanese names without translations tracked; dump via ' hotkey to `EntityNames.json`
- **Initial translations:** Bat (コウモリ), Fairy King (妖精王), Locked Door (カギのかかった扉)

### 2026-01-24 - Interactive Object Name Resolution
- **Root cause:** Interactive objects (locked doors, etc.) showed generic "Interactive Object" instead of localized names
- **Fix:** Modified `EntityScanner.ConvertToNavigableEntity()` to call `GetEntityNameFromProperty()` first for both `FieldMapObjectDefault` and `IInteractiveEntity` cases
- **Fallback preserved:** Only falls back to `GetInteractiveObjectName()` or `CleanObjectName()` if property name is empty
- **Key insight:** `GetEntityNameFromProperty()` already existed for NPCs and properly resolves `PropertyEntity.Name` via `MessageManager.GetMessage()` for localized names

### 2026-01-24 - NPC Entity Scanner Fix
- **Root cause:** String-based NPC detection (`"chara"`, `"npc"`) matched non-NPC entities causing false positives
- **Fix:** Replaced with type-based detection using `TryCast<FieldNonPlayer>()`
- **CanAction filter:** Added `CanAction` check to filter non-interactive NPCs (collision without dialogue)
- **GetEntityNameFromProperty():** Added method using `PropertyEntity.Name` for localized NPC names
- **Key insight:** FF3 already had type-based detection; FF1 was using outdated string matching

### 2026-01-24 - Vehicle Scanning Fix
- **Root cause:** Vehicles use `ResidentCharaEntity` GameObjects, which were being filtered out by the "residentchara" check before reaching vehicle detection
- **Fix:** Moved `VehicleTypeMap` check in `EntityScanner.ConvertToNavigableEntity()` to run BEFORE the residentchara filter
- **Cleanup:** Removed verbose diagnostic logging from `FieldNavigationHelper.cs`
- **Key insight:** Filter order in EntityScanner matters - vehicle detection must precede party member filtering

### 2026-01-24 - Popup Architecture Fixes
- **NewGame duplicate fix** - Removed `CharacterCreationPatches` (wrong grid math); `NewGamePatches` handles character creation correctly
- **Popup button architecture** - Moved `CommonPopup.UpdateFocus` patch from BattlePausePatches to PopupPatches
- **Save/load button reading** - Added `PopupPatches.ReadCurrentButton(cursor)` call in `CursorNavigation_Postfix`
- **Battle pause menu fix** - Fixed cursor path detection to check full 3-level path for "curosr_parent"
- **BattlePausePatches simplified** - Now only contains `BattlePauseState` for memory-based pause detection
- **Status effect icon markup** - Added `TextUtils.StripIconMarkup()` to `GetConditionName()` in BattleMessagePatches; status effects (Protect, Haste, etc.) now announced without icon tags

### 2026-01-23 - Popup Reading Port from FF3
- **PopupPatches.cs** - Base popup system: CommonPopup, ChangeMagicStonePopup, GameOverSelectPopup, InfomationPopup, InputPopup, ChangeNamePopup
- **BattlePausePatches.cs** - Battle pause menu with FF1-specific offset (pauseController at 0x98 vs FF3's 0x90)
- **SaveLoadPatches.cs** - Save/load confirmation popups for title load, main menu load/save, QuickSave
- **NewGamePatches.cs** - Character creation: slot selection, name cycling, keyboard input, start confirmation
- **MenuStateRegistry.cs** - Centralized state tracking utility
- Title screen "Press any button" reading via SplashController + SystemIndicator approach
- Added `PopupState.ShouldSuppress()` and `SaveLoadMenuState.ShouldSuppress()` to CursorNavigation_Postfix

### 2026-01-23 - Config Menu Fixes
- **Double announcement fix** - Removed value-change handling from `SetFocus` patch; value changes (left/right arrows) now handled exclusively by `SwitchArrowSelectTypeProcess` and `SwitchSliderTypeProcess`
- **BGM/SFX sliders** - Now announce raw values (1-10) instead of percentages; `GetSliderPercentage()` accepts optional setting name parameter

### 2026-01-23 - Time.time Removal
- Removed entity scan throttling (now event-driven: map change or empty list only)
- Converted all ShouldAnnounce() to string-only deduplication (8 files)
- Files: FFI_ScreenReaderMod.cs, BattlePatches.cs, EquipMenuPatches.cs, ItemMenuPatches.cs, MagicMenuPatches.cs, StatusMenuPatches.cs, BattleItemPatches.cs, BattleMagicPatches.cs, ShopPatches.cs

### 2026-01-15 - Performance Optimizations
- **Startup lag** - Replaced assembly scanning with `typeof(GameCursor)` in TryPatchCursorNavigation
- **Entity scanning lag** - Removed debug logging from EntityScanner
- **Incremental scanning** - Added entityMap cache; only processes new entities after initial scan
- **Vehicle state** - Added V key handler; patched GetOn (FF1 uses GetOn(1) to disembark, not GetOff)
- **Stale callbacks** - Track LastSelectedCommandIndex to block magic/item callbacks after backing out

---

## Command Workarounds

| Failed Command | Workaround |
|----------------|------------|
| `cmd.exe //c build_and_deploy.bat` | Use PowerShell: `powershell.exe -Command "& 'path\build_and_deploy.bat'"` |
| `sed -n 'X,Yp' file` | Use `grep -n` or Read tool |
