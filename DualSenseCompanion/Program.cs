using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DualSenseCompanion.App;
using Microsoft.Win32;
using WinForms = System.Windows.Forms;

namespace DualSenseCompanion;

public static class Program
{
    private const string MutexName = @"Local\DualSenseCompanion";
    private const string ShowEventName = @"Local\DualSenseCompanion.Show";
    private const string ExitEventName = @"Local\DualSenseCompanion.Exit";
    public const string ExitArgument = "--exit";

    [STAThread]
    public static int Main(string[] args)
    {
        //Two copies would fight over the same controller; a second launch just opens the window of the first
        using var singleInstance = new Mutex(true, MutexName, out bool isFirstInstance);

        bool exitRequested = args.Contains(ExitArgument, StringComparer.OrdinalIgnoreCase);

        if (!isFirstInstance)
        {
            //"DualSenseCompanion.exe --exit" closes the running copy cleanly; any other launch opens its window
            try
            {
                using var signal = EventWaitHandle.OpenExisting(exitRequested ? ExitEventName : ShowEventName);
                signal.Set();
            }
            catch
            {
                //First instance is still starting up
            }

            return 0;
        }

        if (exitRequested)
        {
            //Nothing is running, so there is nothing to close
            return 0;
        }

        //Only the first instance owns the log; a second launch must not rotate it
        Log.Initialize();

        //The tray icon's menu uses WinForms visual styles
        WinForms.Application.EnableVisualStyles();
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
            NativeDialog.ShowError($"Could not create the virtual controller.\n\n{ex.Message}\n\nMake sure the ViGEmBus driver is installed, then restart DualSense Companion.\n\nDetails: {Log.FilePath}",
                "DualSense Companion");
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

        CompanionApp.Service = service;
        CompanionApp.StartMinimized = settings.StartMinimized ||
                                      args.Contains(Autostart.MinimizedArgument, StringComparer.OrdinalIgnoreCase);

        //A second launch signals one of these events
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        using var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        StartListener(showEvent, () => CompanionApp.Current?.ShowMainWindow(), "Show window listener");
        StartListener(exitEvent, () => CompanionApp.Current?.ExitApp(), "Exit listener");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);

        Console.WriteLine("Exiting...");
        service.Dispose();
        CloakManager.UnhidePS5Controller();
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<CompanionApp>().UsePlatformDetect();

    private static void StartListener(EventWaitHandle handle, Action onSignal, string name)
    {
        var thread = new Thread(() =>
        {
            while (handle.WaitOne())
            {
                Dispatcher.UIThread.Post(onSignal);
            }
        })
        { IsBackground = true, Name = name };

        thread.Start();
    }
}
