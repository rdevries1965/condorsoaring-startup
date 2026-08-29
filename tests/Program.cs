using GoZCCondorLauncher;

var root = Path.Combine(Path.GetTempPath(), $"gozc-tests-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
var passed = 0;

try
{
    Run("geldige Condor Main-map", () =>
    {
        var main = Path.Combine(root, "Condor3");
        Directory.CreateDirectory(main);
        File.WriteAllText(Path.Combine(main, "Condor.exe"), "test");
        Assert(ConfigurationService.IsValidCondorMain(main));
        Assert(!ConfigurationService.IsValidCondorMain(Path.Combine(root, "missing")));
    });

    Run("geldige Condor User-map", () =>
    {
        var user = Path.Combine(root, "User");
        Directory.CreateDirectory(Path.Combine(user, "Flightplans"));
        Assert(ConfigurationService.IsValidCondorUser(user));
    });

    Run("pilotprofielen vinden", () =>
    {
        var user = Path.Combine(root, "Profiles");
        Directory.CreateDirectory(Path.Combine(user, "Pilots", "Pilot B"));
        Directory.CreateDirectory(Path.Combine(user, "Pilots", "Pilot A"));
        var profiles = ConfigurationService.FindPilotProfiles(user);
        Equal(2, profiles.Count);
        Equal("Pilot A", Path.GetFileName(profiles[0]));
    });

    Run("geldige gebruikersinstellingen lezen en paden samenstellen", () =>
    {
        var main = Path.Combine(root, "MainValid");
        var user = Path.Combine(root, "UserValid");
        var pilot = Path.Combine(user, "Pilots", "ClubPilot");
        Directory.CreateDirectory(main);
        Directory.CreateDirectory(Path.Combine(user, "Flightplans"));
        Directory.CreateDirectory(pilot);
        File.WriteAllText(Path.Combine(main, "Condor.exe"), "test");
        var settings = ConfigurationService.CreateUserSettings(main, user, pilot,
            new Dictionary<int, string> { [1] = "Aangepaste scenarionaam" });
        var json = Path.Combine(root, "user-settings.json");
        ConfigurationService.SaveUserSettings(settings, json);
        Assert(ConfigurationService.TryLoadUserSettings(out var loaded, json));
        Equal(Path.Combine(main, "Condor.exe"), loaded.CondorExe);
        Equal(Path.Combine(user, "Flightplans"), loaded.FlightPlansFolder);
        Equal(pilot, loaded.PilotFolder);
        Equal("Aangepaste scenarionaam", loaded.ScenarioNames[1]);
    });

    Run("beschadigde JSON afhandelen", () =>
    {
        var json = Path.Combine(root, "broken.json");
        File.WriteAllText(json, "{ geen geldige json");
        Assert(!ConfigurationService.TryLoadUserSettings(out _, json));
    });

    Run("wachtwoord hashen en correct valideren", () =>
    {
        var password = SecurityService.CreatePassword("clubbeheer");
        Equal("PBKDF2-SHA256", password.Algorithm);
        Assert(password.Iterations >= 100_000);
        Assert(password.Hash != "clubbeheer");
        Assert(SecurityService.VerifyPassword("clubbeheer", password));
        Assert(!SecurityService.VerifyPassword("verkeerd", password));
    });

    Run("ieder wachtwoord krijgt een unieke salt", () =>
    {
        var first = SecurityService.CreatePassword("hetzelfde");
        var second = SecurityService.CreatePassword("hetzelfde");
        Assert(first.Salt != second.Salt);
        Assert(first.Hash != second.Hash);
    });

    Run("bestaande configuratie migreren", () =>
    {
        var app = new AppSettings
        {
            Scenarios = [new Scenario { Number = 1, Name = "Test", Category = "Oud", Aircraft = "LS4", File = "Scenario1.fpl" }]
        };
        var user = new UserSettings { CondorMainFolder = "bestaand-pad", ScenarioNames = new() { [1] = "Bewaarde naam" } };
        ConfigurationMigration.Migrate(app, user);
        Equal(1, app.Groups.Count);
        Assert(!string.IsNullOrWhiteSpace(app.Scenarios[0].GroupId));
        Equal("bestaand-pad", user.CondorMainFolder);
        Equal("Bewaarde naam", user.ScenarioNames[1]);
        Equal(1, user.GroupPreferences.Count);
    });

    Run("groepsnamen bewaren, herstellen en sorteren", () =>
    {
        var app = new AppSettings
        {
            Groups =
            [
                new ScenarioGroup { GroupId = "a", DisplayName = "Groep A", SortOrder = 1 },
                new ScenarioGroup { GroupId = "b", DisplayName = "Groep B", SortOrder = 2 }
            ]
        };
        var user = new UserSettings
        {
            GroupPreferences =
            [
                new GroupPreference { GroupId = "a", DisplayName = "Gewijzigd", SortOrder = 2 },
                new GroupPreference { GroupId = "b", DisplayName = "Eerst", SortOrder = 1 }
            ]
        };
        Equal("Eerst", ConfigurationMigration.SortedGroups(user)[0].DisplayName);
        var restored = ConfigurationMigration.RestoreDefaultGroups(app);
        Equal("Groep A", restored[0].DisplayName);
        Equal(1, restored[0].SortOrder);
    });

    Run("maximaal drie groepen per rasterrij", () =>
    {
        Equal(1, ConfigurationMigration.GridColumns(1));
        Equal(3, ConfigurationMigration.GridColumns(5));
        Equal(3, ConfigurationMigration.GridColumns(13));
    });

    Run("Condor-versie beschikbaar en niet beschikbaar", () =>
    {
        Equal("onbekend", VersionService.CondorVersion(Path.Combine(root, "bestaat-niet.exe")));
        var currentExecutable = Environment.ProcessPath ?? throw new InvalidOperationException("Test-executable ontbreekt.");
        Assert(VersionService.CondorVersion(currentExecutable) != "onbekend");
    });

    Console.WriteLine($"Alle {passed} tests geslaagd.");
    return 0;
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

void Run(string name, Action test)
{
    test();
    passed++;
    Console.WriteLine($"GESLAAGD: {name}");
}

static void Assert(bool condition)
{
    if (!condition) throw new InvalidOperationException("Verwachting is niet waar.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Verwacht '{expected}', ontvangen '{actual}'.");
}
