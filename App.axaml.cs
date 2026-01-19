using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ColorSorterGUI.Services;
using ColorSorterGUI.Views;
using System.Threading.Tasks;

namespace ColorSorterGUI;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new LoginWindow();

            // Run async init in background (but no UI updates needed here)
            _ = InitializeStartupAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeStartupAsync()
    {
        await DatabaseService.InitializeAsync();

        var users = new UserRepository(DatabaseService.GetConnectionString());
        await users.EnsureDefaultAdminAsync("admin", "admin123");
    }
}
