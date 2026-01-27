# FF1 Screen Reader - Project Overview

## About

MelonLoader accessibility mod for Final Fantasy I Pixel Remaster. Provides screen reader support (NVDA, JAWS, Narrator) for blind users via Tolk.dll integration. Port of ff3-screen-reader.

**Tech:** .NET 6.0 | MelonLoader | HarmonyLib 2.x | Tolk.dll

---

## Feature Status

### Menus
- [x] Title screen ("Press any button"), new game, load/save
- [x] Main menu (Items, Magic, Equipment, Status, Formation, Config, Save)
- [x] Config menu with I key descriptions
- [x] Character creation (Light Warrior slots, name cycling, job selection)
- [x] Shop system (items, magic slots, equipment comparison)
- [x] Popup confirmations (save/load, exit, game over, magic learn/forget)
- [x] G key (gil), H key (party HP/MP)

### Dialogue
- [x] Scrolling intro/outro text
- [x] Message windows (page-by-page)
- [x] Speaker names

### Navigation
- [x] Entity scanner (chests, NPCs, exits, warp tiles, events)
- [x] Category filtering (J/K/L navigation)
- [x] Pathfinding with distance/direction
- [x] Map transition announcements ("Entering {MapName}" on map change, including load-game)
- [x] Map name (M key), wall bump sounds (consecutive hit detection, fade suppression)
- [x] Directional wall proximity tones (N/S/E/W stereo panning, E=220Hz W=200Hz for L/R distinction)
- [x] Teleportation (Ctrl+Arrow)
- [x] Vehicle announcements (V key, enter/exit)
- [ ] Landing zone detection - ported, untested

### Battle
- [x] Command/target selection
- [x] Damage, healing, status effects
- [x] Battle item/magic menus (spell charges)
- [x] Victory screen (gil, XP, items, level ups)
- [x] Battle start, escape
- [x] Battle pause menu (spacebar)

**Note:** Accuracy/evasion stat gains not hooked (minor)

---

## Hotkeys

| Key | Function |
|-----|----------|
| J/[ | Previous entity |
| K | Repeat current |
| L/] | Next entity |
| Shift+J/L | Cycle category |
| P/\ | Pathfind to entity |
| Shift+P | Toggle path filter |
| M | Map name |
| H | Party HP/MP |
| G | Gil |
| V | Vehicle state |
| I | Item/config description |
| 0 | Reset to All category |
| =/- | Next/prev category |
| Ctrl+Arrow | Teleport |
| ' | Dump untranslated names |

---

## Project Structure

```
FFI_ScreenReader/
├── Core/
│   ├── FFI_ScreenReaderMod.cs
│   ├── InputManager.cs
│   └── Filters/
├── Field/
│   ├── EntityScanner.cs
│   ├── NavigableEntity.cs
│   ├── FieldNavigationHelper.cs
│   ├── MapNameResolver.cs
│   └── FilterContext.cs
├── Menus/
│   ├── MenuTextDiscovery.cs
│   ├── SaveSlotReader.cs
│   ├── ConfigMenuReader.cs
│   ├── ItemDetailsAnnouncer.cs
│   ├── StatusDetailsReader.cs
│   └── CharacterSelectionReader.cs
├── Patches/
│   ├── *MenuPatches.cs
│   ├── Battle*Patches.cs
│   ├── MessageWindowPatches.cs
│   ├── ScrollMessagePatches.cs
│   ├── PopupPatches.cs
│   ├── BattlePausePatches.cs
│   ├── MapTransitionPatches.cs
│   ├── MovementSoundPatches.cs
│   ├── CursorNavigationPatches.cs
│   ├── SaveLoadPatches.cs
│   ├── ShopPatches.cs
│   ├── VehicleLandingPatches.cs
│   ├── JobSelectionPatches.cs
│   └── NewGamePatches.cs
└── Utils/
    ├── TolkWrapper.cs
    ├── SpeechHelper.cs
    ├── TextUtils.cs
    ├── SoundPlayer.cs
    ├── GameObjectCache.cs
    ├── MenuStateRegistry.cs
    ├── EntityTranslator.cs
    ├── LocationMessageTracker.cs
    ├── AnnouncementDeduplicator.cs
    ├── CoroutineManager.cs
    ├── MoveStateHelper.cs
    ├── StateMachineHelper.cs
    └── IL2CppOffsets.cs
```

---

## FF1-Specific Notes

- **Magic:** Spell slots per level (x/y charges), not MP pool
- **Jobs:** Fighter, Thief, Black Mage, White Mage, Red Mage, Monk
- **Party:** Fixed 4 Light Warriors named at start
- **Vehicles:** Ship, Canoe, Airship (no chocobo)
- **Translations:** Japanese entity names translated via `UserData/FFI_ScreenReader/FF1_translations.json` (see debug.md for details)
