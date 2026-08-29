namespace GoZCCondorLauncher;

public static class ConfigurationMigration
{
    public static void Migrate(AppSettings appSettings, UserSettings userSettings)
    {
        if (appSettings.Groups.Count == 0)
            appSettings.Groups = CreateLegacyGroups(appSettings.Scenarios);

        foreach (var scenario in appSettings.Scenarios.Where(item => string.IsNullOrWhiteSpace(item.GroupId)))
        {
            scenario.GroupId = LegacyGroupId(scenario.Category, scenario.Aircraft);
            if (appSettings.Groups.All(group => group.GroupId != scenario.GroupId))
            {
                appSettings.Groups.Add(new ScenarioGroup
                {
                    GroupId = scenario.GroupId,
                    DisplayName = JoinDisplayName(scenario.Category, scenario.Aircraft),
                    SortOrder = appSettings.Groups.Count + 1
                });
            }
        }

        userSettings.ScenarioNames ??= [];
        userSettings.GroupPreferences ??= [];
        foreach (var group in appSettings.Groups)
        {
            if (userSettings.GroupPreferences.All(item => item.GroupId != group.GroupId))
                userSettings.GroupPreferences.Add(new GroupPreference
                {
                    GroupId = group.GroupId,
                    DisplayName = group.DisplayName,
                    SortOrder = group.SortOrder
                });
        }
    }

    public static IReadOnlyList<GroupPreference> RestoreDefaultGroups(AppSettings appSettings) =>
        appSettings.Groups.Select(group => new GroupPreference
        {
            GroupId = group.GroupId,
            DisplayName = group.DisplayName,
            SortOrder = group.SortOrder
        }).ToArray();

    public static IReadOnlyList<GroupPreference> SortedGroups(UserSettings settings) =>
        settings.GroupPreferences.OrderBy(group => group.SortOrder).ThenBy(group => group.DisplayName).ToArray();

    public static int GridColumns(int groupCount) => Math.Clamp(groupCount, 1, 3);

    private static List<ScenarioGroup> CreateLegacyGroups(IEnumerable<Scenario> scenarios) => scenarios
        .GroupBy(item => new { item.Category, item.Aircraft })
        .Select((group, index) => new ScenarioGroup
        {
            GroupId = LegacyGroupId(group.Key.Category, group.Key.Aircraft),
            DisplayName = JoinDisplayName(group.Key.Category, group.Key.Aircraft),
            SortOrder = index + 1
        }).ToList();

    private static string LegacyGroupId(string category, string aircraft)
    {
        var value = $"{category}-{aircraft}".ToLowerInvariant();
        var chars = value.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray();
        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string JoinDisplayName(string category, string aircraft) =>
        string.IsNullOrWhiteSpace(aircraft) ? category : $"{category} – {aircraft}";
}
