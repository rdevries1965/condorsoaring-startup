using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GoZCCondorLauncher;

internal sealed class PasswordPromptWindow : Window
{
    private readonly PasswordSettings _settings;
    private readonly PasswordBox _password = new() { Margin = new Thickness(0, 8, 0, 8), MinWidth = 300 };
    private readonly TextBlock _message = new() { Foreground = System.Windows.Media.Brushes.DarkRed, TextWrapping = TextWrapping.Wrap };
    private readonly Button _submit = new() { Content = "Ontgrendelen", IsDefault = true, MinWidth = 130, Margin = new Thickness(6) };
    private int _attempts;

    public PasswordPromptWindow(PasswordSettings settings)
    {
        _settings = settings;
        Title = "Beheerder aanmelden";
        Width = 430; Height = 250; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Voer het beheerderswachtwoord in.", FontSize = 18, FontWeight = FontWeights.Bold });
        panel.Children.Add(_password); panel.Children.Add(_message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Annuleren", IsCancel = true, MinWidth = 110, Margin = new Thickness(6) };
        _submit.Click += Submit_Click; buttons.Children.Add(cancel); buttons.Children.Add(_submit); panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => _password.Focus();
    }

    private void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (SecurityService.VerifyPassword(_password.Password, _settings)) { DialogResult = true; return; }
        _attempts++; _password.Clear(); _message.Text = "Onjuist wachtwoord.";
        if (_attempts >= 3) BeginLockout();
    }

    private void BeginLockout()
    {
        var remaining = 30;
        _submit.IsEnabled = false; _password.IsEnabled = false;
        _message.Text = $"Te veel onjuiste pogingen. Wacht {remaining} seconden.";
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            remaining--;
            if (remaining > 0) { _message.Text = $"Te veel onjuiste pogingen. Wacht {remaining} seconden."; return; }
            timer.Stop(); _attempts = 0; _submit.IsEnabled = true; _password.IsEnabled = true;
            _message.Text = "Probeer het opnieuw."; _password.Focus();
        };
        timer.Start();
    }
}

internal sealed class SetPasswordWindow : Window
{
    private readonly PasswordBox _first = new() { Margin = new Thickness(0, 5, 0, 8) };
    private readonly PasswordBox _second = new() { Margin = new Thickness(0, 5, 0, 8) };
    private readonly TextBlock _message = new() { Foreground = System.Windows.Media.Brushes.DarkRed };
    public PasswordSettings? Result { get; private set; }

    public SetPasswordWindow(string title = "Beheerderswachtwoord instellen")
    {
        Title = title; Width = 470; Height = 330; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Kies een wachtwoord van minimaal 6 tekens.", FontSize = 17, FontWeight = FontWeights.Bold });
        panel.Children.Add(new TextBlock { Text = "Nieuw wachtwoord" }); panel.Children.Add(_first);
        panel.Children.Add(new TextBlock { Text = "Herhaal nieuw wachtwoord" }); panel.Children.Add(_second); panel.Children.Add(_message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Annuleren", IsCancel = true, MinWidth = 110, Margin = new Thickness(6) });
        var save = new Button { Content = "Bevestigen", IsDefault = true, MinWidth = 120, Margin = new Thickness(6) };
        save.Click += Save_Click; buttons.Children.Add(save); panel.Children.Add(buttons); Content = panel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_first.Password.Length < SecurityService.MinimumPasswordLength) { _message.Text = "Het wachtwoord moet minimaal 6 tekens bevatten."; return; }
        if (_first.Password != _second.Password) { _message.Text = "De twee wachtwoorden zijn niet gelijk."; return; }
        Result = SecurityService.CreatePassword(_first.Password); DialogResult = true;
    }
}

internal sealed class ChangePasswordWindow : Window
{
    private readonly PasswordSettings _currentSettings;
    private readonly PasswordBox _current = new() { Margin = new Thickness(0, 4, 0, 7) };
    private readonly PasswordBox _first = new() { Margin = new Thickness(0, 4, 0, 7) };
    private readonly PasswordBox _second = new() { Margin = new Thickness(0, 4, 0, 7) };
    private readonly TextBlock _message = new() { Foreground = System.Windows.Media.Brushes.DarkRed };
    public PasswordSettings? Result { get; private set; }

    public ChangePasswordWindow(PasswordSettings currentSettings)
    {
        _currentSettings = currentSettings; Title = "Beheerderswachtwoord wijzigen";
        Width = 470; Height = 400; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Beheerderswachtwoord wijzigen", FontSize = 18, FontWeight = FontWeights.Bold });
        AddField(panel, "Huidig wachtwoord", _current); AddField(panel, "Nieuw wachtwoord", _first); AddField(panel, "Herhaal nieuw wachtwoord", _second);
        panel.Children.Add(_message);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "Annuleren", IsCancel = true, MinWidth = 110, Margin = new Thickness(6) });
        var save = new Button { Content = "Wijzigen", IsDefault = true, MinWidth = 110, Margin = new Thickness(6) };
        save.Click += Save_Click; buttons.Children.Add(save); panel.Children.Add(buttons); Content = panel;
    }

    private static void AddField(Panel panel, string label, PasswordBox box) { panel.Children.Add(new TextBlock { Text = label }); panel.Children.Add(box); }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!SecurityService.VerifyPassword(_current.Password, _currentSettings)) { _message.Text = "Onjuist wachtwoord."; return; }
        if (_first.Password.Length < SecurityService.MinimumPasswordLength) { _message.Text = "Het nieuwe wachtwoord moet minimaal 6 tekens bevatten."; return; }
        if (_first.Password != _second.Password) { _message.Text = "De twee nieuwe wachtwoorden zijn niet gelijk."; return; }
        Result = SecurityService.CreatePassword(_first.Password); DialogResult = true;
    }
}
