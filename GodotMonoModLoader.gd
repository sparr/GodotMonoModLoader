extends SceneTree

var ref
var mod_loader
var mods: Dictionary[String, ModInfo]
var log_history: Array[String] = []

var start_time

# Ways this build can be told to load mods, reported to the player when none of them
# ran. Empty here on purpose: this script is shared by every loading mechanism and
# names none of them.
#
# A branch that adds a mechanism appends one line, phrased as an instruction and in
# plain text with no BBCode, since these lines are both printed to godot.log and
# rendered in the report:
#
#     loading_methods.append("Start the game through SomeLauncher.exe instead of AtomCraft.exe")
var loading_methods: Array[String] = []

# Ways from that list which cannot work without a display, named when the loader fails
# in a headless run. A headless game creates no DisplayServer, so anything that waits to
# be called by the windowing or input code never starts at all, and the failure looks
# identical to not having set anything up. Registered the same way as loading_methods.
var headless_unsupported: Array[String] = []

func _initialize():

	start_time = Time.get_ticks_msec()
	log_separator()
	load_game()
	if load_mod_loader():
		load_mods()

	log_message("Loading report...")
	var mod_loader_report = load("./GodotMonoModLoader/ModLoaderReport.tscn").instantiate()
	mod_loader_report.loading_methods = loading_methods
	mod_loader_report.initialize(mods, log_history, mod_loader != null)
	self.root.add_child(mod_loader_report)

	log_message("Exiting mod loader.")
	log_separator()
	
	

func _finalize():
	unload_current_scene()

class ModInfo:
	static var json_fields = ["id", "name", "description", "author", "version", "modules"]

	var id: String
	var name: String
	var description: String
	var author: String
	var version: String
	var modules: Dictionary[String, ModuleInfo]
	var path: String

	func _to_string():
		return str("{ id: " + id + ", name: " + name + ", description: " + description + ", author: " + author + ", version: " + version + ", modules: " + str(modules) + ", path: " + path + " }")

enum ModuleState { OPTIONAL, DEFAULT, READY, LOADED, ERROR }

class ModuleInfo:
	static var json_fields = ["moduleId", "dll", "initClass", "materials", "reactions", "translations", "loadAsResourcePack", "optional", "dependencies", "optionalDependencies"]

	var modId: String
	var moduleId: String
	var dll: String
	var initClass: String
	var materials: String
	var reactions: String
	var translations: String
	var loadAsResourcePack: bool = false
	var optional: bool = false
	var dependencies: Array[String]
	var optionalDependencies: Array[String]
	var state = ModuleState.DEFAULT
	var error_message: String

	func _to_string():
		return str("{ moduleId: " + moduleId + ", dll: " + dll + ", initClass: " + initClass + ", materials: " + materials + ", reactions: " + reactions + ", translations: " + translations + ", loadAsResourcePack: " + str(loadAsResourcePack) + ", optional: " + str(optional) + ", dependencies: " + str(dependencies) + ", optionalDependencies: " + str(optionalDependencies) + ", state: " + str(ModuleState.keys()[state]) + " }")

func load_game() -> void:
	var main_scene := ProjectSettings.get_setting("application/run/main_scene") as String

	log_message(str("Loading Game main scene: ", main_scene))
	change_scene_to_file(main_scene)

	await process_frame
	await process_frame
	
	log_message("Game loaded.")
	log_separator()

# 0Harmony.dll and GodotMonoModLoader.dll are already loaded by the time this runs.
# Something got ModLoaderBootstrap running before the main scene existed, and it loaded
# them into the game's own load context and registered their scripts. So all that is
# left here is to pick up what they registered.
#
# A null script means that did not happen, which is a question of how the game was
# started rather than anything this script can fix.
#
# The report names no mechanism of its own, listing whatever loading_methods holds, so
# that this script stays shared by all of them. It points at ModLoader.md rather than
# the README because that is the name the release zip installs it under, next to the
# game executable.
# Both failure modes mean the same thing, so they report the same way. An unregistered
# res:// script path does not give back null here: load() returns an unusable
# CSharpScript and it is new() that fails, so checking only for null would miss the
# common case and report nothing useful.
func load_mod_loader():

	log_message("Initializing mod loader...")

	var ModLoader = load("res://GodotMonoModLoader/GodotMonoModLoader.cs")
	if ModLoader != null:
		mod_loader = ModLoader.new()

	if mod_loader == null:
		log_message("Mod loader did not load: the game was started without any of the ways to load it.")
		if not loading_methods.is_empty():
			log_message("Use whichever one you installed:")
			for method in loading_methods:
				log_message("  - " + method)
		if not headless_unsupported.is_empty() and DisplayServer.get_name() == "headless":
			log_message("This is a headless run, so these cannot work here: " + ", ".join(headless_unsupported))

		log_message("ModLoader.md, next to the game executable, covers what each way needs, including any launch options it requires.")
		log_message("GodotMonoModLoader.startup.log, also next to the game executable, records how far it got. It is missing entirely if nothing ran at all.")
		return false

	log_message("Mod loader initialized.")
	log_separator()
	return true

func load_mods() -> void:


	mods = lookup_mods()
	var loadable_modules = get_loadable_modules(true)
	log_message(str("Modules loaded: ", loadable_modules.size()))

func get_loadable_modules(load_now: bool) -> Array[ModuleInfo]:
	var modules: Dictionary[String, ModuleInfo] = {}
	var loadable_modules: Array[ModuleInfo] = []
	var modules_to_load: Array[String] = []

	var modsId: Array[String] = mods.keys();
	modsId.sort();

	for modId in modsId:
		var mod = mods[modId]
		for moduleId in mod.modules:
			var module: ModuleInfo = mod.modules[moduleId]
			modules[moduleId] = module
			if module.state < ModuleState.READY: 
				modules_to_load.append(moduleId)
				if module.optional:
					module.state = ModuleState.OPTIONAL

	#modules_to_load.sort()
	log_message(str("Modules to load: ", modules_to_load))
	modules_to_load.reverse()

	var doneSomething = true
	var lastLoop = false

	while not modules_to_load.is_empty() and (doneSomething or lastLoop):
		doneSomething = false
		var i = modules_to_load.size()

		while i > 0:
			i -= 1
			var moduleId = modules_to_load[i]
			var module: ModuleInfo = modules[moduleId]

			if module.dependencies and module.dependencies.any(func (dependencyId): return modules[dependencyId] == null or modules[dependencyId].state == ModuleState.ERROR):
				module.state = ModuleState.ERROR
				log_message(str("Missing dependency for ", moduleId))
				doneSomething = true
				modules_to_load.remove_at(i)
				log_separator()
				continue
			elif module.dependencies and module.dependencies.any(func (dependencyId): return modules[dependencyId].state != ModuleState.READY and modules[dependencyId].state != ModuleState.LOADED):
				if module.dependencies.all(func (dependencyId): return modules[dependencyId].state == ModuleState.OPTIONAL or modules[dependencyId].state == ModuleState.READY or modules[dependencyId].state == ModuleState.LOADED):
					for dependencyId in module.dependencies:
						if modules[dependencyId].state == ModuleState.OPTIONAL:
							modules[dependencyId].state = ModuleState.DEFAULT
					doneSomething = true
				continue
			elif not lastLoop and module.optionalDependencies and module.optionalDependencies.any(func (dependencyId): return modules[dependencyId] != null and modules[dependencyId].state < ModuleState.READY):
				if module.optionalDependencies.all(func (dependencyId): return modules[dependencyId].state == ModuleState.OPTIONAL or modules[dependencyId].state >= ModuleState.READY):
					for dependencyId in module.optionalDependencies:
						if modules[dependencyId].state == ModuleState.OPTIONAL:
							modules[dependencyId].state = ModuleState.DEFAULT
					doneSomething = true
				continue
			elif module.state == ModuleState.OPTIONAL:
				continue
			else:
				if load_now:
					if load_module(mods[module.modId], module):
						module.state = ModuleState.LOADED
						loadable_modules.append(module)
					else:
						module.state = ModuleState.ERROR
				else:
					module.state = ModuleState.READY
					loadable_modules.append(module)
				doneSomething = true
				modules_to_load.remove_at(i)
				log_separator()


		# When no more modules can be loaded due to dependency restrictions, it does an extra iteration to load all modules that were waiting for optionalDependencies
		lastLoop = not lastLoop and not doneSomething

	if !modules_to_load.is_empty():
		log_message("This modules cannot be loaded because there are cyclic dependencies:")
		for moduleId in modules_to_load:
			if modules[moduleId].state != ModuleState.OPTIONAL:
				log_message(str("Module: ", moduleId, ", Dependencies: ", modules[moduleId].dependencies))


	return loadable_modules


func load_module(mod: ModInfo, module: ModuleInfo) -> bool:
	log_message(str("Loading Mod: ", mod.id, ", Module: ", module.moduleId))
	log_message(str("Zip: ", mod.path))


	if module.loadAsResourcePack:
		ProjectSettings.load_resource_pack(mod.path)
		
	if module.dll:
		if !load_dll(mod, module):
			return false

	if module.materials:
		if !load_materials(mod, module):
			return false
	
	if module.reactions:
		if !load_reactions(mod, module):
			return false
	
	if module.translations:
		if !load_translations(mod, module):
			return false

	log_message(str("Module loaded correctly: ", module.moduleId))
	return true

func load_dll(mod: ModInfo, module: ModuleInfo) -> bool:

	log_message(str("Loading Dll: ", module.dll))
	if module.initClass:
		log_message(str("InitClass: ", module.initClass))

	var error = mod_loader.LoadDllFromZip(mod.id, module.moduleId, mod.path, mod.id.path_join(module.dll), module.initClass)
	if error == 1:
		module.error_message = str("Error while trying to load the dll: ", module.dll)
		log_message(str("Error while trying to load dll from module : ", module.moduleId))
	elif error == 2:
		module.error_message = str("Error while trying to initialize the dll: ", module.dll, " , initClass: ", module.initClass)
		log_message(str("Error while trying to initialize dll from module: ", module.moduleId))
	return error == 0

func load_materials(mod: ModInfo, module: ModuleInfo) -> bool:
	log_message(str("Loading materials: ", module.materials))
	var error = mod_loader.LoadMaterials(mod.path, mod.id.path_join(module.materials))
	if error == 1:
		module.error_message = str("Error while trying to load materials from: ", module.materials)
		log_message(str("Error while trying to load materials from module: ", module.moduleId))
	return error == 0

func load_reactions(mod: ModInfo, module: ModuleInfo) -> bool:
	log_message(str("Loading reactions: ", module.reactions))
	var error = mod_loader.LoadReactions(mod.path, mod.id.path_join(module.reactions))
	if error == 1:
		module.error_message = str("Error while trying to load reactions from: ", module.reactions)
		log_message(str("Error while trying to load reactions from module: ", module.moduleId))
	return error == 0

func load_translations(mod: ModInfo, module: ModuleInfo) -> bool:
	log_message(str("Loading translations: ", module.translations))
	var error = mod_loader.LoadTranslations(mod.path, mod.id.path_join(module.translations))
	if error == 1:
		module.error_message = str("Error while trying to load translations from: ", module.translations)
		log_message(str("Error while trying to load translations from module: ", module.moduleId))
	return error == 0

func lookup_mods() -> Dictionary[String, ModInfo]:
	var mod_list: Dictionary[String, ModInfo] = {}

	log_message("Looking up for mods...")
	
	register_bundled_mods(mod_list)
	read_mods(mod_list, lookup_zips(OS.get_executable_path().get_base_dir().path_join("Mods")))
	read_mods(mod_list, lookup_zips(OS.get_user_data_dir().path_join("Mods")))
	# TODO read mods from steam when it gets implemented

	return mod_list

func register_bundled_mods(mod_list: Dictionary[String, ModInfo]):



	var mod: ModInfo = ModInfo.new()

	mod.id = "GodotMonoModLoader"
	mod.name = "Mod Loader"
	mod.description = "This mod loader is used to load all the mods."
	mod.author = "sacroimper"
	mod.version = "@VERSION@"
	mod.modules = {}
	mod.path = "bundled"

	mod_list.set(mod.id, mod)


	var modules: Dictionary[String, ModuleInfo] = {}
	
	var module: ModuleInfo = ModuleInfo.new()
	module.modId = "0Harmony"
	module.moduleId = "0Harmony/Library"
	module.state = ModuleState.LOADED

	modules.set(module.moduleId, module)


	mod = ModInfo.new()

	mod.id = "0Harmony"
	mod.name = "Harmony"
	mod.description = "Harmony library, used by other mods."
	mod.author = "Andreas Pardeike (main author of the library)"
	mod.version = "@HARMONY_VERSION@"
	mod.modules = modules
	mod.path = "bundled"
	
	mod_list.set(mod.id, mod)

func read_mods(mod_list : Dictionary[String, ModInfo], mod_paths: Array[String]):

	for mod_path in mod_paths:
		var mod_info = read_mod(mod_path)
		if mod_info == null or not mod_info.id:
			log_message(str("Could not load Mod: ", mod_path))
		elif mod_list.get(mod_info.id):
			log_message(str("Duplicated Mod ", mod_info.id, ": ", mod_path))
			log_message(str("Keeping: ", mod_list[mod_info.id].path))
		else:
			mod_list[mod_info.id] = mod_info


func read_mod(mod_path : String) -> ModInfo:


	var json_as_text = read_mod_json(mod_path)
	if json_as_text == "":
		return

	var json = JSON.new()
	var error = json.parse(json_as_text)
	if error == OK:
		var json_as_dict = json.data
		if typeof(json_as_dict) == TYPE_DICTIONARY :

			var mod_info = parse_mod_info(json_as_dict)

			mod_info.path = mod_path
			return mod_info
	return

func read_mod_json(mod_path : String) -> String:
	var reader = ZIPReader.new()
	var err = reader.open(mod_path)
	if err != OK:
		log_message(str("Error reading mod.json: ", mod_path))
		log_message(err)
		return ""

	for file in reader.get_files():
		if file.get_file() == "mod.json":
			var json_as_text = reader.read_file(file)
			reader.close()
			return json_as_text.get_string_from_utf8()

	reader.close()
	return ""

func parse_mod_info(json_as_dict: Dictionary) -> ModInfo:
	var mod_info = ModInfo.new()

	var properties : Array = mod_info.get_property_list()

	for property in properties:
		var key = property.name
		if ModInfo.json_fields.has(key):
			var value = json_as_dict[key]
			# Backwards compatibility, dllModules is deprecated
			if key == "modules" and value == null:
				value = json_as_dict["dllModules"]

			if value != null:
				if property.type == TYPE_STRING and typeof(value) == TYPE_STRING:
					mod_info.set(key, value)
				elif key == "modules" and typeof(value) == TYPE_ARRAY:
					var modules = parse_modules(mod_info, value)
					mod_info.modules = modules

	return mod_info

func parse_modules(mod_info: ModInfo, modules_as_json_array: Array) -> Dictionary[String, ModuleInfo]:
	var modules: Dictionary[String, ModuleInfo] = {}
	for module_as_json in modules_as_json_array:
		var module_info = ModuleInfo.new()

		var properties : Array = module_info.get_property_list()
		for property in properties:
			var key = property.name
			if ModuleInfo.json_fields.has(key):
				var value = module_as_json.get(key)

				if value != null:
					if property.type == TYPE_STRING and typeof(value) == TYPE_STRING:
						module_info.set(key, value)
					elif property.type == TYPE_BOOL and (typeof(value) == TYPE_STRING or typeof(value) == TYPE_BOOL):
						module_info.set(key, value or value == "true")
					elif property.type == TYPE_ARRAY and typeof(value) == TYPE_ARRAY:
						var array:Array[String]
						array.assign(value)
						module_info.set(key, array)

		if module_info.moduleId != null and not module_info.moduleId.is_empty():
			module_info.modId = mod_info.id
			modules[module_info.moduleId] = module_info

	return modules


func lookup_zips(path : String) -> Array[String]:
	var zip_paths: Array[String] = []

	if DirAccess.dir_exists_absolute(path):
		var dir = DirAccess.get_files_at(path)
		for file in dir:
			if file.get_extension() == "zip":
				zip_paths.append(path.path_join(file))
	return zip_paths

func log_separator():
	log_message("--------------------------------------------------------------------------------", true)

func log_message(message: String, use_time = false):
	if use_time:
		var elapsed = Time.get_ticks_msec() - start_time
		message += str(" Time: ", elapsed, " ms")
	log_history.append(message)
	print(str("[GodotMonoModLoader]: ", message))
