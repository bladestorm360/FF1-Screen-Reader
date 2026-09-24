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
| Value-0 battle views | Changed: `HitType.Zero` → "{target}: 0 damage"; value-0 `Hit` (buffs, FF1 status cures) stays silent. (The `RecoveryCondition` → "{target}: cured" branch was removed in round 2: FF1 never emits it; cures are now announced by the status-removal hook.) | `BattleMessagePatches.CreateDamageView_Postfix` |
| H in battle read stale HP/status | Reverted in round 2: the extra battle-unit lookup was redundant — `OwnedCharacterData.Parameter` IS the live battle parameter (same object) | `FFI_ScreenReaderMod.AnnounceCharacterStatus` |
| New-game job names were English in every language | Changed: game's own localized job name first, English fallback | `NewGameHelpers.GetJobNameById` |
| Multi-hit default ("Total only" in FF1, "With hit count" in FF2–FF5) | Changed (coordinator commit 8328d4f): defaults to "With hit count", matching FF2–FF5, stored as the new `MultiHitDamage` entry so every install moves to the new default once | `PreferencesManager.DamageDisplay` |

## Round 2 (2026-09-24) — status

All rows are **not yet verified in game**. Details, hooks, RVAs and reasoning: `docs/debug.md` → "Round 2 (2026-09-24)".

| Item | Status | Where |
|------|--------|-------|
| Status removal | New: "{unit}: {condition} removed" for cures, natural wear-off and revive; silent for battle-end cleanup, statuses cleared by KO/Stone, never-applied statuses, unnamed conditions | `BattleConditionRemovalPatches` (BattleMessagePatches.cs) |
| "{0}: cured" | Removed (unreachable in FF1) with its mod_text key | `BattleMessagePatches.CreateDamageView_Postfix` |
| Title "Press any button" | Event-driven: armed on the "Title" scene load, spoken from `SystemIndicator.Hide` when the prompt shows (was a 0.15 s poll) | `TitleScreenPatches` |
| Battle initial target | Read at `EnemysInit`/`PlayerInit` (was per-frame `EnemysUpdate`/`PlayerUpdate`) | `BattleCommandPatches` |
| Item / Equip / Magic command bars | Read on the bar's own cursor-set method and on command-state entry (was per-frame `UpdateController`) | `CommandBarPatches` |
| Item list / item target initial focus | Bounded read started by the list's state-entry Init (was per-frame `UpdateController`) | `FieldItemReannouncePatches` (ItemMenuPatches.cs) |
| Magic spell list entry | Bounded read started by `UseListInit`/`ForgetInit`, no `FindObjectOfType` (was per-frame `UpdateController`) | `MagicMenuPatches` |
| Popups (common, game over) | Read on the popup's `SetCommandSelectCursor` (was per-frame `UpdateFocus`/`UpdateCommand`) | `PopupPatches` |
| Bestiary minimap open/close | Checked after `SetupKeyHelp`/`SetupEnlargedMapKeyHelp` (was per-frame `UpdateController`) | `BestiaryManualPatches` |
| Bestiary formation entry | Read when the party list is filled (`InitMonsterPartyList`) (was a 3 s poll) | `BestiaryManualPatches` |
| Gallery / Music Player entry item | Read from the focus event after the header (was a 2 s poll) | `GalleryManualPatches`, `MusicPlayerManualPatches` |
| Field toggles "Encounters on/off", "Run"/"Walk" | Spoken from the game's client setters, only for a real change on the field (Player state); config-menu changes and save loads stay silent (was a per-frame poll) | `GameToggleAnnouncer`, `GameStatePatches.IsFieldPlayerState` |
| Config row re-read after a popup / the bestiary | Popup close reads at once; bestiary return reads when the menu fades back in (was a per-frame `UpdateController` consume) | `ConfigController_SetActive_Patch`, `GameStatePatches` |
| Mod-menu controller state | Removed a duplicate state write (`ModMenu.Open/Close` already sync it) | `ControllerRouter.OpenModMenu/CloseModMenu` |
