# FF1-screen-reader

## Purpose

Adds NVDA output, pathfinding, sound queues and other accessibility aides to Final Fantasy Pixel Remaster.

## Known Issues

NPCs in shops are blocked by counters, but interacting with the counter opens the shop. Usually this is straight north from the door.

Secret passages, even when opened, do not show properly on the pathfinder. Can use wall bumps and estimation to find, usually near the opening mechanism.

## Install

Create an account at store.steampowered.com, login, join steam.

Once account is created, install steam download app (should be prompted to do so after account creation.)

Log into desktop app.

to purchase games, the easiest way is to use the web interface. You can search for a game when logged into the browser, purchase it there and will be asked if you want to install your games, which opens the desktop app to finish installation.

Ensure you purchase Final Fantasy, the page should mention being remastered in the description.

Install MelonLoader into game's installation directory. Ensure nightly builds are enabled.
https://github.com/LavaGang/MelonLoader/releases

Copy NVDAControllerClient64.dll, tolk.dll and SDL3.dll into installation directory with game executable, usually c:\\Program Files (x86)\\Steam\\Steamapps\\common\\Final Fantasy PR.

If you created a steam library on another drive, the path will be Drive Letter\\Path to steam library\\SteamLibrary\\steamapps\\common\\Final Fantasy PR.

FFI\_screenreader.dll   goes in MelonLoader/mods folder.

waypoints.json (optional) goes in the game install's UserData folder (Final Fantasy PR\\UserData\\waypoints.json). It contains pre-marked waypoints from a playthrough — town docks, landing sites, key dungeon transitions — that you can cycle with , and . (comma / period) or D-pad. Skip the file if you'd rather start with an empty waypoint list and mark your own. The mod creates the UserData folder automatically on first run if it doesn't already exist.

## Keys

### Game

- WASD or arrow keys: movement
- Enter: Confirm
- Backspace/escape: cancel
- Q: Random suggested name during character creation. Toggle between statistics and description in menus that have them.
- f1: toggle between walk and run (dungeons and towns only; world map is walk-only by design — use vehicles for faster world travel).
- f3: toggle random encounters on and off.

### Mod

- J and L or \[ and ]: cycle destinations in pathfinder
- Shift+J and L or - and =: change destination categories
- \\ or p: get directions to selected destination
- Shift+\\ or P: Toggle pathfinding filter so that not all destinations are visible, just ones with a valid path.
- Ctrl+\\ or Ctrl+P: Toggle layer transition filter (hides stairs and layer-change destinations from navigation).
- K: announce the currently selected entity again.
- Backtick (the key above Tab): rescan nearby entities.
- Shift+k: Reset category to all
- ': Toggle footsteps
- ;: toggle wall tones
- f6: toggle audio beacons
- G: Announce current Gil
- M: Announce current map.
- Shift+M: Toggle map exit filter so multiple exits to the same place collapse to the nearest one.
- H: In battle, announce active character hp, mp, status effects.
- R: Repeat the current dialogue or message.
- I: In configuration  menu accessible from tab menu, read description of highlighted setting. In shop menus, reads description or stats or of highlighted item. . In item menu with equipment highlighted, reads which jobs can equip. In the 15 puzzle minigame, announces the row and column of the highlighted tile.
- Shift+I: announce the controls for the current screen.
- V: Announce active vehicle state.
- Ctrl+Arrow keys: Teleport to direction of selected entity (Ctrl+Up = north of entity, etc.)

### Waypoints (field only)

- , (comma): previous waypoint.
- . (period): next waypoint.
- Shift+, : previous waypoint category.
- Shift+. : next waypoint category.
- / (slash): pathfind to current waypoint.
- Shift+/ : add new waypoint at current location (prompts for name).
- Ctrl+. : rename current waypoint.
- Ctrl+/ : remove current waypoint.
- Ctrl+Shift+/ : clear all waypoints for the current map.

### Other toggles

- f5: toggle between HP display: full numbers, percentages or no HP display. NO hp display is how the game is intended to be played by the developers.
- f7: Toggle autodetail mode. Reads stats/descriptions of items and spells when navigating instead of pressing I to request the information.
- f8: activate the mod menu where individual sound volume can be adjusted for mod sounds, as well as all togglable options.

### When on a character's status screen

- up and down arrows read through statistics.
- Shift plus arrows: jumps between groups, character info, vitals, statistics, combat statistics, progression.
- control plus arrows: jump to beginning or end of statistics screen.

### Game controller

Button names are shown Xbox (PlayStation). Nintendo Pro / Joy-Con labels are also recognized — the controller type is auto-detected.

- Left Stick: movement on field, navigation in menus.
- D-pad: menu navigation only. On the field, D-pad is repurposed by the mod for waypoint cycling (see Mod controller below).
- A (Cross): Confirm.
- B (Circle): Cancel.
- X (Square): Shortcut — random name in character creation, statistics/description toggle in shop buy menus (keyboard Q equivalent).
- Y (Triangle): Open / close the field menu.
- LB / RB (L1 / R1): Tab switching in menus.
- LT (L2): Page up in non-field menus. On the field, LT is reserved by the mod for pathfinding (see Mod controller).
- RT (R2): Open the pause menu on the field and in battle. In non-field menus, page down.
- L3 (Left Stick Click): toggle random encounters (keyboard F3 equivalent) — only when Stick-click normalization is enabled in the Mod Menu.
- R3 (Right Stick Click): toggle walk/run (keyboard F1 equivalent) — only when Stick-click normalization is enabled.

### Mod controller

- Back/Select: Mod mode
- Start/Menu: Mod menu

#### Mod Mode combos (press Back/select, then one of the following)

##### In battle

- X (Square): announce active character HP, MP, and status (keyboard H equivalent).

##### On field

- X (Square): announce current Gil.
- Y (Triangle): announce current map or location.
- A (Cross): announce active vehicle state.
- Right Stick Up / Down / Left / Right: teleport 16 tiles in that direction.

##### While a dialogue or message window is open

- X (Square): repeat the current message (keyboard R equivalent).

#### Stick-click mod actions (preference-controlled in the Mod Menu)

##### When stick-click normalization is OFF (default)

- L3 (Left Stick Click): toggle audio beacons.
- R3 (Right Stick Click): toggle pathfinding filter.

##### When stick-click normalization is ON

- Back + L3: toggle audio beacons.
- Back + R3: toggle pathfinding filter.
- (L3 / R3 alone become game functions — encounter toggle / walk-run toggle.)

#### Normal-state mod actions (no Mod button needed)

##### Field — waypoint navigation

- D-pad Up / Down: previous / next waypoint.
- D-pad Left / Right: previous / next waypoint category.

##### Field — entity scanning

- Right Stick Up / Down: previous / next entity.
- Right Stick Left / Right: previous / next entity category.

##### Field — pathfinding

- Left Trigger (L2 / ZL): pathfind to last selected target / restart beacon.

##### In menus (status, bestiary, shop, configuration, and the Mod Menu)

- Right Stick Up: read item details (keyboard I equivalent).
- Right Stick Down: announce context controls / help for the current screen (keyboard Shift+I equivalent).
- Right Stick Left: announce "usable by" classes for highlighted equipment (keyboard U equivalent).
- D-pad Up / Down or Left Stick Up / Down: previous / next stat in the status and bestiary detail screens.

##### Mod Menu navigation (after Start opens it)

- D-pad or Left Stick Up / Down: navigate items.
- D-pad or Left Stick Left / Right: decrease / increase value.
- A (Cross): toggle / confirm.
- B (Circle) or Start: close the Mod Menu.

## Credits:

- ZKline for making the original ff6 mod, without which I likely never would have tried modding these games.
- Stirlock and UnexplainedEntity for help testing the builds before public release.
- Stirlock for obtaining Japanese entity names so they could be translated.
