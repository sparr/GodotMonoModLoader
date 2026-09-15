namespace ModLoaderLauncher;

internal static class Log
{
    internal static bool Verbosely;

    internal static void Info(string message) => Console.WriteLine($"[launcher] {message}");

    internal static void Verbose(string message)
    {
        if (Verbosely) Console.WriteLine($"[launcher] {message}");
    }

    internal static void Error(string message) => Console.Error.WriteLine($"[launcher] error: {message}");
}
