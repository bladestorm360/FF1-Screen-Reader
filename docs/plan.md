# FF1 Screen Reader - Project Overview

MelonLoader accessibility mod for Final Fantasy I Pixel Remaster. Screen reader support (NVDA, JAWS, Narrator) via Tolk.dll. Port of ff3-screen-reader.

**Tech:** .NET 6.0 | MelonLoader | HarmonyLib 2.x | Tolk.dll

---

## Features

**Menus:** Title screen, new game, load/save (slot name/date/character/location/playtime), main menu (Items/Magic/Equipment/Status/Formation/Config/Save), config descriptions (I key), character creation, shops, popup confirmations, G key (gil), H key (party HP/MP)

**Dialogue:** Scrolling intro/outro, message windows (page-by-page), speaker names

**Navigation:** Entity scanner (chests/NPCs/exits/warp tiles/events), category filtering (J/K/L), pathfinding, map transitions ("Entering {MapName}"), M key (map name), wall bumps, directional wall tones (E=220Hz/W=200Hz stereo), Ctrl+Arrow teleport, V key (vehicle state). Landing zone detection ported but untested.

**Battle:** Command/target selection, damage/healing/status, item/magic menus (spell charges), victory screen, battle start/escape, pause menu (spacebar). Note: accuracy/evasion stat gains not hooked.

**Minigames:** 15-puzzle — arrowing over a tile speaks the number (or "empty"); I key / right-stick up speaks row/column

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
**Patches:** `*MenuPatches.cs`, `Battle*Patches.cs`, `MessageWindowPatches.cs`, `ScrollMessagePatches.cs`, `PopupPatches.cs`, `BattlePausePatches.cs`, `MapTransitionPatches.cs`, `MovementSoundPatches.cs`, `CursorNavigationPatches.cs`, `SaveLoadPatches.cs`, `ShopPatches.cs`, `VehicleLandingPatches.cs`, `JobSelectionPatches.cs`, `NewGamePatches.cs`, `PuzzlePatches.cs`
**Utils:** `TolkWrapper.cs`, `SpeechHelper.cs`, `TextUtils.cs`, `SoundPlayer.cs`, `GameObjectCache.cs`, `MenuStateRegistry.cs`, `EntityTranslator.cs`, `LocationMessageTracker.cs`, `AnnouncementDeduplicator.cs`, `CoroutineManager.cs`, `MoveStateHelper.cs`, `StateMachineHelper.cs`, `IL2CppOffsets.cs`

---

## FF1-Specific Notes

- **Magic:** Spell slots per level (x/y charges), not MP pool
- **Jobs:** Fighter, Thief, Black Mage, White Mage, Red Mage, Monk
- **Party:** Fixed 4 Light Warriors named at start
- **Vehicles:** Ship, Canoe, Airship (no chocobo)
- **Translations:** Japanese entity names via embedded dictionary in `EntityTranslator.cs`

---

## Open-issues pass (2026-09-23, session 2) — status

All rows are **not yet verified in game**. Details, hooks and reasoning: `docs/debug.md` → "Open-issues pass (2026-09-23, session 2)".

| Item | Status | Where |
|------|--------|-------|
| Per-frame `FindObjectOfType` for `FieldPlayerController` off-field | Changed: per-frame caller rescans at most every 30 frames; key presses still rescan at once | `InputManager.IsOnValidMap(onDemand)` |
| Tab cleared battle state mid-battle | Changed: clears only when no active `BattleController` exists | `InputManager.HandleTabKey`, `BattleStateHelper.IsBattleControllerAlive` |
| Unplugging the controller in mod mode locked the keyboard out | Changed: no gamepad → MOD_MODE (and a MOD_MENU with the menu closed) drops to NORMAL | `ControllerRouter.ReleaseControllerOwnedState` |
| F8 mod menu left D-pad / stick / LT acting underneath | Changed: `ModMenu.Open/Close` sync the controller state (MOD_MENU / NORMAL) | `ControllerRouter.SyncWithModMenu` |
| English literals in speech | Changed: battle damage/heal/miss, turn, entity names/types, bestiary, save slots, controls screen, controller button names, fallbacks — all through `T()` | many files; keys listed in debug.md |
| Value-0 battle views | Changed: `HitType.Zero` → "{target}: 0 damage"; `RecoveryCondition` → "{target}: cured" (FF1 never emits it); value-0 `Hit` (buffs, FF1 status cures) stays silent | `BattleMessagePatches.CreateDamageView_Postfix` |
| H in battle read stale HP/status | Changed: reads the battle unit's live parameter (`BattleUnitDataInfo.Parameter`), falls back to `OwnedCharacterData.Parameter` | `FFI_ScreenReaderMod.AnnounceCharacterStatus` |
| New-game job names were English in every language | Changed: game's own localized job name first, English fallback | `NewGameHelpers.GetJobNameById` |
| Multi-hit default ("Total only" in FF1, "With hit count" in FF2–FF5) | Not changed — user decision | `PreferencesManager.DamageDisplay` |
