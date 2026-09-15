
This is a Mod Loader for the game Atomcraft.

# Quick Start

1. Extract everything from `GodotMonoModLoader.zip` into the game installation
   folder, next to `AtomCraft.exe`.
2. Install mod zips (don't extract them) into
   `%AppData%/Godot/app_userdata/Atomcraft/Mods`.
   Mods can also be installed in `Atomcraft/Mods/`.
3. In Steam, right click the game in the Library, visit Propertes > General,
   then enter `-s GodotMonoModLoader.gd` in the Launch Options field (or
   `WINEDLLOVERRIDES="dinput8=n,b" %command% -s GodotMonoModLoader.gd` for
   Linux)
4. Launch the game, see the mod loader window, enjoy!

# Launch Alternatives

Everything in this section is optional and meant for unusual use cases.

Each of these gets the mod loader running inside the game; they differ in what
they touch and what they need from you. You can choose a different one for each
game launch.

| Method              | Edits game files | Needs `-s` | `--headless` | Additional requirements                                 |
| ------------------- | ---------------- | ---------- | ------------ | ------------------------------------- |
| `ModLoaderLauncher` | **no**           | **no**     | **yes**      | Steam must not launch `AtomCraft.exe` |
| `dinput8.dll`       | **no**           | yes        | no           | `WINEDLLOVERRIDES` on Proton          |
| `AtomcraftPatcher`  | yes              | yes        | **yes**      | **none**                              |

<!-- Each way to start the mod loader documents itself here, as a "## "
     subsection of this one, added by whichever branch introduces it. Keep to
     this shape so the sections read as alternatives rather than steps:

       ## <Name of the mechanism>
       One sentence on what it does.
       Pro:
       Con:
       Additional details

     The rest of this file is shared, so add nothing outside this section. -->

## ModLoaderLauncher

Starts the game suspended, injects the mod loader into it, and lets it run.

Pro:
- Does not modify any game files
- Does not require launch options
- Supports `--headless` for testing, future multiplayer servers, etc
- Most likely to continue working with future game versions

Con:
- Convincing Steam to run it instead of `AtomCraft.exe`
  - Linux
    - Option 1
      - Launch Options:
        `bash -c 'exec "${@/AtomCraft.exe/ModLoaderLauncher.exe}"' -- %command%`
    - Option 2
      - Create `steam_appid.txt` containing `2803490` beside `AtomCraft.exe`
      - Run `ModLoaderLauncher.exe` via `proton`
  - Windows
    - Create `steam_appid.txt` containing `2803490` beside `AtomCraft.exe`, then
      - Run `ModLoaderLauncher.exe` directly, or
      - Add `ModLoaderLauncher.exe` as a Non-Steam Game in your Library

Additional command line options: `--no-script` starts the game unmodded through
the launcher, `--help` lists the rest.

## dinput8.dll

Stands in for a Windows DLL the game loads at startup, and starts the mod loader
from there.

Pro:
- Does not modify any game files
- Simplest setup

Con:
- Requires launch options
  - Windows: `-s GodotMonoModLoader.gd`
  - Linux/Proton:
    `WINEDLLOVERRIDES="dinput8=n,b" %command% -s GodotMonoModLoader.gd`
- No `--headless` mode
- Most likely to break after an update
- Least log feedback on mod loader failure

## AtomcraftPatcher

Adds a call to the mod loader inside the game's own `Atomcraft.dll`.

Pro:
- Run just once (per update)
- Requires no Steam configuration changes
- Supports `--headless` for testing, future multiplayer servers, etc

Con:
- Modifies game files (with a backup).
  Must re-run after a game update or Steam file integrity check

Patching can be scripted. When stdin is not a terminal, the patcher skips its
"Press ANY key" prompt and reports the outcome through its exit code:

| Option                    | Effect |
| ------------------------- | ------ |
| `-y`, `--non-interactive` | Never wait for a keypress, even on a terminal. |
| `--restore`               | Restore the backup instead of patching. |
| `--fail-if-patched`       | Exit 6 instead of 0 if already patched. |
| `--quiet`                 | Suppress progress output, but not errors. |
| `-h`, `--help`            | Show usage. |

| Exit code | Meaning |
| :-------: | ------- |
|     0     | Patch applied, or already patched |
|     1     | Unclassified failure |
|     2     | Usage error |
|     3     | `Atomcraft.dll` or backup not found |
|     4     | `Atomcraft.dll` is not a build this patcher understands |
|     5     | Old patch detected, restore required |
|     6     | Already patched, with `--fail-if-patched` |

A successful patch always exits 0, so
`AtomcraftPatcher [...] < /dev/null || exit 1` is enough to catch a failure. Use
`--fail-if-patched` when a no-op needs telling apart from work actually done.

# Troubleshooting

With the mod loader running, the game shows a Mod Loader report on startup, and
the same messages go to `godot.log`. A `--headless` run has no window, so read
the log there instead. Which symptom you get says where the problem is.

**No report at all, and the game plays as normal.** The mod loader itself never
ran, so `-s GodotMonoModLoader.gd` is missing from the launch options and was
not provided by your chosen launch alternative. Try [Quick Start](#quick-start)
again.

**The report says the Mod Loader did not start, and there is no
`GodotMonoModLoader.startup.log`.** Did you use `dinput8.dll` and `--headless`?
Use `ModLoaderLauncher` or `AtomcraftPatcher` instead. If you already did,
re-check the requirements in [Launch Alternatives](#launch-alternatives).

**The report says the Mod Loader did not start, and
`GodotMonoModLoader.startup.log` exists.** Something started but did not finish.
The log says how far it got.

**The report lists your mods with errors.** The loader is working and the
problem is in a mod. The report's own error text says which, and the log tab has
the detail.

If none of that fits, check that every file from `GodotMonoModLoader.zip` was
extracted next to the game executable.

---

# Mods

Here is a list of the mods I've made:
[AtomcraftMods](https://github.com/sacroimper/AtomcraftMods).

The community shares mods in
[#mods](https://discord.com/channels/264359192167448576/1467253955758260294) on
the Atomcraft Official discord.

Another contributor to this project publishes mods
[on Github](https://github.com/sparr?tab=repositories&q=atomcraft-mod).

# Modders

To make a mod that loads with this Mod Loader:

- It has to be packed as a zip and files have to be placed inside a folder named
  with the ModId.
- The zip must contain one file named mod.json with the following format:

```json
{
  "id": "<ModId>",
  "name": "<Mod Name>",
  "description": "<Description>",
  "author": "<author>",
  "version": "<version>",
  "modules": [
    {
      "moduleId": "<ModId/ModuleId>",
      "dll": "<Path/To/Dll.dll>",
      "initClass": "<Namespace.ClassName>",
      "materials": "<File or Folder to load Materials, same format as game JSON files>",
      "reactions": "<File or Folder to load Reactions, same format as game JSON files>",
      "translations": "<File or Folder to load translations, see JSON format below>",
      "loadAsResourcePack": "<true or false, needed to be able to access resources from the zip with 'res://'"
      "optional": "<true or false, with true this module will only be loaded if it is a dependency (or optionalDependency) of another module>"
      "dependencies": [
        "<moduleId that is required to be loaded before this one>",
        ...
      ],
      "optionalDependencies": [
        "<moduleId that is NOT required to be loaded, but if it exists, load it before this one>",
        ...
      ]
    },
    ...
  ]
}
```

- For modules, only moduleId is mandatory. The other fields can be used only
  when needed.
- If initClass is defined, once the library is loaded, a **public static**
  method named `Initialize` will be called.
  Additionally, **public static** methods `OnWorldLoad` and `OnWorldSave` will
  also be called before loading and saving a world. A Serializable object can be
  received/returned on these methods to save data into the world file (it will
  be stored in a file <saveDir>/modded/world.json).
- The Harmony library is already loaded by default (version 2.4.2), don't
  include the dll on your mod.
- The JSON file for translations has the following format:

```json
{
  "<language code, as in the game files>": {
    "<key>": "<string>",
    ...
  },
  ...
}
```
---

# Contact

For any issue or comment about the mod loader, you can find me in the official
Atomcraft Discord as @sacroimper.
