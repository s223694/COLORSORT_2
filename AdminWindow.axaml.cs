using Avalonia.Controls;
using Avalonia.Interactivity;
using ColorSorterGUI.Services;
using System;

namespace ColorSorterGUI.Views;

public partial class AdminWindow : Window
{
    private readonly string _connStr;
    private readonly UserRepository _users;

    public AdminWindow()
    {
        InitializeComponent();

        _connStr = DatabaseService.GetConnectionString();
        _users = new UserRepository(_connStr);
    }

    private async void Create_Click(object? sender, RoutedEventArgs e)
    {
        AdminStatusText.Text = "";

        var u = NewUsernameBox.Text?.Trim() ?? "";
        var p = NewPasswordBox.Text ?? "";
        var role = (RoleBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "User";

        if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p))
        {
            AdminStatusText.Text = "Username and password are required.";
            return;
        }

        try
        {
            await _users.CreateUserAsync(u, p, role);
            AdminStatusText.Text = $"Created user '{u}' as {role}.";
            NewUsernameBox.Text = "";
            NewPasswordBox.Text = "";
            RoleBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            AdminStatusText.Text = ex.Message;
        }
    }

    private void OpenControl_Click(object? sender, RoutedEventArgs e)
    {
        var w = new ControlView();
        w.Show();
    }

    private void Logout_Click(object? sender, RoutedEventArgs e)
    {
        var login = new LoginWindow();
        login.Show();
        Close();
    }
}
