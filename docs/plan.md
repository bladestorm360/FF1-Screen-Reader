# FF1 Screen Reader - Implementation Plan

## Overview

Port the FF3 screen reader to FF1, adapting for FF1-specific game assemblies while maintaining identical user experience and hotkey mappings.

---

## Project Structure

```
FFI_ScreenReader/
├── Core/
│   ├── FFI_ScreenReaderMod.cs      # Entry point + CursorNavigation dispatcher
│   ├── InputManager.cs             # Hotkey handling (I, G, H, M keys)
│   └── Filters/                    # Entity filtering system
├── Field/
│   ├── EntityScanner.cs            # Field entity discovery
│   ├── NavigableEntity.cs          # Entity types
│   └── FieldNavigationHelper.cs    # Pathfinding integration
├── Menus/
│   ├── MenuTextDiscovery.cs        # Generic cursor text discovery
│   └── SaveSlotReader.cs           # Save/load parsing
├── Patches/
│   ├── *MenuPatches.cs             # Menu-specific patches with state classes
│   ├── Battle*Patches.cs           # Battle system patches
│   ├── MessageWindowPatches.cs     # NPC dialogue
│   └── ScrollMessagePatches.cs     # Intro/outro text
└── Utils/
    ├── TolkWrapper.cs              # Screen reader integration
    └── TextUtils.cs                # Icon stripping
```

---

## Active State System

Each menu type has a state class with `ShouldSuppress()` to prevent `MenuTextDiscovery` from double-announcing. State is cleared via `SetActive(false)` patches on all menu controllers.

| State Class | Purpose |
|-------------|---------|
| `ConfigMenuState` | Config menu |
| `ItemMenuState` | Item menu + target selection |
| `EquipMenuState` | Equipment slots + items |
| `MagicMenuState` | Spell list + target selection |
| `StatusMenuState` | Character status + details |
| `ShopMenuTracker` | Shop item lists |
| `BattleCommandState` | Battle commands |
| `BattleTargetState` | Enemy/ally targeting |
| `BattleItemMenuState` | Battle items |
| `BattleMagicMenuState` | Battle magic |

**Performance:** Never use `FindObjectOfType` in `ShouldSuppress()` - causes lag.

---

## Implementation Status

### Phase 1: Core - COMPLETE
- Project setup, TolkWrapper, SpeechHelper, CoroutineManager, TextUtils

### Phase 2: Menus - COMPLETE
- [x] Cursor navigation with state suppression
- [x] Config menu (I key for descriptions) - TESTED
- [x] Character creation/job selection - TESTED
- [x] Title screen, Save/Load - TESTED
- [x] Shop system (items, magic slots, "can't learn") - TESTED
- [x] Equipment menu - TESTED
- [x] Item menu (I key for descriptions) - TESTED
- [x] Magic menu (spell charges MP: x/y, target HP/status) - TESTED
- [x] Status menu + details (23 stats, arrow navigation) - TESTED
- [x] G key gil, H key party HP - TESTED

### Phase 3: Dialogue - COMPLETE
- [x] Scrolling intro text - TESTED
- [x] Message windows (page-by-page) - TESTED
- [x] Speaker names - TESTED

### Phase 4: Navigation - COMPLETE
- [x] Entity scanner with category filtering - TESTED
- [x] Pathfinding integration - TESTED
- [x] Map name (M key), wall bumps - TESTED
- [x] Focus preservation, chest opened/unopened state - TESTED
- [ ] Vehicle state announcements (ship, canoe, airship) - PORTED, UNTESTED
- [ ] Landing zone detection ("Can land") - PORTED, UNTESTED

### Phase 5: Battle - COMPLETE
- [x] Command/target selection - TESTED
- [x] Damage, HP/MP recovery, status effects - TESTED
- [x] Battle item/magic menus (spell charges MP: x/y) - TESTED
- [x] Victory screen (gil, XP, items, level ups) - TESTED
- [x] Battle start messages, escape - TESTED

**Known Issue:** Level up stat gains only showing HP - under investigation.

---

## Critical IL2CPP Workarounds

### String Parameters Crash
Use manual patching with positional params (`__0`, `__1`) instead of `[HarmonyPatch]` with string params.

### Field Access Fails
Use pointer-based access with offsets from dump.cs:
```csharp
IntPtr ptr = ((Il2CppObjectBase)instance).Pointer;
unsafe {
    IntPtr fieldPtr = *(IntPtr*)((byte*)ptr.ToPointer() + OFFSET);
}
```

---

## FF1-Specific Notes

- **Magic System:** Spell slots per level (MP: x/y charges), not MP pool
- **Jobs:** Fighter, Thief, Black Mage, White Mage, Red Mage, Monk
- **Party:** Fixed 4 Light Warriors, named at game start
