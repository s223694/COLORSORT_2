using Avalonia.Controls;
using Avalonia.Interactivity;
using ColorSorterGUI.Services;
using System;

namespace ColorSorterGUI.Views;

// AdminWindow er et vindue kun til admins.
// Formål:
// 1) Oprette nye brugere (Admin/User) i databasen
// 2) Åbne kontrolpanelet (ControlView) for robot/inventory
// 3) Logge ud og gå tilbage til LoginWindow
public partial class AdminWindow : Window
{
    // Connection string til SQLite databasen (bruges af UserRepository).
    private readonly string _connStr;

    // Repository der håndterer oprettelse og autentificering af brugere.
    private readonly UserRepository _users;

    public AdminWindow()
    {
        InitializeComponent();

        // Henter connection string baseret på appens DB-path.
        // DatabaseService sørger for at stien er stabil (AppData/.../colorsorter.sqlite).
        _connStr = DatabaseService.GetConnectionString();

        // Opretter et UserRepository som kan lave DB-queries på Users tabellen.
        _users = new UserRepository(_connStr);
    }

    // Click handler for "Create user" knappen.
    // Opretter en ny bruger i databasen ud fra felterne i UI.
    private async void Create_Click(object? sender, RoutedEventArgs e)
    {
        // Nulstil status-besked, så vi ikke viser gammel fejl/succes.
        AdminStatusText.Text = "";

        // Læs input fra UI og gør det lidt robust:
        // - Trim username
        // - Password bruger vi som det er (ingen trim, så mellemrum kan være en del af password hvis man vil)
        var u = NewUsernameBox.Text?.Trim() ?? "";
        var p = NewPasswordBox.Text ?? "";

        // Hent rollen fra ComboBox (default "User" hvis noget går galt)
        var role = (RoleBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "User";

        // Input-validering: kræv at begge felter er udfyldt.
        if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p))
        {
            AdminStatusText.Text = "Username and password are required.";
            return;
        }

        try
        {
            // Opret bruger i DB.
            // UserRepository sørger for at hashe password + salt, og gemme role.
            await _users.CreateUserAsync(u, p, role);

            // Vis success til admin
            AdminStatusText.Text = $"Created user '{u}' as {role}.";

            // Ryd felter i UI efter success (god UX)
            NewUsernameBox.Text = "";
            NewPasswordBox.Text = "";
            RoleBox.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            // Hvis DB fejler (fx username allerede findes pga UNIQUE constraint),
            // eller role ikke er gyldig, viser vi fejlen.
            AdminStatusText.Text = ex.Message;
        }
    }

    // Åbner ControlView vinduet (robot + inventory kontrol).
    private void OpenControl_Click(object? sender, RoutedEventArgs e)
    {
        // Opret nyt kontrol-vindue og vis det.
        // OBS: AdminWindow forbliver åbent. Det kan være meningen.
        var w = new ControlView();
        w.Show();
    }

    // Logger ud ved at åbne LoginWindow igen og lukke AdminWindow.
    private void Logout_Click(object? sender, RoutedEventArgs e)
    {
        var login = new LoginWindow();
        login.Show();

        // Lukker admin vinduet, så man ikke kan gå "tilbage" til admin efter logout.
        Close();
    }
}
