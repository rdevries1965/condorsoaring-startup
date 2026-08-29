using System.IO;
using System.Text.Json;

namespace GoZCCondorLauncher;

public static class ConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string UserSettingsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GoZC Condor Launcher");

    public static string UserSettingsPath => Path.Combine(UserSettingsFolder, "user-settings.json");

    public static AppSettings LoadAppSettings(string? path = null)
    {
        path ??= Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"De standaardconfiguratie ontbreekt: {path}", path);

        try
        {
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"De standaardconfiguratie is leeg: {path}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"De standaardconfiguratie is ongeldig: {path}", ex);
        }
    }

    public static bool TryLoadUserSettings(out UserSettings settings, string? path = null)
    {
        settings = new UserSettings();
        path ??= UserSettingsPath;
        if (!File.Exists(path)) return false;

        try
        {
            settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), JsonOptions) ?? new UserSettings();
            settings.ScenarioNames ??= [];
            settings.GroupPreferences ??= [];
            return SettingsAreComplete(settings);
        }
        catch (JsonException ex)
        {
            Logger.Error($"Beschadigd instellingenbestand '{path}': {ex}");
            settings = new UserSettings();
            return false;
        }
        catch (IOException ex)
        {
            Logger.Error($"Instellingenbestand kon niet worden gelezen '{path}': {ex}");
            settings = new UserSettings();
            return false;
        }
    }

    public static bool SettingsAreComplete(UserSettings settings) =>
        settings.FirstRunCompleted &&
        IsValidCondorMain(settings.CondorMainFolder) &&
        IsValidCondorUser(settings.CondorUserFolder) &&
        Directory.Exists(settings.PilotFolder) &&
        PathsEqual(settings.CondorExe, Path.Combine(settings.CondorMainFolder, "Condor.exe")) &&
        PathsEqual(settings.FlightPlansFolder, Path.Combine(settings.CondorUserFolder, "Flightplans"));

    public static bool IsValidCondorMain(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder, "Condor.exe"));

    public static bool IsValidCondorUser(string? folder) =>
        !string.IsNullOrWhiteSpace(folder) &&
        (Directory.Exists(Path.Combine(folder, "Flightplans")) || Directory.Exists(Path.Combine(folder, "Pilots")));

    public static IReadOnlyList<string> FindPilotProfiles(string? condorUserFolder)
    {
        if (string.IsNullOrWhiteSpace(condorUserFolder)) return [];
        var pilots = Path.Combine(condorUserFolder, "Pilots");
        if (!Directory.Exists(pilots)) return [];
        try { return Directory.GetDirectories(pilots).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase).ToArray(); }
        catch (IOException) { return []; }
        catch (UnauthorizedAccessException) { return []; }
    }

    public static IReadOnlyList<string> FindCondorMainCandidates()
    {
        var candidates = new[]
        {
            @"C:\Condor3",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Condor3"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Condor3")
        };
        return candidates.Where(IsValidCondorMain).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static string? FindCondorUserCandidate()
    {
        var candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Condor3");
        return IsValidCondorUser(candidate) ? candidate : null;
    }

    public static UserSettings CreateUserSettings(
        string mainFolder, string userFolder, string pilotFolder,
        IReadOnlyDictionary<int, string>? scenarioNames = null) => new()
    {
        FirstRunCompleted = true,
        CondorMainFolder = Path.GetFullPath(mainFolder),
        CondorExe = Path.Combine(Path.GetFullPath(mainFolder), "Condor.exe"),
        CondorUserFolder = Path.GetFullPath(userFolder),
        FlightPlansFolder = Path.Combine(Path.GetFullPath(userFolder), "Flightplans"),
        PilotFolder = Path.GetFullPath(pilotFolder),
        ScenarioNames = scenarioNames?.ToDictionary(pair => pair.Key, pair => pair.Value) ?? []
    };

    public static void SaveUserSettings(UserSettings settings, string? path = null)
    {
        path ??= UserSettingsPath;
        var folder = Path.GetDirectoryName(path) ?? throw new InvalidDataException("De map voor gebruikersinstellingen kon niet worden bepaald.");
        Directory.CreateDirectory(folder);
        var temporaryPath = Path.Combine(folder, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static bool PathsEqual(string? left, string right)
    {
        if (string.IsNullOrWhiteSpace(left)) return false;
        try { return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
