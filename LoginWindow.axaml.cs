using Avalonia.Controls;
using Avalonia.Interactivity;
using ColorSorterGUI.Services;
using System;

namespace ColorSorterGUI.Views;

// LoginWindow er første vindue i appen.
// Formål:
// 1) Sørge for at databasen er initialiseret (tabeller + seed)
// 2) Sikre at der findes en default admin-konto (første gang appen kører)
// 3) Logge brugeren ind og navigere videre afhængigt af rolle (Admin/User)
public partial class LoginWindow : Window
{
    // Connection string til SQLite DB (samme som resten af appen bruger).
    private readonly string _connStr;

    // Repository til Users tabellen (login + oprettelse af default admin).
    private readonly UserRepository _users;

    public LoginWindow()
    {
        InitializeComponent();

        // DB connection string baseret på DatabaseService (AppData/.../colorsorter.sqlite)
        _connStr = DatabaseService.GetConnectionString();

        // Repository til at arbejde med Users tabellen
        _users = new UserRepository(_connStr);

        // Vi deaktiverer login-knappen indtil databasen er klar,
        // så brugeren ikke kan trykke "Login" før Users-tabellen findes.
        LoginButton.IsEnabled = false;

        // Start init i baggrunden (async) uden at blokere UI.
        _ = EnsureDbAndAdminAsync();
    }

    // Initialiserer DB + sikrer default admin.
    // Idé: Første gang appen kører, findes der ingen DB og ingen brugere,
    // så vi skal oprette tabeller + seed og lave en admin.
    private async System.Threading.Tasks.Task EnsureDbAndAdminAsync()
    {
        try
        {
            // Status til brugeren (bruges her som info-tekst)
            ErrorText.Text = "Initializing database...";

            // Opretter tabeller hvis de ikke findes (Inventory + Users)
            await DatabaseService.InitializeAsync();

            // Opretter default admin hvis den ikke findes i forvejen.
            // Hvis brugeren allerede findes, gør metoden ingenting (idempotent).
            //
            // NB: Credentials er hardcoded her -> fint i skoleprojekt,
            // men i en "rigtig" app ville det være en risiko.
            await _users.EnsureDefaultAdminAsync("admin", "admin123");

            // Info til test: fortæller hvad man kan logge ind med.
            ErrorText.Text = "Ready. Login with admin / admin123";
        }
        catch (Exception ex)
        {
            // Hvis DB init fejler, kan login ikke fungere.
            ErrorText.Text = $"Init failed: {ex.Message}";
        }
        finally
        {
            // Vi aktiverer login-knappen uanset hvad,
            // så brugeren kan prøve igen eller se fejlmeddelelsen.
            // (Man kunne også vælge kun at enable ved succes.)
            LoginButton.IsEnabled = true;
        }
    }

    // Click handler for login knappen.
    // Autentificerer user og åbner næste vindue afhængigt af rolle.
    private async void Login_Click(object? sender, RoutedEventArgs e)
    {
        // Ryd tidligere fejlstatus
        ErrorText.Text = "";

        // Læs input fra UI
        var u = UsernameBox.Text?.Trim() ?? "";
        var p = PasswordBox.Text ?? "";

        // Basis-validering
        if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p))
        {
            ErrorText.Text = "Enter username and password.";
            return;
        }

        try
        {
            // AuthenticateAsync:
            // - finder user i DB
            // - verificerer password via PasswordHasher
            // - returnerer (ok, role)
            var (ok, role) = await _users.AuthenticateAsync(u, p);

            if (!ok)
            {
                ErrorText.Text = "Invalid username/password.";
                return;
            }

            // Rolle-baseret navigation:
            // Admin -> AdminWindow (kan oprette brugere + åbne control)
            // User  -> ControlView (direkte til robot + inventory)
            Window next = role.Equals("Admin", StringComparison.OrdinalIgnoreCase)
                ? new AdminWindow()
                : new ControlView();

            // Vis næste vindue og luk login-vinduet
            next.Show();
            Close();
        }
        catch (Exception ex)
        {
            // Fx DB fejl, connection problemer, osv.
            ErrorText.Text = ex.Message;
        }
    }

    // Lukker hele appen (eller i hvert fald login-vinduet).
    private void Exit_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

