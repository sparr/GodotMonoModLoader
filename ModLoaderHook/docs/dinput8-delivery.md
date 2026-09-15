# Delivering the hook as a dinput8 stand-in

Measured against the real game under Proton 10.0. The hook this delivers is documented in
[`hostfxr-chain.md`](hostfxr-chain.md); this covers only how it gets into the process.

Method: `WINEDEBUG=+loaddll,+dinput` on a harness run, reading `$OUT/launch.log`.

## Being resident early

`AtomCraft.exe` imports `DINPUT8.dll` statically, so the loader maps it during process
initialization, long before anything the mod loader cares about:

| Log line | Module |
| --- | --- |
| 234 | `C:\windows\system32\DINPUT8.dll` |
| 311 | `data_Atomcraft_windows_x86_64\hostfxr.dll` |
| 313 | `coreclr.dll` |
| 333 | `Atomcraft.dll` |
| 336 | `GodotSharp.dll` |

Steam does not ship `dinput8.dll`, so a copy beside the executable is a new file rather
than a modified one, and nothing reverts it.

## The trigger, and its limit

Being mapped is not enough: Native AOT runs no managed code at DLL attach, so the hook
only starts when the game calls an export.

In a **windowed** run the game does, before loading hostfxr. Two `DirectInput8Create`
calls precede it, and the second is the game's own — `hinst 0000000140000000` is the
standard 64-bit PE image base, and it runs on thread `0134`, the same thread that goes on
to load hostfxr. Same thread means sequential ordering, not a race:

| Log line | Event | Thread |
| --- | --- | --- |
| 293 | `DirectInput8Create` (Wine-internal caller) | 014c |
| 336 | `DirectInput8Create` from `AtomCraft.exe` | 0134 |
| 348 | `hostfxr.dll` loaded | 0134 |

In a **headless** run it does not. No DisplayServer is created, so `DirectInput8Create`
is never called and the hook never initializes. The file is loaded either way:

```
Loaded L"Z:\\...\\install\\DINPUT8.dll" ... : native
```

but nothing invokes it. No `GodotMonoModLoader.startup.log` is produced at all, and the
game reports the vanilla `Loaded materials: 1913` instead of `1919`. The single
`DirectInput8Create` seen in a headless trace comes from a Wine-internal module and goes
to the system builtin, not to this file.

**This is the approach's hard limit.** It rests on a promise about game behaviour that a
headless run does not keep. Fixing it needs a second stand-in for some other DLL whose
export *is* called before hostfxr in headless. Counting real calls (not the callee's own
`DllMain`) before the hostfxr load:

| Channel | Calls before hostfxr | Notes |
| --- | --- | --- |
| `bcrypt` | 10 | `BCryptGenRandom`; the exe imports exactly this one function |
| `iphlpapi` | 3 | first call precedes the game process; likely `wineboot` |
| `winmm` | 0 | only the DLL's own `DllMain` appears |
| `dwmapi` | 0 | windowing only, as expected |

`bcrypt` is the plausible target and a much larger commitment: a stand-in must export
every symbol its importers use, and `dinput8` needs only five while CoreCLR and
`System.Security.Cryptography` P/Invoke a large part of `bcrypt`.

## Wine requires an explicit override

Wine resolves `dinput8` to its own builtin from `system32` and ignores a file next to the
executable. Without an override:

```
Loaded L"C:\\windows\\system32\\DINPUT8.dll" ... : builtin
```

With `WINEDLLOVERRIDES="dinput8=n,b"`:

```
Loaded L"Z:\\...\\install\\DINPUT8.dll" ... : native
```

Native Windows searches the application directory ahead of `system32` for DLLs that are
not KnownDLLs, and `dinput8` is not one, so no equivalent setting is needed there.
