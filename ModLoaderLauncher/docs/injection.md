# Injecting the hook into a suspended game

Measured against the real game under Proton 10.0. The hook this delivers is documented
in [`ModLoaderHook/docs/hostfxr-chain.md`](../../ModLoaderHook/docs/hostfxr-chain.md);
this covers only how it gets there.

## The sequence

`ModLoaderLauncher.exe` creates the game with `CREATE_SUSPENDED`, loads
`ModLoaderHook.dll` into it, calls its `InitializeHook` export, and only then resumes the
main thread.

Starting suspended is the whole point. A suspended process has executed no code, so
hostfxr cannot already be loaded and the hook is always installed in time. Nothing about
this depends on what the game goes on to do, which is what makes it work in every display
mode, including `--headless`.

## Cross-process injection works under Wine

This was the open risk, since Wine's implementation of these calls is a historically
rough corner. It works, and the whole sequence completes in both display modes:
`CreateProcess(CREATE_SUSPENDED)`, `VirtualAllocEx`, `WriteProcessMemory`,
`CreateRemoteThread`, `CreateToolhelp32Snapshot` with `Module32FirstW`/`Module32NextW`,
and `ResumeThread`.

## Two remote threads, not one

`LoadLibraryW` maps the hook but starts nothing, because Native AOT runs no managed code
at DLL attach. A second remote thread therefore calls `InitializeHook` directly.

Its address is the module's base in the target plus the export's RVA:

- **The base** comes from a Toolhelp32 module snapshot of the target. The exit code of
  the `LoadLibraryW` thread cannot carry it: a thread exit code is 32 bits, and module
  bases under Wine sit around `0x00006FFF_xxxxxxxx`.
- **The RVA** is read out of the DLL on disk by `PeFile`. Loading the hook locally and
  subtracting would be shorter, but it is itself a Native AOT image, and mapping a second
  one into the launcher invites two runtimes initializing over each other.

`kernel32` is mapped at the same address in every process of a session, on Windows and
under Wine alike, so the launcher's own `LoadLibraryW` address is valid in the target.

## Injection failure stops the game

If injection fails the launcher terminates the process rather than resuming it. Starting
a save that expects mods with no mods loaded is worse than not starting at all.

## Launching through Steam

On Linux/Proton, `%command%` ends with the path to the game executable, so the launcher
can be substituted into it:

```
bash -c 'exec "${@/AtomCraft.exe/ModLoaderLauncher.exe}"' -- %command% -s GodotMonoModLoader.gd
```

On Windows, Steam appends launch options as arguments and cannot replace the executable.
Run `ModLoaderLauncher.exe` directly, or add it to Steam as a non-Steam game. A game not
started by Steam also needs a `steam_appid.txt` containing `2803490` beside the
executable for Steamworks to initialize; that file is not one the game ships, so adding
it is safe. This path is untested here, having no Windows machine to hand.
