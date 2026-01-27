# FF1 Screen Reader - Engineering Rules

## HARD RULES

1. **Logs First** - Always check MelonLoader logs first when debugging issues
2. **No Large Files** - Never read `dump.cs` (~15MB) directly; use Grep only. Path: `D:\Games\Dev\Unity\FFPR\ff1\dump.cs`
3. **Build Command** - Always use: `powershell.exe -Command "& 'D:\Games\Dev\Unity\FFPR\ff1\ff1-screen-reader\build_and_deploy.bat'"`
4. **Log Files** - Don't use `Latest.log`; find newest timestamped log in `Final Fantasy PR\MelonLoader\Logs\`
5. **No shell file commands** - No dir/find/Get-ChildItem/ls; use Glob, Read, and Grep tools only
6. **Stay In Bounds** - Only work within `D:\Games\Dev\Unity\FFPR\ff1\` and `D:\Games\Dev\Unity\FFPR\ff1\ff1-screen-reader\`. NEVER search the game installation directory (`Final Fantasy PR\`), Steam library, parent directories, or any directory above `D:\Games\Dev\Unity\FFPR\ff1\`. The game directory is deployment-only — never search it. All decompiled output and analysis files are within the ff1 workspace.
7. **No FF3 Reference** - NEVER reference or search the ff3 codebase unless the user explicitly asks to port code from that mod.

## Coding Rules

- **Patching:** HarmonyPatch attributes for static types; manual for runtime. Prefix=state tracking, Postfix=announcements. Coroutines for one-frame delays.
- **IL2CPP:** String params in patches crash — use `__0`/`__1`. Field access fails — use pointer offsets (see debug.md).
- **Performance:** No polling/per-frame patterns. Exceptions: `InputManager.cs`, `SoundPlayer.cs`, `MapTransitionPatches.cs`, `MovementSoundPatches.cs`.
- **Screen reader:** `interrupt: true` for hotkeys, `false` for game events. Strip icon markup. Lock TolkWrapper.

## References

- **FF3 Reference:** `D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader`
- **dump.cs:** `D:\Games\Dev\Unity\FFPR\ff1\dump.cs` (grep only, never read directly)
- **Game Install (deploy only):** `Final Fantasy PR\` — never search, only used by build_and_deploy.bat
- **Documentation:** See `docs/plan.md` (features/structure), `docs/debug.md` (architecture/offsets/changelog)
