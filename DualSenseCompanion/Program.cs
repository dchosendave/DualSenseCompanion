using DualSenseCompanion.App;
using Microsoft.Win32;

namespace DualSenseCompanion;

public static class Program
{
    [STAThread]
    public static int Main()
    {
        Log.Initialize();

        //Two copies would fight over the same controller
        using var singleInstance = new Mutex(true, @"Local\DualSenseCompanion", out bool isFirstInstance);

        if (!isFirstInstance)
        {
            MessageBox.Show("DualSense Companion is already running. Look for its icon in the system tray.",
                "DualSense Companion", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.ThreadException += (s, e) => Console.WriteLine($"Unexpected error: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Console.WriteLine($"Fatal error: {e.ExceptionObject}");

        Console.WriteLine("DualSense Companion starting...");

        if (!DependencyManager.CheckAndInstallDependencies())
        {
            return 1;
        }

        Console.WriteLine("All dependencies verified.");

        var settings = Settings.Load();
        Console.WriteLine($"Settings: {Settings.FilePath}");

        CloakManager.Initialize();

        ControllerService service;

        try
        {
            service = new ControllerService(settings);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not create the virtual controller: {ex}");
            MessageBox.Show($"Could not create the virtual controller.\n\n{ex.Message}\n\nMake sure the ViGEmBus driver is installed, then restart DualSense Companion.\n\nDetails: {Log.FilePath}",
                "DualSense Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
            CloakManager.UnhidePS5Controller();
            return 1;
        }

        //Logoff or shutdown: restore the controller before Windows kills the process
        SystemEvents.SessionEnding += (s, e) =>
        {
            service.Stop();
            CloakManager.UnhidePS5Controller();
        };

        service.Start();
        Application.Run(new TrayApp(service));

        Console.WriteLine("Exiting...");
        service.Dispose();
        CloakManager.UnhidePS5Controller();
        return 0;
    }
}
