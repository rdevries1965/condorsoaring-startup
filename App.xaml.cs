using System.Windows;

namespace GoZCCondorLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Onverwachte fout", MessageBoxButton.OK, MessageBoxImage.Error);
            Logger.Error(args.Exception.ToString());
            args.Handled = true;
        };
        base.OnStartup(e);

        try
        {
            var appSettings = ConfigurationService.LoadAppSettings();
            ConfigurationService.TryLoadUserSettings(out var userSettings);
            ConfigurationMigration.Migrate(appSettings, userSettings);
            if (!ConfigurationService.SettingsAreComplete(userSettings))
            {
                if (userSettings.AdministratorPassword is null)
                {
                    var passwordWindow = new SetPasswordWindow();
                    if (passwordWindow.ShowDialog() != true) { Shutdown(); return; }
                    userSettings.AdministratorPassword = passwordWindow.Result;
                }
                var wizard = new SetupWizardWindow(appSettings, userSettings, true);
                if (wizard.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
                userSettings = wizard.Result;
            }

            var mainWindow = new MainWindow(appSettings, userSettings);
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Logger.Error(ex.ToString());
            MessageBox.Show(ex.Message, "Configuratiefout", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }
}
