using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using DualSenseCompanion.App;

namespace DualSenseCompanion.UI.Pages;

public partial class LogPage : UserControl
{
    public LogPage()
    {
        InitializeComponent();

        LogText.Text = string.Join(Environment.NewLine, Log.RecentLines()) + Environment.NewLine;
        LogText.ScrollToEnd();

        Loaded += (s, e) => Log.LineAdded += OnLine;
        Unloaded += (s, e) => Log.LineAdded -= OnLine;
    }

    private void OnLine(string line)
    {
        Dispatcher.BeginInvoke(() =>
        {
            LogText.AppendText(line + Environment.NewLine);
            LogText.ScrollToEnd();
        });
    }

    private void Copy_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(LogText.Text);

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Log.FilePath) { UseShellExecute = true });
        }
        catch
        {
            Process.Start("notepad.exe", $"\"{Log.FilePath}\"");
        }
    }
}
