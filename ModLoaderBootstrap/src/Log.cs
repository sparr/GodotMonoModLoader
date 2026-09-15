using System.Text;

namespace ModLoaderBootstrap;

/// <summary>
/// Appends to the log the injected hook started.
/// </summary>
/// <remarks>
/// Godot is alive by the time the bootstrap runs, but GodotSharp is on the far side
/// of a load-context boundary here, so GD.Print is not reachable without reflection.
/// Sharing the hook's file keeps the whole startup path, native and managed, in one
/// place. Once GodotMonoModLoader.dll is loaded it logs to godot.log as before.
/// </remarks>
internal static class Log
{
    private const string FileName = "GodotMonoModLoader.startup.log";

    private static readonly object Gate = new();
    private static string? _path;
    private static bool _failed;

    /// <param name="loaderDirectory">
    /// The GodotMonoModLoader directory; the log lives beside the game executable,
    /// one level up.
    /// </param>
    internal static void Open(string loaderDirectory)
    {
        try { _path = Path.Combine(Path.GetDirectoryName(loaderDirectory) ?? loaderDirectory, FileName); }
        catch { _failed = true; }
    }

    internal static void Write(string message)
    {
        if (_failed || _path is null) return;
        lock (Gate)
        {
            try { File.AppendAllText(_path, $"[bootstrap] {message}{Environment.NewLine}", Encoding.UTF8); }
            catch { _failed = true; }
        }
    }
}
