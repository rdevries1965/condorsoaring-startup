using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace GoZCCondorLauncher;

public partial class SetupWizardWindow : Window
{
    private sealed record PilotChoice(string Name, string Path);
    private sealed record GroupEditor(string GroupId, TextBox DisplayName, TextBox SortOrder);

    private readonly AppSettings _appSettings;
    private readonly bool _firstRun;
    private readonly List<GroupEditor> _groupEditors = [];
    private PasswordSettings? _pendingPassword;
    public UserSettings Result { get; private set; } = new();

    public SetupWizardWindow(AppSettings appSettings, UserSettings current, bool firstRun)
    {
        InitializeComponent();
        _appSettings = appSettings;
        _firstRun = firstRun;
        Result = current;
        MainFolderText.Text = current.CondorMainFolder;
        UserFolderText.Text = current.CondorUserFolder;
        IntroText.Text = firstRun
            ? "Welkom. Controleer de gevonden mappen of kies ze handmatig. Annuleren sluit de launcher zonder Condor te starten."
            : "Controleer of wijzig de gebruikte Condor-mappen en het pilotprofiel.";

        if (string.IsNullOrWhiteSpace(MainFolderText.Text))
        {
            var candidates = ConfigurationService.FindCondorMainCandidates();
            if (candidates.Count == 1) MainFolderText.Text = candidates[0];
        }
        if (string.IsNullOrWhiteSpace(UserFolderText.Text))
            UserFolderText.Text = ConfigurationService.FindCondorUserCandidate() ?? "";

        RefreshPilots(current.PilotFolder);
        BuildScenarioNameEditors(current);
        BuildGroupEditors(current.GroupPreferences);
        RefreshVersionLabels();
        RefreshState();
    }

    private void BuildGroupEditors(IEnumerable<GroupPreference> groups)
    {
        _groupEditors.Clear(); GroupNameList.Items.Clear();
        foreach (var group in groups.OrderBy(item => item.SortOrder))
        {
            var row = new Grid { Margin = new Thickness(0, 4, 8, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            row.Children.Add(new TextBlock { Text = group.GroupId, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
            var name = new TextBox { Text = group.DisplayName, ToolTip = $"Voorbeeld: {group.DisplayName}" };
            var order = new TextBox { Text = group.SortOrder.ToString(), ToolTip = "Sorteervolgorde" };
            name.TextChanged += (_, _) => RefreshState(); order.TextChanged += (_, _) => RefreshState();
            Grid.SetColumn(name, 1); Grid.SetColumn(order, 2); row.Children.Add(name); row.Children.Add(order);
            _groupEditors.Add(new GroupEditor(group.GroupId, name, order)); GroupNameList.Items.Add(row);
        }
    }

    private void BuildScenarioNameEditors(UserSettings current)
    {
        foreach (var scenario in _appSettings.Scenarios.OrderBy(item => item.Number))
        {
            var row = new Grid { Margin = new Thickness(0, 2, 8, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new TextBlock
            {
                Text = $"{scenario.Number}. {scenario.Aircraft}",
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            });
            var editor = new TextBox
            {
                Text = current.ScenarioNames.TryGetValue(scenario.Number, out var customName) ? customName : scenario.Name,
                Tag = scenario.Number,
                ToolTip = $"Standaard: {scenario.Name}"
            };
            editor.TextChanged += (_, _) => RefreshState();
            Grid.SetColumn(editor, 1);
            row.Children.Add(editor);
            ScenarioNameList.Items.Add(row);
        }
    }

    private void BrowseMain_Click(object sender, RoutedEventArgs e)
    {
        var folder = PickFolder("Kies de Condor 3-programmamap", MainFolderText.Text);
        if (folder is null) return;
        if (!ConfigurationService.IsValidCondorMain(folder))
        {
            MessageBox.Show($"Deze map bevat geen Condor.exe:\n{Path.Combine(folder, "Condor.exe")}\n\nKies een andere map.",
                "Ongeldige Condor Main-map", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        MainFolderText.Text = folder;
        RefreshVersionLabels();
        RefreshState();
    }

    private void BrowseUser_Click(object sender, RoutedEventArgs e)
    {
        var folder = PickFolder("Kies de Condor 3-gebruikersmap", UserFolderText.Text);
        if (folder is null) return;
        if (!ConfigurationService.IsValidCondorUser(folder))
        {
            MessageBox.Show($"Deze map bevat geen herkenbare map 'Flightplans' of 'Pilots':\n{folder}\n\nKies een andere map.",
                "Ongeldige Condor User-map", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        UserFolderText.Text = folder;
        RefreshPilots(null);
        RefreshState();
    }

    private static string? PickFolder(string title, string initialDirectory)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (Directory.Exists(initialDirectory)) dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private void RefreshPilots(string? preferredPath)
    {
        var pilots = ConfigurationService.FindPilotProfiles(UserFolderText.Text)
            .Select(path => new PilotChoice(Path.GetFileName(path), path)).ToList();
        PilotCombo.ItemsSource = pilots;
        PilotCombo.SelectedItem = pilots.FirstOrDefault(p => string.Equals(p.Path, preferredPath, StringComparison.OrdinalIgnoreCase));
        if (PilotCombo.SelectedItem is null && pilots.Count == 1) PilotCombo.SelectedIndex = 0;
    }

    private void PilotCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshState();

    private void RefreshState()
    {
        var mainValid = ConfigurationService.IsValidCondorMain(MainFolderText.Text);
        var userValid = ConfigurationService.IsValidCondorUser(UserFolderText.Text);
        var pilot = PilotCombo.SelectedItem as PilotChoice;
        var pilotValid = pilot is not null && Directory.Exists(pilot.Path);
        var scenarioNamesValid = ScenarioEditors().All(editor => !string.IsNullOrWhiteSpace(editor.Text));
        var flightplansValid = Directory.Exists(Path.Combine(UserFolderText.Text, "Flightplans"));
        var groupIdsValid = _groupEditors.Count > 0 && _groupEditors.Select(item => item.GroupId).All(id => !string.IsNullOrWhiteSpace(id))
            && _groupEditors.Select(item => item.GroupId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == _groupEditors.Count;
        var groupNamesValid = _groupEditors.All(item => !string.IsNullOrWhiteSpace(item.DisplayName.Text));
        var parsedOrders = _groupEditors.Select(item => int.TryParse(item.SortOrder.Text, out var value) && value > 0 ? value : -1).ToArray();
        var groupOrdersValid = parsedOrders.All(value => value > 0) && parsedOrders.Distinct().Count() == parsedOrders.Length;
        SaveButton.IsEnabled = mainValid && userValid && flightplansValid && pilotValid && scenarioNamesValid
            && groupIdsValid && groupNamesValid && groupOrdersValid;

        var flightplans = string.IsNullOrWhiteSpace(UserFolderText.Text) ? "—" : Path.Combine(UserFolderText.Text, "Flightplans");
        var pilots = string.IsNullOrWhiteSpace(UserFolderText.Text) ? "—" : Path.Combine(UserFolderText.Text, "Pilots");
        DerivedPathsText.Text = $"Flightplans: {flightplans}\nPilots: {pilots}";

        var messages = new List<string>();
        if (!mainValid) messages.Add("Kies een Condor Main-map die Condor.exe bevat.");
        if (!userValid) messages.Add("Kies een Condor User-map met Flightplans en/of Pilots.");
        if (!flightplansValid) messages.Add($"De Flightplans-map ontbreekt: {Path.Combine(UserFolderText.Text, "Flightplans")}");
        if (!pilotValid) messages.Add("Selecteer een bestaand pilotprofiel.");
        if (!scenarioNamesValid) messages.Add("Alle 15 scenarionamen moeten zijn ingevuld.");
        if (!groupIdsValid) messages.Add("De interne GroupId-waarden moeten niet-leeg en uniek zijn.");
        if (!groupNamesValid) messages.Add("Alle zichtbare groepsnamen moeten zijn ingevuld.");
        if (!groupOrdersValid) messages.Add("Iedere groepsvolgorde moet een uniek positief geheel getal zijn.");
        if (pilotValid)
        {
            var vr = Path.Combine(pilot!.Path, "VR.ini");
            var screen = Path.Combine(pilot.Path, "Scherm.ini");
            if (!File.Exists(vr)) messages.Add($"Waarschuwing: VR-vluchten zijn geblokkeerd zolang dit bestand ontbreekt: {vr}");
            if (!File.Exists(screen)) messages.Add($"Waarschuwing: schermvluchten zijn geblokkeerd zolang dit bestand ontbreekt: {screen}");
        }
        ValidationText.Text = messages.Count == 0 ? "Alle vereiste instellingen zijn geldig." : string.Join("\n", messages);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!SaveButton.IsEnabled || PilotCombo.SelectedItem is not PilotChoice pilot) return;
        var administratorPassword = _pendingPassword ?? Result.AdministratorPassword;
        var scenarioNames = ScenarioEditors().ToDictionary(
            editor => (int)editor.Tag, editor => editor.Text.Trim());
        Result = ConfigurationService.CreateUserSettings(
            MainFolderText.Text, UserFolderText.Text, pilot.Path, scenarioNames);
        Result.GroupPreferences = _groupEditors.Select(editor => new GroupPreference
        {
            GroupId = editor.GroupId,
            DisplayName = editor.DisplayName.Text.Trim(),
            SortOrder = int.Parse(editor.SortOrder.Text)
        }).ToList();
        Result.AdministratorPassword = administratorPassword;
        try
        {
            ConfigurationService.SaveUserSettings(Result);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex.ToString());
            MessageBox.Show($"De instellingen konden niet veilig worden opgeslagen.\n\n{ex.Message}",
                "Opslaan mislukt", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_firstRun) Logger.Info("Eerste-installatiewizard geannuleerd; Condor is niet gestart.");
        DialogResult = false;
    }

    private void OpenMain_Click(object sender, RoutedEventArgs e) => OpenFolder(MainFolderText.Text);
    private void OpenUser_Click(object sender, RoutedEventArgs e) => OpenFolder(UserFolderText.Text);
    private void OpenFlightplans_Click(object sender, RoutedEventArgs e) => OpenFolder(Path.Combine(UserFolderText.Text, "Flightplans"));
    private void OpenPilot_Click(object sender, RoutedEventArgs e) => OpenFolder((PilotCombo.SelectedItem as PilotChoice)?.Path);

    private void OpenFolders_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: not null } button)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void RestoreGroups_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Alle groepsnamen en hun volgorde herstellen naar de standaardwaarden?",
            "Groepsnamen herstellen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        BuildGroupEditors(ConfigurationMigration.RestoreDefaultGroups(_appSettings));
        RefreshState();
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        var current = _pendingPassword ?? Result.AdministratorPassword;
        if (current is null)
        {
            var create = new SetPasswordWindow { Owner = this };
            if (create.ShowDialog() == true) _pendingPassword = create.Result;
            return;
        }
        var change = new ChangePasswordWindow(current) { Owner = this };
        if (change.ShowDialog() == true) _pendingPassword = change.Result;
    }

    private void RefreshVersionLabels()
    {
        var executable = string.IsNullOrWhiteSpace(MainFolderText.Text) ? "" : Path.Combine(MainFolderText.Text, "Condor.exe");
        SettingsCondorVersionText.Text = $"Condor Soaring Simulator 3 – versie {VersionService.CondorVersion(executable)}";
        SettingsLauncherVersionText.Text = $"GoZC Launcher versie {VersionService.LauncherVersion()}";
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(Logger.LogPath)!;
        Directory.CreateDirectory(folder);
        if (File.Exists(Logger.LogPath))
        {
            Process.Start(new ProcessStartInfo(Logger.LogPath) { UseShellExecute = true });
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
        MessageBox.Show($"Er is nog geen logbestand aangemaakt. De logmap is geopend:\n{folder}",
            "Logbestand", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static void OpenFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            MessageBox.Show($"De map bestaat niet:\n{path}", "Map openen", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    private IEnumerable<TextBox> ScenarioEditors() => ScenarioNameList.Items
        .OfType<Grid>()
        .SelectMany(grid => grid.Children.OfType<TextBox>());
}
