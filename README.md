
This is a Mod Loader for the game Atomcraft.

# Quick Start

1. Extract everything from `GodotMonoModLoader.zip` into the game installation
   folder, next to `AtomCraft.exe`.
2. Install mod zips (don't extract them) into
   `%AppData%/Godot/app_userdata/Atomcraft/Mods`.
   Mods can also be installed in `Atomcraft/Mods/`.
3. In Steam, right click the game in the Library, visit Propertes > General,
   then enter `-s GodotMonoModLoader.gd` in the Launch Options field.
4. Arrange for something to start the mod loader inside the game, from
   [Launch Alternatives](#launch-alternatives) below.
5. Launch the game, see the mod loader window, enjoy!

# Launch Alternatives

Everything in this section is optional and meant for unusual use cases.

Each of these gets the mod loader running inside the game; they differ in what
they touch and what they need from you. You can choose a different one for each
game launch.

<!-- Each way to start the mod loader documents itself here, as a "## "
     subsection of this one, added by whichever branch introduces it. Keep to
     this shape so the sections read as alternatives rather than steps:

       ## <Name of the mechanism>
       One sentence on what it does.
       Pro:
       Con:
       Additional details

     The rest of this file is shared, so add nothing outside this section. -->

# Troubleshooting

With the mod loader running, the game shows a Mod Loader report on startup, and
the same messages go to `godot.log`. A `--headless` run has no window, so read
the log there instead. Which symptom you get says where the problem is.

**No report at all, and the game plays as normal.** The mod loader itself never
ran, so `-s GodotMonoModLoader.gd` is missing from the launch options and was
not provided by your chosen launch alternative. Try [Quick Start](#quick-start)
again.

**The report says the Mod Loader did not start, and there is no
`GodotMonoModLoader.startup.log`.** Nothing got the C# half of the mod loader
into the game. Re-check the requirements in
[Launch Alternatives](#launch-alternatives).

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
