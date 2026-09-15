extends CanvasLayer



enum ModuleState { OPTIONAL, DEFAULT, READY, LOADED, ERROR }

var mods: Dictionary
var history: Array[String]
var mod_loader_initialized: bool

# Set by the loader script before this node enters the tree. Each entry is one plain
# text instruction for a way this build can be told to load mods; the list is empty
# when nothing has contributed one. See loading_methods in GodotMonoModLoader.gd.
var loading_methods: Array[String] = []

@onready var summary := %Summary
@onready var tabs := %Tabs
@onready var errors := %ErrorsText
@onready var logLabel := %LogText
@onready var continue_button := %ContinueButton
@onready var mod_list := %ModList
@onready var mod_details := %ModDetails

func initialize(p_mods:Dictionary, p_history:Array[String], p_mod_loader_initialized: bool) -> void:
	mods = p_mods
	history = p_history
	mod_loader_initialized = p_mod_loader_initialized


func _ready():
	continue_button.pressed.connect(queue_free)

	
	var style = logLabel.get_theme_stylebox("normal").duplicate()

	style.content_margin_left = 20
	style.content_margin_top = 20
	style.content_margin_right = 20
	style.content_margin_bottom = 20

	tabs.set_tab_title(0, "            Errors             ")
	tabs.set_tab_title(1, "             Mods              ")
	tabs.set_tab_title(2, "           View Log            ")

	errors.add_theme_stylebox_override("normal", style)
	logLabel.add_theme_stylebox_override("normal", style)
	
	var styleEmpty = StyleBoxEmpty.new()
	summary.add_theme_stylebox_override("focus", styleEmpty)
	errors.add_theme_stylebox_override("focus", styleEmpty)
	mod_details.add_theme_stylebox_override("focus", styleEmpty)
	mod_list.add_theme_stylebox_override("focus", styleEmpty)
	logLabel.add_theme_stylebox_override("focus", styleEmpty)

	_populate()


func _populate():

	logLabel.clear()

	for line in history:
		logLabel.append_text(line + "\n")

	summary.clear()
	errors.clear()

	if !mod_loader_initialized:
		tabs.current_tab = 0
		tabs.set_tab_hidden(1, true)
		# Names no mechanism of its own: whatever loading_methods holds is listed, so
		# this stays shared by all of them. Only the headline is enlarged, because a
		# list at font_size 30 fills the pane and stops being readable as a list.
		errors.append_text("[color=red][font_size=30]The Mod Loader did not start.[/font_size][/color]\n")
		errors.append_text("\n[color=yellow]The game was launched without any of the ways to load mods.[/color]\n")
		if not loading_methods.is_empty():
			errors.append_text("\nUse whichever one you installed:\n\n")
			for method in loading_methods:
				errors.append_text("    " + method + "\n")
		errors.append_text("\nAlso check that every file from GodotMonoModLoader.zip was extracted next to the game executable.\n")
		errors.append_text("\n[b]ModLoader.md[/b], next to the game executable, covers what each way needs, including any launch options it requires.\n")
		return

	var mods_total := 0
	var mods_loaded := 0
	var mods_partial := 0
	var failed := 0
	var optional := 0
	var modules_total := 0
	var modules_loaded := 0

	for mod in mods.values():
		if mod.path == "bundled":
			continue
		mods_total += 1
		var module_loaded = false
		var module_error = false
		for module in mod.modules.values():
			modules_total += 1

			match module.state:
				ModuleState.LOADED:
					modules_loaded += 1
					module_loaded = true
				ModuleState.ERROR:
					failed += 1

					errors.append_text(
						"Mod: %s Module: %s\n" % [mod.name, module.moduleId]
					)
					errors.append_text(
						"[color=red]Error: %s[/color]\n\n" % module.error_message
					)
					module_error = false

				ModuleState.OPTIONAL:
					optional += 1
		if module_loaded:
			mods_loaded += 1
			if module_error:
				mods_partial += 1

	summary.append_text(
		"Mods Loaded: %d / %d\n" % [mods_loaded, mods_total]
	)

	if mods_partial != 0:
		summary.append_text(
			"Mods Partially Loaded: %d / %d\n" % [mods_partial, mods_total]
		)
	
	summary.append_text(
		"Modules Loaded: %d / %d\n" % [modules_loaded, modules_total]
	)

	if failed != 0:
		tabs.current_tab = 0
		summary.append_text(
			"Modules Failed: %d\n" % failed
		)

	if optional != 0:
		summary.append_text(
			"Optional Modules Skipped: %d\n" % optional
		)

	if failed == 0:
		errors.append_text("[color=lime]All mods loaded correctly.[/color]\n")
		


	mod_list.clear()

	for mod in mods.values():
		mod_list.add_item(mod.name)
		mod_list.set_item_metadata(mod_list.get_item_count() - 1, mod.id);

	mod_list.item_selected.connect(func(idx): _on_mod_selected(mod_list.get_item_metadata(idx)))

	if mods.size() > 0:
		mod_list.select(0)
		_on_mod_selected(mod_list.get_item_metadata(0))

func _on_mod_selected(mod_id: String) -> void:
	var mod = mods[mod_id]

	mod_details.clear()

	mod_details.append_text("[font_size=36][b]%s[/b][/font_size]\n\n" % mod.name)

	mod_details.append_text("[b]Author:[/b] %s\n" % mod.author)
	mod_details.append_text("[b]Version:[/b] %s\n" % mod.version)
	mod_details.append_text("[b]ID:[/b] %s\n\n" % mod.id)

	mod_details.append_text("[b]Description[/b]\n")
	mod_details.append_text("%s\n\n" % mod.description)

	if mod.modules:
		mod_details.append_text("[b]Modules[/b]\n")

		for module in mod.modules.values():
			var status := ""

			match module.state:
				ModuleState.LOADED:
					status = "[color=lime]✓ Loaded[/color]"

				ModuleState.ERROR:
					status = "[color=red]✗ Error[/color]"

				ModuleState.OPTIONAL:
					status = "[color=yellow]○ Optional[/color]"

				ModuleState.READY:
					status = "Ready"

				_:
					status = "Default"

			mod_details.append_text("• %s — %s\n" % [
				module.moduleId,
				status
			])

			if module.state == ModuleState.ERROR:
				mod_details.append_text("    [color=red]%s[/color]\n" % module.error_message)
		
	
	mod_details.append_text("\n[b]Location:[/b] %s\n" % mod.path)
