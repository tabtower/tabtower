using System.Threading;

namespace TabTower;

public static class Program
{
    private const string MutexName = "TabTower_SingletonMutex";

    [STAThread]
    public static int Main(string[] args)
    {
        // Any command-line argument means CLI mode: forward to the running instance's pipe.
        if (args.Length > 0)
        {
            // Install commands run locally — they must work before the app has ever started.
            if (args[0] is "install-hooks" or "uninstall-hooks")
                return Cli.HookInstaller.Run(args);
            if (args[0] == "doctor")
                return Cli.SetupCheck.Run(args);

            return Cli.CliClient.Run(args);
        }

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Second UI launch — bring the existing instance to front and exit.
            Cli.CliClient.TrySendActivate();
            return 0;
        }

        // The same lock under the former product name. Holding it keeps an old build from
        // starting next to this one, and finding it taken means an old build is running now:
        // two decks would fight over the reserved zone and both register as the AppBar.
        using var legacyMutex = new Mutex(initiallyOwned: true, Services.LegacyName.Mutex, out bool legacyFree);
        if (!legacyFree)
        {
            System.Windows.MessageBox.Show(
                $"An older version of this app ({Services.LegacyName.Product}) is still running. " +
                "Quit it from its menu, then start TabTower again.",
                "TabTower", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            return 1;
        }

        Services.StartupService.MigrateLegacyValue();
        Services.StartupService.RefreshPathIfStale();

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
