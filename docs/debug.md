# Debug Notes & Technical Reference

## IL2CPP Pointer Access Pattern

When `AccessTools.Field` returns null for IL2CPP types, use pointer-based access:
```csharp
IntPtr ptr = ((Il2CppObjectBase)instance).Pointer;
unsafe {
    IntPtr fieldPtr = *(IntPtr*)((byte*)ptr.ToPointer() + OFFSET);
    var list = new Il2CppSystem.Collections.Generic.List<string>(fieldPtr);
}
```

---

## Key Offsets

### MessageWindowManager
| Field | Offset | Description |
|-------|--------|-------------|
| `messageList` | 0x88 | Word-wrapped dialogue lines |
| `newPageLineList` | 0xA0 | END line indices per page |
| `spekerValue` | 0xA8 | Speaker name (typo in game) |
| `currentPageNumber` | 0xF8 | Current page (0-based) |

### Menu Controllers (FF1 KeyInput)
| Controller | Field | Offset |
|------------|-------|--------|
| `EquipmentWindowController` | stateMachine | 0x60 |
| `AbilityWindowController` | stateMachine | 0x88 |
| `AbilityWindowController` | statusController | 0x50 |
| `AbilityCharaStatusController` | targetData | 0x48 |

### Battle Controllers (FF1 KeyInput)
| Controller | Field | Offset |
|------------|-------|--------|
| `BattleAbilityInfomationControllerBase` | stateMachine | 0x28 |
| `BattleAbilityInfomationControllerBase` | selectedBattlePlayerData | 0x30 |
| `BattleAbilityInfomationControllerBase` | dataList | 0x70 |
| `BattleAbilityInfomationControllerBase` | contentList | 0x78 |
| `BattleUnitData` | BattleUnitDataInfo | 0x28 |
| `BattleUnitDataInfo` | Parameter | 0x10 |

### Shop Controllers
| Controller | Field | Offset |
|------------|-------|--------|
| `ShopMagicTargetSelectController` | isFoundEquipSlot | 0x70 |

---

## HitType Values
| Value | Type | Announcement |
|-------|------|--------------|
| 2 | Miss | "Miss" |
| 4 | Recovery | "Recovered X HP" |
| 5 | MPHit | "X MP damage" |
| 6 | MPRecovery | "Recovered X MP" |

## ConditionType Values
| Value | Type |
|-------|------|
| 5 | UnableFight (KO) |
| 6 | Silence |
| 7 | Sleep |
| 8 | Paralysis |
| 9 | Blind |
| 10 | Poison |
| 11 | Mineralization (Stone) |
| 12 | Confusion |

---

## State Machine Values

### EquipmentWindowController
- STATE_COMMAND = 1 (command bar focused)

### AbilityWindowController
- STATE_NONE = 0 (closing)
- STATE_COMMAND = 4 (command bar focused)

---

## Known Issue

**Level up stat gains** - Only HP showing. Debug logging added in `BattleResultPatches.cs`. Check logs for `[Battle Result]` messages.

---

## Key Patterns

### Menu State Clearing
All menu controllers must have `SetActive(false)` postfix that clears state unconditionally. Do NOT check `menuManager.IsOpen`.

### Battle State Clearing
- Set `IsInBattle = true` in `BattleStartPatches`
- Clear on victory/defeat/escape callbacks
- Guard with `if (!IsInBattle) return;` to prevent startup issues

### Dialogue Page Reading
- `newPageLineList` contains END indices (inclusive)
- Convert to start indices: `[0, 2]` → pages start at lines `[0, 1, 3]`
- Patch both `PlayingInit` (all pages) and `NewPageInputWaitInit` (intermediate)

---

## Version History

### 2026-01-15 - Performance Optimizations
- **Fix: Slow startup - Assembly scanning replaced with typeof()**
  - **Problem**: `TryPatchCursorNavigation` used expensive `GetAssemblies()/GetTypes()` loops
  - **Solution**: Added IL2CPP type alias `using GameCursor = Il2CppLast.UI.GameCursor;` and use `typeof(GameCursor)` directly
  - File: `Core/FFI_ScreenReaderMod.cs`

- **Fix: Entity scanning lag - Debug logging removed**
  - **Problem**: EntityScanner had extensive debug logging (per-entity processing, property exploration, hierarchy dumps)
  - **Solution**: Removed all `MelonLogger.Msg` calls, kept only error-level logging
  - Removed debug methods: `LogEntityDetails`, `LogParentHierarchy`, `LogFilteredEntity`, `GetEntityHierarchy`
  - Methods cleaned: `ScanEntities`, `EnsureCorrectMap`, `GetCurrentMapId`, `ConvertToNavigableEntity`, `GetGotoMapDestinationId`, `ResolveMapName`, `GetMapExitDestination`
  - File: `Field/EntityScanner.cs`

- **Fix: Entity scanning performance - Incremental scanning implemented**
  - **Problem**: `ScanEntities()` re-converted ALL entities every scan (full clear + rebuild)
  - **Solution**: Added `Dictionary<FieldEntity, NavigableEntity> entityMap` to cache conversions
  - New behavior: Only processes NEW entities, keeps existing conversions
  - Added `ForceRescan()` method for explicit cache clearing
  - `EnsureCorrectMap()` now calls `ForceRescan()` on map transitions to properly clear cache
  - **Impact**: After initial scan, subsequent scans only process new/changed entities
  - File: `Field/EntityScanner.cs`

- **Fix: Vehicle state announcements not working** - VERIFIED WORKING
  - **Problem**: V key did nothing; entering/exiting vehicles had no announcements
  - **Root Causes**:
    1. V key handler was missing from `InputManager.cs`
    2. Patching `ChangeMoveState` didn't work - method never fires during vehicle transitions
  - **Solution**:
    1. Added V key handler + `AnnounceCurrentVehicle()` method to `InputManager.cs`
    2. Changed patching approach from `ChangeMoveState` to `GetOn` only (FF1 doesn't use GetOff)
    3. Simplified `MoveStateHelper.cs` to use cached state from GetOn events
  - **FF1 GetOn signature**: `GetOn(int typeId, bool isBackground = False)` - 2 params (FF3 has 3)
  - **FF1-specific**: GetOff is never called! FF1 uses `GetOn(1)` (TRANSPORT_PLAYER) to disembark instead
  - **TransportationType values**: Player (1), Ship (2), Plane/Airship (3), Content/Canoe (5), LowFlying (7)
  - Files: `Core/InputManager.cs`, `Patches/VehicleLandingPatches.cs`, `Utils/MoveStateHelper.cs`

- **Fix: Battle magic/item menu re-announcing after backing out** - VERIFIED WORKING
  - **Problem**: Entering magic menu, highlighting a spell, backing out to command menu, then selecting Attack would re-announce the last highlighted spell
  - **Root Cause**: `BattleMagicPatches.SetCursor_Postfix` fired ~350ms AFTER returning to command menu (stale callback during close animation), overwriting correct state
  - **Solution**: Track `LastSelectedCommandIndex` in `BattleCommandState` and guard magic/item patches
    - Magic menu only processes if `LastSelectedCommandIndex == 1` (Magic command)
    - Item menu only processes if `LastSelectedCommandIndex == 2` (Items command)
    - Stale callbacks blocked because user moved to different command (e.g., Attack at index 0)
  - **Command indices**: Attack (0), Magic (1), Items (2), Defend/Run (3)
  - Files: `Patches/BattlePatches.cs`, `Patches/BattleCommandPatches.cs`, `Patches/BattleMagicPatches.cs`, `Patches/BattleItemPatches.cs`
