# FF1 Screen Reader - Project Overview

## About

MelonLoader accessibility mod for Final Fantasy I Pixel Remaster. Provides screen reader support (NVDA, JAWS, Narrator) for blind users via Tolk.dll integration. Port of ff3-screen-reader.

**Tech:** .NET 6.0 | MelonLoader | HarmonyLib 2.x | Tolk.dll

---

## Feature Status

### Menus ✅ Complete
- [x] Title screen ("Press any button"), new game, load/save
- [x] Main menu (Items, Magic, Equipment, Status, Formation, Config, Save)
- [x] Config menu with I key descriptions
- [x] Character creation (Light Warrior slots, name cycling, job selection)
- [x] Shop system (items, magic slots, equipment comparison)
- [x] Popup confirmations (save/load, exit, game over, magic learn/forget)
- [x] G key (gil), H key (party HP/MP)

### Dialogue ✅ Complete
- [x] Scrolling intro/outro text
- [x] Message windows (page-by-page)
- [x] Speaker names

### Navigation ✅ Complete
- [x] Entity scanner (chests, NPCs, exits, events)
- [x] Category filtering (J/K/L navigation)
- [x] Pathfinding with distance/direction
- [x] Map name (M key), wall bump sounds (consecutive hit detection, fade suppression)
- [x] Teleportation (Ctrl+Arrow)
- [x] Vehicle announcements (V key, enter/exit)
- [ ] Landing zone detection - ported, untested

### Battle ✅ Complete
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
│   ├── FFI_ScreenReaderMod.cs    # Entry point
│   ├── InputManager.cs           # Hotkeys
│   └── Filters/                  # Entity filters
├── Field/
│   ├── EntityScanner.cs          # Entity discovery
│   ├── NavigableEntity.cs        # Entity types
│   └── FieldNavigationHelper.cs  # Pathfinding
├── Menus/
│   ├── MenuTextDiscovery.cs      # Cursor text
│   └── SaveSlotReader.cs         # Save parsing
├── Patches/
│   ├── *MenuPatches.cs           # Menu patches
│   ├── Battle*Patches.cs         # Battle patches
│   ├── MessageWindowPatches.cs   # Dialogue
│   ├── PopupPatches.cs           # Confirmation popups + button navigation
│   ├── BattlePausePatches.cs     # Battle pause state detection
│   ├── MapTransitionPatches.cs    # Fade detection (wall tone suppression)
│   ├── SaveLoadPatches.cs        # Save/load popups
│   └── NewGamePatches.cs         # Character creation (Light Warriors)
└── Utils/
    ├── TolkWrapper.cs            # Screen reader
    ├── TextUtils.cs              # Text cleanup
    ├── GameObjectCache.cs        # Caching
    ├── MenuStateRegistry.cs      # State tracking
    └── EntityTranslator.cs       # Japanese→English translations
```

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

## FF1-Specific Notes

- **Magic:** Spell slots per level (x/y charges), not MP pool
- **Jobs:** Fighter, Thief, Black Mage, White Mage, Red Mage, Monk
- **Party:** Fixed 4 Light Warriors named at start
- **Vehicles:** Ship, Canoe, Airship (no chocobo)
- **Translations:** Japanese entity names translated via `UserData/FFI_ScreenReader/FF1_translations.json`

---

## Translation System

Translates Japanese entity names to English for screen reader announcements.

### Files
| File | Purpose |
|------|---------|
| `Utils/EntityTranslator.cs` | Translation lookup, JSON parsing, untranslated name tracking |
| `UserData/FFI_ScreenReader/FF1_translations.json` | User-editable translation dictionary |
| `UserData/FFI_ScreenReader/EntityNames.json` | Dumped untranslated names (generated) |

### Hotkeys
| Key | Function |
|-----|----------|
| ' (apostrophe) | Dump untranslated Japanese names to EntityNames.json |

### Adding Translations
1. Open `UserData/FFI_ScreenReader/FF1_translations.json`
2. Add entries as `"Japanese": "English"` pairs:
   ```json
   {
     "コウモリ": "Bat",
     "妖精王": "Fairy King",
     "カギのかかった扉": "Locked Door"
   }
   ```
3. Save file - translations are loaded at startup (or use Reload() for hot-reload)
