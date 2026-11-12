using System.Windows;
using DualSenseCompanion.App;
using DualSenseCompanion.UI;
using DualSenseCompanion.UI.Pages;
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
        Log.Initialize();

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

        WinForms.Application.EnableVisualStyles();
        WinForms.Application.SetHighDpiMode(WinForms.HighDpiMode.PerMonitorV2);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Console.WriteLine($"Fatal error: {e.ExceptionObject}");

        Console.WriteLine("DualSense Companion starting...");

        if (exitRequested)
        {
            //Nothing is running, so there is nothing to close
            return 0;
        }

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
            WinForms.MessageBox.Show($"Could not create the virtual controller.\n\n{ex.Message}\n\nMake sure the ViGEmBus driver is installed, then restart DualSense Companion.\n\nDetails: {Log.FilePath}",
                "DualSense Companion", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error);
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

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/DualSenseCompanion;component/UI/Theme.xaml"),
        });
        app.DispatcherUnhandledException += (s, e) =>
        {
            Console.WriteLine($"Unexpected UI error: {e.Exception}");
            e.Handled = true;
        };

        var window = new MainWindow(service);
        using var tray = new TrayIcon(service, app.Dispatcher);
        bool toldAboutTray = false;

        void ShowWindow() => window.BringToFront();

        tray.OpenRequested += ShowWindow;
        tray.PageRequested += page =>
        {
            window.Navigate(page);
            ShowWindow();
        };
        tray.SettingsChanged += window.OnSettingsReplaced;
        void ExitApp()
        {
            window.AllowClose = true;
            window.Close();
            app.Shutdown();
        }

        tray.ExitRequested += ExitApp;

        SettingsPage.SettingsReloaded += window.OnSettingsReplaced;

        window.HiddenToTray += () =>
        {
            if (!toldAboutTray)
            {
                toldAboutTray = true;
                tray.ShowMessage("Still running in the tray so your controller keeps working. Right-click the icon to exit.");
            }
        };

        //A second launch signals this event
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var showThread = new Thread(() =>
        {
            while (showEvent.WaitOne())
            {
                if (app.Dispatcher.HasShutdownStarted)
                {
                    return;
                }

                app.Dispatcher.BeginInvoke(ShowWindow);
            }
        })
        { IsBackground = true, Name = "Show window listener" };
        showThread.Start();

        using var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        var exitThread = new Thread(() =>
        {
            if (exitEvent.WaitOne() && !app.Dispatcher.HasShutdownStarted)
            {
                app.Dispatcher.BeginInvoke(ExitApp);
            }
        })
        { IsBackground = true, Name = "Exit listener" };
        exitThread.Start();

        bool startMinimized = settings.StartMinimized ||
                              args.Contains(Autostart.MinimizedArgument, StringComparer.OrdinalIgnoreCase);

        if (startMinimized)
        {
            tray.ShowMessage("Running in the system tray. Click the icon to open.");
        }
        else
        {
            window.Show();
        }

        app.Run();

        Console.WriteLine("Exiting...");
        service.Dispose();
        CloakManager.UnhidePS5Controller();
        return 0;
    }
}
