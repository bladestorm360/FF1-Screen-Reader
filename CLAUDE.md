# FF1 Screen Reader - Engineering Rules

## HARD RULES

1. **Logs First** - Always check MelonLoader logs first when debugging issues
2. **No Large Files** - Never read `dump.cs` (~15MB) directly; use Grep only
3. **Build Command** - Always use: `powershell.exe -Command "& 'D:\Games\Dev\Unity\FFPR\ff1\ff1-screen-reader\build_and_deploy.bat'"`
4. **Log Files** - Don't use `Latest.log`; find newest timestamped log in `Final Fantasy PR\MelonLoader\Logs\`
5. **No shell file commands** - No dir/find/Get-ChildItem/ls; use Glob, Read, and Grep tools only

## Coding Rules

### Patching
- HarmonyPatch attributes for static types; manual patching for runtime discovery
- Prefix for state tracking, Postfix for announcements
- Use coroutines for one-frame delays (let game state settle)

### IL2CPP Constraints
- String parameters in patches crash - use positional params (`__0`, `__1`)
- Field access often fails - use pointer offsets from dump.cs (see debug.md)

### Performance
- **No polling/timer/per-frame patterns** - React to game events via Harmony patches
- Exceptions (may find hook-based workarounds later):
  - `InputManager.cs` polls for hotkey input
  - `SoundPlayer.cs` dedicated audio thread with queue
  - Wall tone system (periodic proximity polling)
  - `MapTransitionPatches.cs` polls FadeManager state for fade detection
  - `MovementSoundPatches.cs` consecutive collision tracking
- Cache components in GameObjectCache; never use FindObjectOfType in ShouldSuppress()
- String-only deduplication in ShouldAnnounce() - no Time.time

### Screen Reader Output
- `interrupt: true` for hotkeys, `interrupt: false` for game events
- Strip icon markup before speaking
- Lock TolkWrapper for all calls

## References

- **FF3 Reference:** `D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader`
- **Game Assemblies:** `Final Fantasy PR\MelonLoader\Il2CppAssemblies\`
- **Documentation:** See `docs/plan.md` (features), `docs/debug.md` (technical)
