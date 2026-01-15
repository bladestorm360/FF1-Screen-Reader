# FF1 Screen Reader - Accessibility Mod

## HARD RULES - NEVER VIOLATE

### 1. Logs First
**Always check logs first after any test attempt if the user reports an issue.** This is always the first step to debugging - no exceptions.

### 2. Large File Handling
**Never load large files into memory** (larger than 50KB or 500 lines). Only use external tools (Grep, pattern searching) to search for patterns or keywords in these large files. The `dump.cs` file is ~15MB and must never be read directly.

### 3. CMD Terminal Limitations
**This is a CMD terminal with limited PowerShell access.** Some commands will not work. If a command exits with an error code:
1. STOP current work immediately
2. Find a workaround
3. Document the workaround below before continuing

### 4. Build Process
**Always use `build_and_deploy.bat` - never use `dotnet build` directly.**
```
powershell.exe -Command "& 'D:\Games\Dev\Unity\FFPR\ff1\ff1-screen-reader\build_and_deploy.bat'"
```

#### Known Command Workarounds
| Failed Command | Workaround | Notes |
|----------------|------------|-------|
| `cmd.exe //c build_and_deploy.bat` | Use PowerShell instead (see above) | Bash shell can't invoke .bat files via cmd.exe |
| `sed -n 'X,Yp' file` | Use `grep -n` or Read tool | sed doesn't work in this environment |

### 5. Reading MelonLoader Logs
**Do not use `Latest.log` - it doesn't update reliably.** Instead, find the newest log file by timestamp:
```bash
# List logs sorted by time (newest first)
ls -lt "D:\Games\steamlibrary\steamapps\common\Final Fantasy PR\MelonLoader\Logs" | head -5

# Then grep the latest log file (e.g., 26-1-11_10-52-24.log)
grep "pattern" "D:\Games\steamlibrary\steamapps\common\Final Fantasy PR\MelonLoader\Logs\26-1-11_10-52-24.log"
```

---

## Project Overview

A MelonLoader accessibility mod making Final Fantasy I Pixel Remaster playable for blind users via screen reader integration (Tolk). This is a direct port of the ff3-screen-reader project, adapting its features for FF1's specific game assemblies and mechanics.

**Tech Stack:**
- Framework: .NET 6.0
- Mod Loader: MelonLoader (Il2Cpp interop)
- Patching: HarmonyLib 2.x
- Screen Reader: Tolk.dll (NVDA, JAWS, Narrator, SAPI)
- Game: Final Fantasy PR (unified launcher at `D:\Games\steamlibrary\steamapps\common\Final Fantasy PR`)

**Reference Implementation:** `D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader`

## Implementation Priority

### 1. Menus (Highest Priority)
- **Title Screen** - Game selection, new game, load game, config
- **Main Menu** - Items, Magic, Equipment, Status, Formation, Config, Save
- **Config Menu** - Settings navigation with value announcements
- **Save/Load Slots** - Character names, playtime, level info
- **Character Selection** - Party/formation screens
- **Equipment/Item Menus** - Selection and comparison

### 2. Dialogue & Text
- **Scrolling Intro Text** - Opening narrative (ScrollMessagePatches)
- **Message Windows** - NPC dialogue, story text
- **Speaker Names** - Character identification in conversations
- **System Messages** - Prompts, confirmations

### 3. Navigation & Pathfinding
- **Entity Scanning** - Chests, NPCs, exits, save points, events
- **Category Filtering** - Filter entities by type
- **Pathfinding** - Direction/distance to targets using MapRouteSearcher
- **Map Announcements** - Current location name

### 4. Battle (Lowest Priority)
- **Command Selection** - Attack, Magic, Item, Defend, Run
- **Target Selection** - Enemy/ally targeting with HP info
- **Action Announcements** - "X uses Y on Z"
- **Damage/Healing Output** - Numeric feedback
- **Status Effects** - Condition changes
- **Battle Results** - Victory, defeat, rewards

## Project Structure

```
ff1-screen-reader/
├── Core/
│   ├── FFI_ScreenReaderMod.cs    # Main mod entry point
│   ├── InputManager.cs           # Hotkey handling
│   └── Filters/                  # Entity filtering system
├── Field/
│   ├── EntityScanner.cs          # Field entity discovery
│   ├── NavigableEntity.cs        # Entity types
│   └── FieldNavigationHelper.cs  # Pathfinding integration
├── Menus/
│   ├── MenuTextDiscovery.cs      # Multi-strategy text detection
│   ├── SaveSlotReader.cs         # Save/load parsing
│   └── ConfigMenuReader.cs       # Settings value extraction
├── Patches/
│   ├── TitleMenuPatches.cs       # Title screen
│   ├── CursorNavigationPatches.cs
│   ├── MessageWindowPatches.cs   # Dialogue
│   ├── ScrollMessagePatches.cs   # Intro text
│   ├── BattleCommandPatches.cs
│   ├── BattleMessagePatches.cs
│   └── BattleResultPatches.cs
└── Utils/
    ├── TolkWrapper.cs            # Thread-safe screen reader calls
    ├── SpeechHelper.cs           # Delayed announcements
    ├── TextUtils.cs              # Icon stripping, formatting
    ├── GameObjectCache.cs        # Component caching
    └── CoroutineManager.cs       # Coroutine lifecycle
```

## Coding Rules

### General
- Port FF3 code directly, adapting only game-specific references
- Use FF1's game assemblies (check `dump.cs` and `Il2CppAssemblies/` for class names)
- Maintain identical hotkey mappings for user consistency
- All public classes/methods require XML documentation

### Patching
- Use HarmonyPatch attributes for static, discoverable types
- Use manual patching (reflection) for types requiring runtime discovery
- Prefix patches for state tracking, Postfix for announcements
- One-frame delays via coroutines to let game state settle

### Screen Reader Output
- `interrupt: true` for user-initiated actions (hotkeys)
- `interrupt: false` for game events (let queue)
- Deduplicate identical messages within short timeframes
- Strip icon markup from game text before speaking

### Performance
- Cache component lookups in GameObjectCache
- Throttle entity scans (5-second intervals, manual trigger allowed)
- Limit concurrent coroutines (max 20)
- Early-exit input handling when no keys pressed

### Thread Safety
- Lock TolkWrapper for all screen reader calls
- Lock GameObjectCache for all cache operations
- Validate cached objects aren't destroyed before use

## Key Hotkeys (Match FF3)

| Key | Function |
|-----|----------|
| J/[ | Previous entity |
| K | Repeat current |
| L/] | Next entity |
| Shift+J/L | Cycle category |
| P/\ | Announce with path |
| Shift+P | Toggle pathfinding filter |
| M | Current map name |
| H | Party HP/MP status |
| G | Current gil |
| 0 | Reset to All category |
| =/- | Next/prev category |

## FF1-Specific Considerations

- FF1 uses job system (Fighter, Thief, Black Mage, etc.) - adapt character readers
- Class change mechanic mid-game - handle job name updates
- Four Light Warriors naming screen at start
- Simpler battle system than FF3 (no job abilities)
- Different vehicle set (Ship, Canoe, Airship)

## Development Resources

- FF1 dump: `D:\Games\Dev\Unity\FFPR\ff1\dump.cs`
- Game assemblies: `D:\Games\steamlibrary\steamapps\common\Final Fantasy PR\MelonLoader\Il2CppAssemblies\`
- FF3 reference: `D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader\`
