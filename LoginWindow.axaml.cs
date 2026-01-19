using Avalonia.Controls;
using Avalonia.Interactivity;
using ColorSorterGUI.Services;
using System;

namespace ColorSorterGUI.Views;

public partial class LoginWindow : Window
{
    private readonly string _connStr;
    private readonly UserRepository _users;

    public LoginWindow()
    {
        InitializeComponent();

        _connStr = DatabaseService.GetConnectionString();
        _users = new UserRepository(_connStr);

        // disable login until DB + default admin is ready
        LoginButton.IsEnabled = false;
        _ = EnsureDbAndAdminAsync();
    }

    private async System.Threading.Tasks.Task EnsureDbAndAdminAsync()
    {
        try
        {
            ErrorText.Text = "Initializing database...";
            await DatabaseService.InitializeAsync();

            // Create default admin if it doesn't exist
            await _users.EnsureDefaultAdminAsync("admin", "admin123");

            ErrorText.Text = "Ready. Login with admin / admin123";
        }
        catch (Exception ex)
        {
            ErrorText.Text = $"Init failed: {ex.Message}";
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private async void Login_Click(object? sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";

        var u = UsernameBox.Text?.Trim() ?? "";
        var p = PasswordBox.Text ?? "";

        if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p))
        {
            ErrorText.Text = "Enter username and password.";
            return;
        }

        try
        {
            var (ok, role) = await _users.AuthenticateAsync(u, p);
            if (!ok)
            {
                ErrorText.Text = "Invalid username/password.";
                return;
            }

            Window next = role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                ? new AdminWindow()
                : new ControlView();

            next.Show();
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void Exit_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
