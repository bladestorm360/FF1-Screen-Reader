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
