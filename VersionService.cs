using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace GoZCCondorLauncher;

public static class VersionService
{
    public static string CondorVersion(string? executable)
    {
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return "onbekend";
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executable);
            return Clean(info.ProductVersion) ?? Clean(info.FileVersion) ?? "onbekend";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return "onbekend"; }
    }

    public static string LauncherVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "onbekend" : $"{version.Major}.{version.Minor}";
    }

    private static string? Clean(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        var token = version.Trim().Split(' ', '+')[0].Trim();
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }
}
