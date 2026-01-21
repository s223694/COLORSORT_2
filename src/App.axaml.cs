using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ColorSorterGUI.Services;
using ColorSorterGUI.Views;
using System.Threading.Tasks;

namespace ColorSorterGUI;

// App er Avalonias "application class".
// Den loader XAML resources/styles og bestemmer hvilket vindue der starter først.
public partial class App : Application
{
    public override void Initialize()
    {
        // Loader App.axaml (styles, resources, themes osv.)
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Classic desktop lifetime betyder "normal desktop app" (Windows/macOS/Linux),
        // hvor MainWindow styrer livscyklus (app lukker typisk når main window lukkes).
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Sæt første vindue brugeren ser:
            // LoginWindow sørger for at brugeren autentificeres og vælger næste vindue (Admin/User).
            desktop.MainWindow = new LoginWindow();

            // Kør DB + default admin init i baggrunden (async) uden at blokere UI-tråden.
            // Der opdateres ikke UI herfra, så det er okay at køre "fire-and-forget".
            _ = InitializeStartupAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Startup init der sikrer at:
    // - databasen/tabeller findes
    // - default admin-konto findes
    //
    // Dette er en "sikkerhedsnet" init: den gør appen robust hvis noget åbner DB før LoginWindow.
    private static async Task InitializeStartupAsync()
    {
        // Opret tabeller + seed hvis nødvendigt
        await DatabaseService.InitializeAsync();

        // Sørg for at default admin findes (hvis den allerede findes, gør den ingenting)
        var users = new UserRepository(DatabaseService.GetConnectionString());
        await users.EnsureDefaultAdminAsync("admin", "admin123");
    }
}
