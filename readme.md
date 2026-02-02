# FF1-screen-reader

## Purpose

Adds NVDA output, pathfinding, sound queues and other accessibility aides to Final Fantasy Pixel Remaster.

## Known Issues

Wall tones play brief false positives when opening doors or when transitioning between maps.
NPCs in shops are blocked by counters, but interacting with the counter opens the shop. Usually this is straight north from the door.
Shop menus are reading the first highlighted item on both entry and exit.
Items that can not be purchased due to a lack of gil are not reading, either upon highlight or the description by pressing I.
Secret passages, even when opened, do not show properly on the pathfinder. Can use wall bumps and estimation to find, usually near the opening mechanism.
H in battle announces statistics for all characters, not active character.
Level ups read HP gained, but not other statistics.
F1 key (walk/run toggle) only works in dungeons and towns. World map defaults to walking speed only - use vehicles (ship, canoe, airship) for faster world map travel.

## Install

Create an account at store.steampowered.com, login, join steam.
Once account is created, install steam download app (should be prompted to do so after account creation.)
Log into desktop app.
to purchase games, the easiest way is to use the web interface. You can search for a game when logged into the browser, purchase it there and will be asked if you want to install your games, which opens the desktop app to finish installation.
Ensure you purchase Final Fantasy, the page should mention being remastered in the description.
Install MelonLoader into game's installation directory. Ensure nightly builds are enabled.
https://melonloader.co/download.html
Copy NVDAControllerClient64.dll and tolk.dll into installation directory with game executable, usually c:\\Program Files (x86)\\Steam\\Steamapps\\common\\Final Fantasy PR.
If you created a steam library on another drive, the path will be Drive Letter\\Path to steam library\\SteamLibrary\\steamapps\\common\\Final Fantasy PR.
FFI\_screenreader.dll   goes in MelonLoader/mods folder.

## Keys

Game:

WASD or arrow keys: movement
Enter: Confirm
Backspace: cancel
Q: Random suggested name during character creation. Toggle between statistics and description in buy menu in shops.
f1: toggle between walk and run
f3: toggle random encounters on and off.

Mod:

J and L or \[ and ]: cycle destinations in pathfinder
Shift+J and L or - and =: change destination categories
\\ or p: get directions to selected destination
Shift+\\ or P: Toggle pathfinding filter so that not all destinations are visible, just ones with a valid path.
Shift+k: Reset category to all
': Toggle footsteps
;: toggle wall tones
9: toggle audio beacons
G: Announce current Gil
M: Announce current map.
H: In battle, announce character hp, mp, status effects.
I: In configuration  menu accessible from tab menu, read description of highlighted setting. In shop menus, reads description or stats or of highlighted item. . In item menu with equipment highlighted, reads which jobs can equip
V: Announce active vehicle state.
Ctrl+Arrow keys: Teleport to direction of selected entity (Ctrl+Up = north of entity, etc.)
f5: toggle between HP display: full numbers, percentages or no HP display. NO hp display is how the game is intended to be played by the developers.
f8: activate the mod menu where individual sound volume can be adjusted for mod sounds, as well as all togglable options.

When on a character's status screen:

up and down arrows read through statistics.
Shift plus arrows: jumps between groups, character info, vitals, statistics, combat statistics, progression.
control plus arrows: jump to beginning or end of statistics screen.

## Credits:
ZKline for making the original ff6 mod, without which I likely never would have tried modding these games.
Stirlock and UnexplainedEntity for help testing the builds before public release.
Stirlock for obtaining Japanese entity names so they could be translated.