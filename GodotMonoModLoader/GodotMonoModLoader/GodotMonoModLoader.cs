
using System.IO.Compression;
using System.Reflection;
using System.Runtime.Loader;
using Godot;
using Godot.Bridge;
using HarmonyLib;
using static System.String;
using MethodInfo = System.Reflection.MethodInfo;

namespace GodotMonoModLoader;

public partial class GodotMonoModLoader : Node
{
	private readonly AtomcraftModLoader _atomcraftModLoader = new AtomcraftModLoader();

	public static readonly Dictionary<string, Type> EntryClasses = [];

    /// <summary>
    /// Entry point called by ModLoaderBootstrap, from inside the game's load context.
    /// </summary>
    /// <remarks>
    /// Registering this assembly's own scripts is what lets GDScript reach C# with
    /// <c>load("res://GodotMonoModLoader/GodotMonoModLoader.cs")</c>. The res:// path
    /// is a key in Godot's script map rather than a real file; it comes from the
    /// [ScriptPath] attribute `Godot.SourceGenerators` emits.
    ///
    /// Harmony is patched here, before `Game._Ready` runs, so hooks on the game's
    /// startup path are in place in time to see it.
    /// </remarks>
    public static void Initialize()
    {
	    ScriptManagerBridge.LookupScriptsInAssembly(typeof(GodotMonoModLoader).Assembly);

	    var harmony = new Harmony("GodotMonoModLoader");
	    harmony.PatchAll();

	    GD.Print("[GodotMonoModLoader] Mod loader initialized before Game._Ready.");
    }

    public int LoadMaterials(string zipPath, string path)
    {
	    return _atomcraftModLoader.LoadMaterials(zipPath, path);
    }

    public int LoadReactions(string zipPath, string path)
    {
	    return _atomcraftModLoader.LoadReactions(zipPath, path);
    }

    public int LoadTranslations(string zipPath, string path)
    {
	    return _atomcraftModLoader.LoadTranslations(zipPath, path);
    }

    public int LoadDllFromZip(string modId, string moduleId, string zipPath, string dllPath, string? initClass)
	{
		Assembly? assembly;
		try
		{
			using ZipArchive archive = ZipFile.OpenRead(zipPath);

			ZipArchiveEntry? entry = archive.GetEntry(dllPath);

			if (entry == null) {
				throw new Exception("DLL not found: " + dllPath);
			}

			using Stream input = entry.Open();
			using MemoryStream memory = new MemoryStream();

			input.CopyTo(memory);

			memory.Position = 0;

			// Check for Harmony namespace before load
			// using AssemblyDefinition assemblyDef = Assembly.ReflectionOnlyLoad.ReadAssembly(memory) {
			//
			// 	bool hasHarmony = assemblyDef.Modules.Any(definition => definition.GetTypes().Select(t =>
			// 	{
			// 		string ns = t.Namespace ?? "";
			// 		int firstDot = ns.IndexOf('.');
			// 		return firstDot == -1 ? ns : ns.Substring(0, firstDot);
			// 	}).Any(ns => ns.Equals("0Harmony")));
			// 	
			// 	
			// 	memory.Position = 0;
			// }
			
			assembly = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly())?.LoadFromStream(memory);
			
			if (assembly == null)
			{
				throw new Exception("Could not load DLL: " + dllPath);
			}

			ScriptManagerBridge.LookupScriptsInAssembly(assembly);
			
			GD.Print($"[GodotMonoModLoader] DLL Loaded: {assembly.FullName}");

		}
		catch (Exception e)
		{
			GD.PrintErr("[GodotMonoModLoader] ", e);
			return 1;
		}

		
		try
		{
			if (!IsNullOrEmpty(initClass))
			{
				if (!InitializeMod(modId, moduleId, assembly, initClass))
				{
					return 2;
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr("[GodotMonoModLoader] ", e);
			return 1;
		}

		return 0;
	}

	public int LoadDllFromPath(string modId, string moduleId, string dllPath, string? initClass)
	{
		Assembly? assembly;
		try
		{
			assembly = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly())?.LoadFromAssemblyPath(dllPath);

			if (assembly == null)
			{
				throw new Exception("Could not load DLL: " + dllPath);
			}
			
			ScriptManagerBridge.LookupScriptsInAssembly(assembly);

			GD.Print($"[GodotMonoModLoader] DLL Loaded: {assembly.FullName}");

		}
		catch (Exception e)
		{
			GD.PrintErr("[GodotMonoModLoader] ", e);
			return 1;
		}

		
		try
		{
			if (!IsNullOrEmpty(initClass))
			{
				if (!InitializeMod(modId, moduleId, assembly, initClass))
				{
					return 2;
				}
			}
		}
		catch (Exception e)
		{
			GD.PrintErr("[GodotMonoModLoader] ", e);
			return 1;
		}

		return 0;
	}
	
	private bool InitializeMod(string modId, string moduleId, Assembly assembly, string initClass)
	{
		string? dllName = assembly.GetName().Name;

		Type? modEntryType = assembly.GetType(initClass);

		if (modEntryType == null)
		{
			throw new Exception($"Class {initClass} not found");
		}
		
		EntryClasses.Add(moduleId, modEntryType);

		MethodInfo? initializeMethod = modEntryType.GetMethod(
				"Initialize",
				BindingFlags.Public |
				BindingFlags.Static
			);

		if (initializeMethod == null)
		{
			throw new Exception($"Public Static method {initClass}.Initialize() not found");
		}

		initializeMethod.Invoke(null, null);

		GD.Print($"[GodotMonoModLoader] DLL {dllName} initialized!");

		return true;
	}
}