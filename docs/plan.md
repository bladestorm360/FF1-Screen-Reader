# FF1 Screen Reader - Project Overview

MelonLoader accessibility mod for Final Fantasy I Pixel Remaster. Screen reader support (NVDA, JAWS, Narrator) via Tolk.dll. Port of ff3-screen-reader.

**Tech:** .NET 6.0 | MelonLoader | HarmonyLib 2.x | Tolk.dll

---

## Features

**Menus:** Title screen, new game, load/save, main menu (Items/Magic/Equipment/Status/Formation/Config/Save), config descriptions (I key), character creation, shops, popup confirmations, G key (gil), H key (party HP/MP)

**Dialogue:** Scrolling intro/outro, message windows (page-by-page), speaker names

**Navigation:** Entity scanner (chests/NPCs/exits/warp tiles/events), category filtering (J/K/L), pathfinding, map transitions ("Entering {MapName}"), M key (map name), wall bumps, directional wall tones (E=220Hz/W=200Hz stereo), Ctrl+Arrow teleport, V key (vehicle state). Landing zone detection ported but untested.

**Battle:** Command/target selection, damage/healing/status, item/magic menus (spell charges), victory screen, battle start/escape, pause menu (spacebar). Note: accuracy/evasion stat gains not hooked.

---

## Hotkeys

| Key | Function | Key | Function |
|-----|----------|-----|----------|
| J/[ | Prev entity | M | Map name |
| K | Repeat current | H | Party HP/MP |
| L/] | Next entity | G | Gil |
| Shift+J/L | Cycle category | V | Vehicle state |
| P/\ | Pathfind | I | Item/config description |
| Shift+P | Toggle path filter | ' | Toggle footsteps |
| 0 | Reset to All category | ; | Toggle wall tones |
| =/- | Next/prev category | 9 | Toggle audio beacons |
| Ctrl+Arrow | Teleport | F1 | Toggle walk/run |
| | | F8 | Open mod menu |

---

## Project Structure

**Core:** `FFI_ScreenReaderMod.cs`, `InputManager.cs`, `ModMenu.cs`, `Filters/`
**Field:** `EntityScanner.cs`, `NavigableEntity.cs`, `FieldNavigationHelper.cs`, `MapNameResolver.cs`, `FilterContext.cs`
**Menus:** `MenuTextDiscovery.cs`, `SaveSlotReader.cs`, `ConfigMenuReader.cs`, `ItemDetailsAnnouncer.cs`, `StatusDetailsReader.cs`, `CharacterSelectionReader.cs`
**Patches:** `*MenuPatches.cs`, `Battle*Patches.cs`, `MessageWindowPatches.cs`, `ScrollMessagePatches.cs`, `PopupPatches.cs`, `BattlePausePatches.cs`, `MapTransitionPatches.cs`, `MovementSoundPatches.cs`, `CursorNavigationPatches.cs`, `SaveLoadPatches.cs`, `ShopPatches.cs`, `VehicleLandingPatches.cs`, `JobSelectionPatches.cs`, `NewGamePatches.cs`
**Utils:** `TolkWrapper.cs`, `SpeechHelper.cs`, `TextUtils.cs`, `SoundPlayer.cs`, `GameObjectCache.cs`, `MenuStateRegistry.cs`, `EntityTranslator.cs`, `LocationMessageTracker.cs`, `AnnouncementDeduplicator.cs`, `CoroutineManager.cs`, `MoveStateHelper.cs`, `StateMachineHelper.cs`, `IL2CppOffsets.cs`

---

## FF1-Specific Notes

- **Magic:** Spell slots per level (x/y charges), not MP pool
- **Jobs:** Fighter, Thief, Black Mage, White Mage, Red Mage, Monk
- **Party:** Fixed 4 Light Warriors named at start
- **Vehicles:** Ship, Canoe, Airship (no chocobo)
- **Translations:** Japanese entity names via embedded dictionary in `EntityTranslator.cs`
