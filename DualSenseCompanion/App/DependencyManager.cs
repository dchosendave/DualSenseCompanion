using System.Diagnostics;
using System.ServiceProcess;
using System.Windows.Forms;
using Nefarius.Drivers.HidHide;

namespace DualSenseCompanion.App;

public static class DependencyManager
{
    private const string ViGEmUrl = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";
    private const string HidHideUrl = "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe";

    //Returns false if a required dependency is missing and the app should exit
    public static bool CheckAndInstallDependencies()
    {
        bool hidHideWasInstalled = false;

        if (!IsViGEmInstalled())
        {
            if (!AskAndInstall("ViGEmBus driver", "It creates the virtual controller that games see.", ViGEmUrl, IsViGEmInstalled))
            {
                ShowError("DualSense Companion can't run without the ViGEmBus driver.");
                return false;
            }
        }

        if (!IsHidHideInstalled())
        {
            if (!AskAndInstall("HidHide driver", "It hides the real controller from games so they don't see double input.", HidHideUrl, IsHidHideInstalled))
            {
                ShowError("DualSense Companion can't run without the HidHide driver.");
                return false;
            }

            hidHideWasInstalled = true;
        }

        if (hidHideWasInstalled && !IsHidHideServiceRunning())
        {
            DialogResult result = MessageBox.Show(
                "HidHide has been installed. A restart is required for changes to take effect.\nRestart now?",
                "Restart Required",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                RestartSystem();
                return false;
            }
        }

        return true;
    }

    private static bool AskAndInstall(string name, string reason, string url, Func<bool> isInstalled)
    {
        DialogResult answer = MessageBox.Show(
            $"The {name} is not installed. {reason}\n\nDownload and run the official installer from GitHub now?\n{url}",
            "DualSense Companion - Missing driver",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
        {
            return false;
        }

        Console.WriteLine($"{name} not found. Downloading...");

        if (DownloadAndRun(url) && isInstalled())
        {
            Console.WriteLine($"{name} installed successfully.");
            return true;
        }

        Console.WriteLine($"Failed to install {name}.");
        return false;
    }

    private static bool IsViGEmInstalled()
    {
        //ViGEmBus.sys driver file check
        string driverPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "ViGEmBus.sys");

        if (!File.Exists(driverPath))
        {
            return false;
        }

        //ViGEmBus service installed and running check
        try
        {
            using var service = new ServiceController("ViGEmBus");
            return service.Status != ServiceControllerStatus.Stopped;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsHidHideInstalled()
    {
        try
        {
            return new HidHideControlService().IsInstalled;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsHidHideServiceRunning()
    {
        try
        {
            using var service = new ServiceController("HidHide");
            return service.Status == ServiceControllerStatus.Running;
        }
        catch
        {
            return false;
        }
    }

    private static bool DownloadAndRun(string url)
    {
        string installerPath = Path.Combine(Path.GetTempPath(), Path.GetFileName(new Uri(url).LocalPath));

        try
        {
            using (var http = new HttpClient())
            using (var response = http.GetAsync(url).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();

                using var file = File.Create(installerPath);
                response.Content.CopyToAsync(file).GetAwaiter().GetResult();
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
            });

            process?.WaitForExit();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error installing from {url}: {ex.Message}");
            return false;
        }
    }

    private static void ShowError(string message)
    {
        Console.WriteLine(message);
        MessageBox.Show(message, "DualSense Companion", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private static void RestartSystem()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown",
            Arguments = "/r /t 5",
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }
}
