using Avalonia.Controls;
using Avalonia.Interactivity;
using ColorSorterGUI.Services;

namespace ColorSorterGUI.Views;

public partial class ControlView : Window
{
    private readonly RobotService _robot = new();

    public ControlView()
    {
        InitializeComponent();
    }

    private async void SortBlue_Click(object? sender, RoutedEventArgs e)
    {
        StatusText.Text = "Sorting blue...";
        await _robot.SendScriptAsync("blaa_26.script");
        StatusText.Text = "Command sent.";
    }

    private async void SortGreen_Click(object? sender, RoutedEventArgs e)
    {
        StatusText.Text = "Sorting green...";
        await _robot.SendScriptAsync("groen_26.script");
        StatusText.Text = "Command sent.";
    }

    private async void SortRed_Click(object? sender, RoutedEventArgs e)
    {
        StatusText.Text = "Sorting red...";
        await _robot.SendScriptAsync("roed_26.script");
        StatusText.Text = "Command sent.";
    }
}
