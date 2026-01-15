# Battle System Implementation Plan

## Status: COMPLETE BUT UNTESTED

**Implementation Date:** 2026-01-12

All battle patches have been implemented and both FF1 and FF3 build successfully. The following files were created/modified:

| File | Status | Notes |
|------|--------|-------|
| `ShopPatches.cs` | Modified | Added 3-tier price access fallback with logging |
| `BattleMessagePatches.cs` | Created | Damage/healing (isRecovery), status effects |
| `BattleCommandPatches.cs` | Created | Turn, command, target selection |
| `BattleItemPatches.cs` | Created | Battle item menu navigation |
| `BattleMagicPatches.cs` | Created | Spell menu with charges (FF1-specific) |
| `BattleResultPatches.cs` | Created | Victory: gil, XP, items, level ups |
| `BattleStartPatches.cs` | Created (FF1 & FF3) | Preemptive, back attack, ambush |
| `BattlePatches.cs` | Modified | Added ResetState() aliases |
| `FFI_ScreenReaderMod.cs` | Modified | Apply new battle patches |
| `FFIII_ScreenReaderMod.cs` | Modified | Apply BattleStartPatches |

**FF1-Specific Adaptations:**
- Uses `BasePower`, `BaseVitality`, `BaseAgility`, `BaseIntelligence` (not `BaseStr`, `BaseVit`, etc.)
- Uses `BaseLevel` (not `Level`)
- ContentUtitlity in `Last.Systems` namespace (not `Last.Management`)
- BattleFrequencyAbilityInfomationController in `Serial.FF1.UI.KeyInput` namespace
- BattleResultData in `Last.Data` namespace (not `Last.Battle`)
- DropItemData has no Count property (single item drops)
- Item names via `GetMesIdItemName()` + `MessageManager`

**Requires in-game testing to verify functionality.**

---

## Overview

Port the FF3 battle system to FF1, adapting for FF1-specific classes. Fix shop price reading first.

---

## Task 1: Fix Shop Price Reading

**Problem:** Shop items read only name, not price.

**Current Code:** `ShopPatches.cs` lines 366-413 - tries reflection with field names "shopListItemContentView" and "view" to find "priceText".

**Fix Approach:**
1. Add debug logging to identify which reflection step fails
2. Search FF1 dump.cs for correct field names on `ShopListItemContentController`
3. Update field name strings to match FF1's actual structure

**Estimated Changes:** ~20 lines in `ShopPatches.cs`

---

## Task 2: Battle Message Patches

**Port from:** `ff3-screen-reader\Patches\BattleMessagePatches.cs`

### 2.1 Global Message Deduplication
- `GlobalBattleMessageTracker` class with 1.5s throttle window
- Prevents same message from multiple patches announcing

### 2.2 Action Announcements (CreateActFunction patch)
**Patch:** `ParameterActFunctionManagment.CreateActFunction`

**Behavior:**
- Get actor name (player: `ownedCharacterData.Name`, enemy: `GetMesIdName()` -> `MessageManager`)
- Get action name:
  1. **Items**: Extract from `battleActData.itemList[0].Name` (NOT generic "Item")
  2. **Spells/Abilities**: Extract from `battleActData.abilityList[0]` via `ContentUtitlity.GetAbilityName()`
  3. **Fallback**: Command name via `battleActData.Command.MesIdName`
- Strip icon markup from all names
- Announce: `"{Actor} attacks"`, `"{Actor} defends"`, `"{Actor}, {SpellName}"`

### 2.3 Damage/Healing Announcements (CreateDamageView patch)
**Patch:** `BattleBasicFunction.CreateDamageView(BattleUnitData, int value, HitType, bool isRecovery)`

**Behavior:**
- Get target name (same as actor name extraction)
- **CRITICAL**: Use `isRecovery` parameter:
  - `isRecovery == true` -> `"{Target}: Recovered {value} HP"`
  - `isRecovery == false` -> `"{Target}: {value} damage"`
  - `HitType.Miss || value == 0` -> `"{Target}: Miss"`
- Queue (no interrupt)

### 2.4 Status Effect Announcements
**Patch:** `BattleConditionController.Add(BattleUnitData, int id)`

**Behavior:**
- Get target name
- Look up condition name via `ConfirmedConditionList()` and `MessageManager`
- Skip conditions with no MesId (internal/hidden)
- Announce: `"{Target}: {ConditionName}"`
- Local deduplication (skip same announcement twice in a row)

---

## Task 3: Battle Command Patches

**Port from:** `ff3-screen-reader\Patches\BattleCommandPatches.cs`

### 3.1 Turn Announcements
**Patch:** `BattleCommandSelectController.SetCommandData(OwnedCharacterData)`

**Behavior:**
- Announce `"{CharacterName}'s turn"` (INTERRUPT)
- Track character ID to prevent duplicate announcements

### 3.2 Command Selection
**Patch:** `BattleCommandSelectController.SetCursor(int index)`

**Behavior:**
- Set `BattleCommandState.IsActive = true`
- Suppress if target selection active
- Skip if same index already announced
- Get command from `contentList[index].TargetCommand.MesIdName`
- Announce command name (no interrupt)

### 3.3 Target Selection
**Patches:** `BattleTargetSelectController.SelectContent` (Player and Enemy overloads)

**Behavior:**
- Set `BattleTargetPatches.IsTargetSelectionActive = true`
- Players: `"{Name}: HP {current}/{max}"` (INTERRUPT)
- Enemies: `"{Name}: HP {current}/{max}"` with A/B/C suffix for duplicates

---

## Task 4: Battle Item Patches

**Port from:** `ff3-screen-reader\Patches\BattleItemPatches.cs`

**Patch:** `BattleItemInfomationController.SelectContent(Cursor, WithinRangeType)`

**Behavior:**
- Find focused item via `contentController.Data` (ItemListContentData)
- Format: `"{ItemName}: {Description}"`
- Strip icon markup
- 150ms throttle for deduplication

---

## Task 5: Battle Magic Patches

**Port from:** `ff3-screen-reader\Patches\BattleMagicPatches.cs`

**NOTE:** FF1 uses spell slot/charges system (X/Y charges per spell level), NOT MP.

**Patch:** `BattleFrequencyAbilityInfomationController.SelectContent` (or FF1 equivalent)

**Behavior:**
- Get spell name via `ability.MesIdName` -> `MessageManager`
- Get spell level from `ability.Ability.AbilityLv`
- Look up charges: `param.CurrentMpCountList[level]` / `param.ConfirmedMaxMpCount(level)`
- Format: `"{SpellName}: {X}/{Y} charges. {Description}"`
- Strip icon markup
- Empty slots announce "Empty"

---

## Task 6: Battle Result Patches

**Port from:** `ff3-screen-reader\Patches\BattleResultPatches.cs`

### 6.1 Phase 1: Experience & Gil (ShowPointsInit)
**Patches:** `ResultMenuController.ShowPointsInit` (KeyInput and Touch variants)

**Behavior:**
- Clear all battle menu flags
- Announce: `"Gained {gil:N0} gil"` (INTERRUPT)
- Per-character: `"{CharName} gained {exp:N0} XP"` (queue)

### 6.2 Phase 2: Item Drops (ShowGetItemsInit)
**Patches:** `ResultMenuController.ShowGetItemsInit`

**Behavior:**
- Convert items via `ListItemFormatter.GetContentDataList()`
- Announce: `"Found {ItemName}"` or `"Found {ItemName} x{count}"`

### 6.3 Phase 3: Level Ups (ShowStatusUpInit)
**Patches:** `ResultMenuController.ShowStatusUpInit`, `ResultSkillController.ShowLevelUp`

**Behavior:**
- Check `charResult.IsLevelUp`
- Announce: `"{CharName} leveled up to level {newLevel}"`
- Calculate and announce stat gains: `"HP +{X}, Strength +{Y}, ..."`

**NOTE:** FF1 has no job system like FF3, so skip job level up announcements.

---

## Task 7: Battle Start Messages (NEW - Not in FF3)

**Problem:** Need to announce "Back Attack!", "Preemptive Attack!", "Ambush!" etc.

**FF1 Classes:**
- `BattlePopPlug.PreeMptiveState` enum:
  - Normal = 0
  - PreeMptive = 1 (player preemptive)
  - BackAttack = 2
  - EnemyPreeMptive = 3 (enemy surprise/ambush)
  - EnemySideAttack = 4
  - SideAttack = 5

**Potential Patches:**
- `BattleController.StartPreeMptiveMes()` - Called when preemptive message shows
- `BattleController.SetBackAttack()` - Called on back attack

**Behavior:**
- Detect state via `BattlePopPlug.GetResult()` or similar
- Announce appropriate message:
  - PreeMptive: "Preemptive attack!"
  - BackAttack: "Back attack!"
  - EnemyPreeMptive: "Ambush!" or "Enemy surprise attack!"

---

## Task 8: Escape Messages (NEW - Not in FF3)

**Problem:** Need to announce "The party escaped!" when successfully fleeing.

**Potential Approaches:**
1. Patch `BattleController.BattleState` transition to escape state
2. Patch battle result with escape type
3. Hook into system message display for escape text

**Investigation needed:** Search dump.cs for escape result handling.

---

## State Management Summary

| State Class | File | Purpose |
|-------------|------|---------|
| `BattleCommandState` | BattlePatches.cs (existing) | Command menu suppression |
| `BattleTargetState` | BattlePatches.cs (existing) | Target selection suppression |
| `BattleItemMenuState` | BattlePatches.cs (existing) | Item menu suppression |
| `BattleMagicMenuState` | BattlePatches.cs (existing) | Magic menu suppression |
| `GlobalBattleMessageTracker` | BattleMessagePatches.cs (new) | Message deduplication |

**All states cleared at battle end via `BattleStateHelper.ClearAllBattleMenuFlags()`**

---

## File Changes Summary

| File | Action | Description |
|------|--------|-------------|
| `ShopPatches.cs` | Modify | Add logging, fix price field name |
| `BattlePatches.cs` | Keep | Existing state classes are sufficient |
| `BattleMessagePatches.cs` | Create | Action, damage, status patches |
| `BattleCommandPatches.cs` | Create | Turn, command, target patches |
| `BattleItemPatches.cs` | Create | Battle item menu |
| `BattleMagicPatches.cs` | Create | Battle magic menu |
| `BattleResultPatches.cs` | Create | Victory screen |
| `BattleStartPatches.cs` | Create | Preemptive/back attack messages |
| `FFI_ScreenReaderMod.cs` | Modify | Apply new patches |

---

## Duplicate Prevention Strategy

1. **Global throttle (1.5s)**: `GlobalBattleMessageTracker.TryAnnounce()` - prevents spam
2. **Index tracking**: Command/target selection track last announced index
3. **State-based suppression**: Each menu state suppresses generic cursor
4. **Local deduplication**: Status effects skip same message twice in a row

---

## Testing Checklist (UNTESTED)

All items below are implemented but require in-game verification:

- [ ] Shop prices announce with item names
- [ ] Battle turn announces character name
- [ ] Commands announce when navigating (Attack, Magic, Item, etc.)
- [ ] Target selection announces with HP
- [ ] Actions announce with actor + action name
- [ ] Items used announce actual item name (e.g., "Rei, Potion" not "Rei, Item")
- [ ] Spells announce spell name without icon tags
- [ ] Healing announces "Recovered X HP" (using isRecovery flag)
- [ ] Damage announces "X damage"
- [ ] Status effects announce
- [ ] Back attack / preemptive / ambush announces at battle start
- [ ] Escape announces when successful
- [ ] Victory: gil, XP per character, items, level ups
- [ ] No duplicate messages during battle
- [ ] FF3 battle start messages work (back attack, preemptive, ambush)
