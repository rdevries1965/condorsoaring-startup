using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace GoZCCondorLauncher;

public partial class MainWindow : Window
{
    private readonly AppSettings _appSettings;
    private UserSettings _userSettings;
    private Scenario? _selected;

    public MainWindow(AppSettings appSettings, UserSettings userSettings)
    {
        InitializeComponent();
        _appSettings = appSettings;
        _userSettings = userSettings;
        BuildScenarioMenu();
        RefreshVersions();
    }

    private void BuildScenarioMenu()
    {
        CategoryList.Items.Clear();
        _selected = null;
        foreach (var groupPreference in ConfigurationMigration.SortedGroups(_userSettings))
        {
            var scenarios = _appSettings.Scenarios.Where(scenario => scenario.GroupId == groupPreference.GroupId).OrderBy(scenario => scenario.Number).ToList();
            if (scenarios.Count == 0) continue;
            var box = new GroupBox { Header = groupPreference.DisplayName };
            var panel = new StackPanel();
            foreach (var scenario in scenarios)
            {
                var name = _userSettings.ScenarioNames.TryGetValue(scenario.Number, out var customName)
                    && !string.IsNullOrWhiteSpace(customName) ? customName : scenario.Name;
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 34 };
                text.Inlines.Add(new Run($"{scenario.Number}  ") { FontWeight = FontWeights.Bold });
                text.Inlines.Add(new Run(name));
                var radio = new RadioButton { Content = text, Tag = scenario, GroupName = "Scenarios" };
                radio.Checked += (_, _) => _selected = (Scenario)radio.Tag;
                panel.Children.Add(radio);
                if (_selected is null) radio.IsChecked = true;
            }
            box.Content = panel;
            CategoryList.Items.Add(box);
        }
    }

    private async void Fly_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        FlyButton.IsEnabled = false;
        try
        {
            StatusText.Text = "Bestanden controleren…";
            var sourcePlan = Path.Combine(_userSettings.FlightPlansFolder, _selected.File);
            var sourceSetup = Path.Combine(_userSettings.PilotFolder,
                VrMode.IsChecked == true ? _appSettings.VrSetupFile : _appSettings.ScreenSetupFile);
            var targetPlan = Path.Combine(_userSettings.PilotFolder, "Flightplan.fpl");
            var targetSetup = Path.Combine(_userSettings.PilotFolder, "Setup.ini");

            RequireFile(_userSettings.CondorExe, "Condor.exe");
            RequireDirectory(_userSettings.PilotFolder, "pilotmap");
            RequireFile(sourcePlan, "geselecteerde flightplan");
            RequireFile(sourceSetup, VrMode.IsChecked == true ? "VR-configuratie" : "schermconfiguratie");

            Backup(targetPlan);
            Backup(targetSetup);
            File.Copy(sourcePlan, targetPlan, true);
            File.Copy(sourceSetup, targetSetup, true);
            Logger.Info($"Scenario {_selected.Number} geselecteerd; modus: {(VrMode.IsChecked == true ? "VR" : "Scherm")}.");

            StatusText.Text = "Condor wordt gestart…";
            await CondorAutomation.StartFlightAsync(_appSettings, _userSettings, CancellationToken.None);
            StatusText.Text = "Condor is gestart.";
            WindowState = WindowState.Minimized;
            await CondorAutomation.FinishFlightAndCloseCondorAsync(_appSettings, CancellationToken.None);
            WindowState = WindowState.Maximized;
            Activate();
            StatusText.Text = "Vlucht afgesloten. Kies een nieuwe opdracht.";
        }
        catch (Exception ex)
        {
            Logger.Error(ex.ToString());
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Maximized;
                Activate();
            }
            StatusText.Text = "Starten mislukt.";
            MessageBox.Show($"De vlucht kon niet worden gestart.\n\n{ex.Message}",
                "GoZC Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { FlyButton.IsEnabled = true; }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_userSettings.AdministratorPassword is null)
        {
            var createPassword = new SetPasswordWindow { Owner = this };
            if (createPassword.ShowDialog() != true || createPassword.Result is null) return;
            _userSettings.AdministratorPassword = createPassword.Result;
            ConfigurationService.SaveUserSettings(_userSettings);
        }
        else
        {
            var password = new PasswordPromptWindow(_userSettings.AdministratorPassword) { Owner = this };
            if (password.ShowDialog() != true) return;
        }

        var wizard = new SetupWizardWindow(_appSettings, _userSettings, false) { Owner = this };
        if (wizard.ShowDialog() == true)
        {
            _userSettings = wizard.Result;
            BuildScenarioMenu();
            RefreshVersions();
            StatusText.Text = "Instellingen opgeslagen.";
        }
    }

    private void RefreshVersions()
    {
        CondorVersionText.Text = $"Condor Soaring Simulator 3 – versie {VersionService.CondorVersion(_userSettings.CondorExe)}";
        LauncherVersionText.Text = $"GoZC Launcher versie {VersionService.LauncherVersion()} · Gliding Services Zulu Echo";
    }

    private static void RequireFile(string path, string description)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"De {description} ontbreekt:\n{path}", path);
    }

    private static void RequireDirectory(string path, string description)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"De {description} ontbreekt:\n{path}");
    }

    private static void Backup(string path)
    {
        if (File.Exists(path)) File.Copy(path, path + ".backup", true);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
