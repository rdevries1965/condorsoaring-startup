using System.IO;

namespace GoZCCondorLauncher;

internal static class Logger
{
    private static readonly object Sync = new();
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GoZC Condor Launcher");
    public static string LogPath => Path.Combine(Folder, "launcher.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            lock (Sync)
                File.AppendAllText(LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}");
        }
        catch { /* logging mag het starten nooit blokkeren */ }
    }
}
