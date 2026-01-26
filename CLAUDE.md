# FF1 Screen Reader - Engineering Rules

## HARD RULES

1. **Logs First** - Always check MelonLoader logs first when debugging issues
2. **No Large Files** - Never read `dump.cs` (~15MB) directly; use Grep only
3. **Build Command** - Always use: `powershell.exe -Command "& 'D:\Games\Dev\Unity\FFPR\ff1\ff1-screen-reader\build_and_deploy.bat'"`
4. **Log Files** - Don't use `Latest.log`; find newest timestamped log in `Final Fantasy PR\MelonLoader\Logs\`
5. **No shell file commands** - No dir/find/Get-ChildItem/ls; use Glob, Read, and Grep tools only

## Coding Rules

- **Patching:** HarmonyPatch attributes for static types; manual for runtime. Prefix=state tracking, Postfix=announcements. Coroutines for one-frame delays.
- **IL2CPP:** String params in patches crash — use `__0`/`__1`. Field access fails — use pointer offsets (see debug.md).
- **Performance:** No polling/per-frame patterns. Exceptions: `InputManager.cs`, `SoundPlayer.cs`, `MapTransitionPatches.cs`, `MovementSoundPatches.cs`.
- **Screen reader:** `interrupt: true` for hotkeys, `false` for game events. Strip icon markup. Lock TolkWrapper.

## Directory Search Boundaries

- Do not search above one directory back from working directory without explicit permission
- Do not search game installation directory or parent directories without being explicitly asked
- Do not reference ff3 unless explicitly porting code from that mod

## References

- **FF3 Reference:** `D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader`
- **Game Assemblies:** `Final Fantasy PR\MelonLoader\Il2CppAssemblies\`
- **Documentation:** See `docs/plan.md` (features/structure), `docs/debug.md` (architecture/offsets/changelog)
