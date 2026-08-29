using System.Windows;
using System.Windows.Controls;

namespace GoZCCondorLauncher;

internal enum CondorRunningChoice { Retry, Cancel }

internal sealed class CondorRunningWindow : Window
{
    public CondorRunningChoice Choice { get; private set; } = CondorRunningChoice.Cancel;

    public CondorRunningWindow(string message)
    {
        Title = "Condor draait nog"; Width = 610; Height = 290; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = message, FontSize = 18, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 20) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var open = Button("Condor openen"); open.Click += (_, _) => CondorAutomation.BringRunningCondorToFront();
        var retry = Button("Opnieuw controleren"); retry.Click += (_, _) => { Choice = CondorRunningChoice.Retry; DialogResult = true; };
        var cancel = Button("Annuleren"); cancel.Click += (_, _) => { Choice = CondorRunningChoice.Cancel; DialogResult = false; };
        buttons.Children.Add(open); buttons.Children.Add(retry); buttons.Children.Add(cancel); panel.Children.Add(buttons); Content = panel;
    }

    private static Button Button(string text) => new() { Content = text, Margin = new Thickness(5), Padding = new Thickness(12, 7, 12, 7), MinWidth = 135 };
}
