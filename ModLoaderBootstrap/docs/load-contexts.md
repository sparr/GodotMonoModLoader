# Which load context the game is in

Measured against the real game under Proton. Re-check after a game update.

## The game is not in the default context

Godot loads the game assembly through hostfxr's
`load_assembly_and_get_function_pointer`, which places it in an isolated context:

```
[bootstrap] game load context: IsolatedComponentLoadContext(...\Atomcraft.dll) [isolated]
```

This is the single most important fact about loading anything into this game, because
getting it wrong fails *silently*. Loading the mod loader into
`AssemblyLoadContext.Default` binds a **second copy** of `Atomcraft.dll`, and Harmony
then patches methods on types the running game never touches. No exception, no warning,
no effect.

`ModLoaderBootstrap` therefore never assumes. It finds the game assembly by name across
every context (`AppDomain.CurrentDomain.GetAssemblies()` spans all of them), asks which
context that assembly is in, and loads `GodotMonoModLoader.dll` into that one.

## Why the bootstrap has no references

It is reached in more than one way, and not all of them put it in the game's context:

| Reached by | Lands in |
| --- | --- |
| `load_assembly_and_get_function_pointer` | an isolated context of its own |
| `Assembly.LoadFrom` from injected IL | the default context |

Either way it is on the wrong side of a context boundary from the game. A compile-time
reference to `GodotSharp` or `Atomcraft` would bind the copy in *its* context, not the
game's. So the bootstrap has no references at all and reaches across purely by
reflection; everything that touches game types lives in `GodotMonoModLoader.dll`, which
is loaded into the game's context and can reference them normally.

It also installs a `Resolving` handler on that context, because `0Harmony.dll` is not on
the game's probing path and mods expect it.

## Two entry points

`Initialize` is an ordinary managed method. `InitializeNative` wraps it with
`[UnmanagedCallersOnly]` for callers holding a function pointer.

One method cannot serve both. Calling an `[UnmanagedCallersOnly]` method from managed
code is a **fatal runtime error**, not a catchable exception:

```
Fatal error. Invalid Program: attempted to call a UnmanagedCallersOnly method from managed code.
```

The process aborts before the game prints a line, and because Godot buffers `godot.log`,
the log comes out empty — which makes it look like the game died for no reason at all.
No `try`/`catch` at the call site helps.
