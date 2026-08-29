using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Diagnostics;

namespace GoZCCondorLauncher;

public partial class MainWindow : Window
{
    private readonly AppSettings _appSettings;
    private UserSettings _userSettings;
    private Scenario? _selected;
    private readonly SemaphoreSlim _flightLock = new(1, 1);
    private readonly FlightStateMachine _flightState = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _activeCondor;
    private bool _isClosing;

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
        if (_selected is null || !await _flightLock.WaitAsync(0)) return;
        if (!_flightState.TryBegin()) { _flightLock.Release(); return; }
        FlyButton.IsEnabled = false;
        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var sessionOwnsCondor = false;
        try
        {
            Transition(FlightSessionState.Preparing, sessionId, "Voorbereiding gestart.");
            if (!await EnsureNoExistingCondorAsync())
            {
                Logger.SessionInfo(sessionId, "Start geannuleerd omdat Condor al draait.");
                return;
            }

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

            Logger.SessionInfo(sessionId, $"Scenario {_selected.Number}; modus {(VrMode.IsChecked == true ? "VR" : "Scherm")}.");
            Logger.SessionInfo(sessionId, $"Flightplan: '{sourcePlan}' -> '{targetPlan}'.");
            Logger.SessionInfo(sessionId, $"Setup: '{sourceSetup}' -> '{targetSetup}'.");

            Backup(targetPlan);
            Backup(targetSetup);
            File.Copy(sourcePlan, targetPlan, true);
            File.Copy(sourceSetup, targetSetup, true);
            Transition(FlightSessionState.StartingCondor, sessionId, "Configuratie gereed; Condor starten.");
            StatusText.Text = "Condor wordt gestart…";
            _activeCondor = CondorAutomation.StartCondor(_userSettings, sessionId);
            sessionOwnsCondor = true;
            WindowState = WindowState.Minimized;

            try
            {
                Transition(FlightSessionState.OpeningFlightPlanner, sessionId, "Automatische Condor-menubediening gestart.");
                await CondorAutomation.AutomateStartAsync(_appSettings, sessionId, _lifetime.Token);
                Transition(FlightSessionState.Flying, sessionId, "Vluchtopdracht gestart.");
            }
            catch (Exception automationError) when (automationError is not OperationCanceledException)
            {
                Logger.SessionError(sessionId, $"Automatische menubediening niet voltooid: {automationError}");
                CondorAutomation.BringRunningCondorToFront();
                MessageBox.Show("Condor is gestart, maar de automatische menubediening is niet voltooid.\n\n" +
                    "Ga handmatig verder in Condor. De launcher blijft de Condor-sessie bewaken.",
                    "GoZC Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
                Transition(FlightSessionState.Flying, sessionId, "Handmatige voortzetting; procesbewaking blijft actief.");
            }

            var endReason = await CondorAutomation.WaitForEndAsync(sessionId, _lifetime.Token);
            if (endReason == CondorEndReason.Debriefing)
            {
                Transition(FlightSessionState.ClosingCondor, sessionId, "DEBRIEFING verwerken en Condor sluiten.");
                await CondorAutomation.CloseAfterDebriefingAsync(_appSettings, sessionId, _lifetime.Token);
            }

            Transition(FlightSessionState.WaitingForExit, sessionId, "Wachten tot alle Condor-processen beëindigd zijn.");
            if (!await CondorProcessService.WaitForAllExitedAsync(TimeSpan.FromSeconds(30), _lifetime.Token))
                await WaitForManualCondorExitAsync(sessionId);

            if (!CondorProcessService.AnyRunning())
                Logger.SessionInfo(sessionId, $"Condor volledig beëindigd om {DateTime.Now:HH:mm:ss}.");
        }
        catch (OperationCanceledException) { Logger.SessionInfo(sessionId, "Sessiecontrole geannuleerd omdat de launcher sluit."); }
        catch (Exception ex)
        {
            var failedStep = _flightState.State;
            Transition(FlightSessionState.Error, sessionId, $"Fout: {ex}");
            RestoreLauncher();
            StatusText.Text = "Fout tijdens de Condor-sessie.";
            MessageBox.Show($"Er ontstond een fout tijdens processtap '{failedStep}'.\n\n{ex.Message}",
                "GoZC Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _activeCondor?.Dispose(); _activeCondor = null;
            if (!CondorProcessService.AnyRunning())
            {
                RestoreLauncher(); ResetSelections();
                _flightState.Reset(); StatusText.Text = "Gereed"; FlyButton.IsEnabled = true;
                Logger.SessionInfo(sessionId, "Teruggekeerd naar GoZC-menu; status Ready.");
            }
            else
            {
                RestoreLauncher(); _flightState.Reset();
                FlyButton.IsEnabled = !sessionOwnsCondor;
                Logger.SessionError(sessionId, sessionOwnsCondor
                    ? "Condor uit deze sessie draait nog; een nieuwe vlucht blijft geblokkeerd."
                    : "Bestaande Condor-sessie draait nog; een nieuwe start wordt bij de volgende poging opnieuw gecontroleerd.");
            }
            _flightLock.Release();
        }
    }

    private async Task<bool> EnsureNoExistingCondorAsync()
    {
        while (CondorProcessService.AnyRunning())
        {
            var dialog = new CondorRunningWindow("Condor draait nog.\n\nSluit de bestaande Condor-sessie voordat een nieuwe vlucht wordt gestart.") { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Choice == CondorRunningChoice.Cancel) return false;
            await Task.Delay(250, _lifetime.Token);
        }
        return true;
    }

    private async Task WaitForManualCondorExitAsync(string sessionId)
    {
        while (CondorProcessService.AnyRunning())
        {
            RestoreLauncher();
            var dialog = new CondorRunningWindow("Condor kon niet volledig worden afgesloten.\n\n" +
                "Sluit Condor via Taakbeheer en klik daarna op 'Opnieuw controleren'.") { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Choice == CondorRunningChoice.Cancel)
            {
                Logger.SessionInfo(sessionId, "Dialoog geannuleerd; Condor blijft op de achtergrond bewaakt tot het proces eindigt.");
                while (CondorProcessService.AnyRunning()) await Task.Delay(500, _lifetime.Token);
                return;
            }
            Logger.SessionInfo(sessionId, "Gebruiker heeft opnieuw controleren gekozen.");
            await Task.Delay(250, _lifetime.Token);
        }
    }

    private void Transition(FlightSessionState state, string sessionId, string message)
    {
        _flightState.MoveTo(state); Logger.SessionInfo(sessionId, $"Status -> {state}. {message}");
    }

    private void RestoreLauncher()
    {
        if (_isClosing || !IsLoaded || Dispatcher.HasShutdownStarted) return;
        Show(); WindowState = WindowState.Maximized; ShowInTaskbar = true;
        if (!Activate()) { Topmost = true; Topmost = false; }
    }

    private void ResetSelections()
    {
        BuildScenarioMenu(); ScreenMode.IsChecked = true; VrMode.IsChecked = false;
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
    protected override void OnClosed(EventArgs e) { _isClosing = true; _lifetime.Cancel(); base.OnClosed(e); }
}
