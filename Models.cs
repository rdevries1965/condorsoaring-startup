namespace GoZCCondorLauncher;

public sealed class AppSettings
{
    public string VrSetupFile { get; set; } = "VR.ini";
    public string ScreenSetupFile { get; set; } = "Scherm.ini";
    public bool AutomateCondorMenus { get; set; } = true;
    public int WindowTimeoutSeconds { get; set; } = 60;
    public List<ScenarioGroup> Groups { get; set; } = [];
    public List<Scenario> Scenarios { get; set; } = [];
}

public sealed class UserSettings
{
    public bool FirstRunCompleted { get; set; }
    public string CondorMainFolder { get; set; } = "";
    public string CondorExe { get; set; } = "";
    public string CondorUserFolder { get; set; } = "";
    public string FlightPlansFolder { get; set; } = "";
    public string PilotFolder { get; set; } = "";
    public Dictionary<int, string> ScenarioNames { get; set; } = [];
    public List<GroupPreference> GroupPreferences { get; set; } = [];
    public PasswordSettings? AdministratorPassword { get; set; }
}

public sealed class PasswordSettings
{
    public string Algorithm { get; set; } = "PBKDF2-SHA256";
    public int Iterations { get; set; } = 210_000;
    public string Salt { get; set; } = "";
    public string Hash { get; set; } = "";
}

public sealed class ScenarioGroup
{
    public string GroupId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int SortOrder { get; set; }
}

public sealed class GroupPreference
{
    public string GroupId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int SortOrder { get; set; }
}

public sealed class Scenario
{
    public int Number { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Aircraft { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string File { get; set; } = "";
    public override string ToString() => $"{Number}  {Name}";
}
