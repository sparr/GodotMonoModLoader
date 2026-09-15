using System.Text;

namespace ModLoaderHook;

/// <summary>
/// Writes to a file beside the game executable.
/// </summary>
/// <remarks>
/// The proxy does its work before Godot's managed bridge exists, so GD.Print and
/// godot.log are not available yet. Everything the proxy has to say about whether
/// the hook took therefore has to go somewhere of its own.
/// </remarks>
internal static class Log
{
    private static readonly object Gate = new();
    private static string? _path;
    private static bool _failed;

    internal static void Open(string gameDirectory)
    {
        _path = Path.Combine(gameDirectory, "GodotMonoModLoader.startup.log");
        try { File.WriteAllText(_path, "", Encoding.UTF8); }
        catch { _failed = true; }
    }

    internal static void Write(string message)
    {
        if (_failed || _path is null) return;
        lock (Gate)
        {
            try { File.AppendAllText(_path, $"[hook] {message}{Environment.NewLine}", Encoding.UTF8); }
            catch { _failed = true; }
        }
    }
}
