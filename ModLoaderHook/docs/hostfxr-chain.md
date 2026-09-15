# Getting in front of Godot's call into hostfxr

Measured against the real game under Proton 10.0, not read out of source. Re-measure
after a game or Proton update.

## Godot's startup, for reference

```
load_hostfxr  ->  initialize_hostfxr_self_contained
  hostfxr_initialize_for_dotnet_command_line
  hostfxr_get_runtime_delegate            // "load_assembly_and_get_function_pointer"
  -> Atomcraft.dll :: GodotPlugins.Game.Main :: InitializeFromGameProject
```

`GodotPlugins.Game.Main` is compiled *into* `Atomcraft.dll` by the Godot SDK; an exported
game has no separate `GodotPlugins.dll`, which is why there is nothing to swap out at
that level. `InitializeFromGameProject` installs the DllImport resolver, initializes
`NativeFuncs` and the managed callbacks, and registers the game assembly's scripts.

`hostfxr.dll` is loaded by name from the game's `data_*` directory, and Godot resolves
its entry points with `GetProcAddress`.

## The chain

Because Godot uses `GetProcAddress`, rewriting the export address table entry for
`hostfxr_get_runtime_delegate` intercepts the lookup. The one write is a 32-bit RVA. No
inline patching, no trampolines, no import-table hooking.

Loading hostfxr here rather than waiting also removes the need for a `LoadLibrary` hook:
when Godot loads it by the same path it receives the module already mapped, and its
`GetProcAddress` reads the patched table.

From there it is three ordinary function-pointer swaps:

| Wrapped | So that |
| --- | --- |
| `hostfxr_get_runtime_delegate` | the returned delegate can be wrapped in turn |
| `load_assembly_and_get_function_pointer` | the game's entry point can be wrapped |
| `godotsharp_game_main_init` | the bootstrap runs after Godot's own init succeeds |

The last one lets Godot finish first, deliberately. Before it returns, `NativeFuncs` is
uninitialized and any managed code touching Godot would crash. After it returns, the
managed bridge is live and the main scene still does not exist, so Harmony patches land
before `Game._Ready` runs and before `Materials.Init` reads its data.

## Nothing here can act on DLL attach

Native AOT runs no managed code when the DLL is loaded. Measured with a probe carrying
both a `[ModuleInitializer]` and an export:

```
--- after LoadLibrary, before export call ---
  (no log yet)
--- after export call ---
  log: MODULE_INITIALIZER
  log: EXPORT_CALLED
```

Declaring `[UnmanagedCallersOnly(EntryPoint = "DllMain")]` does not help either: the
Native AOT runtime pack already defines that symbol in `dllmain.obj`, and the link fails
with a duplicate definition.

So whatever delivers this DLL into the process must call `EnsureInitialized` explicitly,
and must do it before Godot loads hostfxr. That requirement is the entire difference
between the approaches built on top of this.

## Failure is quiet, so check the log

The hook writes `GodotMonoModLoader.startup.log` next to the executable. It has to: all
of this happens before Godot's managed bridge exists, so `GD.Print` is not available and
`godot.log` has not been opened.

A missing startup log means the hook never ran at all. Note also that Godot buffers
`godot.log`, so a process that aborts during startup leaves it **empty** rather than
truncated — an empty log is a symptom, not an absence of information.
