using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DualSenseCompanion.App;

namespace DualSenseCompanion.UI.Pages;

public partial class LogPage : UserControl
{
    public LogPage()
    {
        InitializeComponent();

        LogText.Text = string.Join(Environment.NewLine, Log.RecentLines()) + Environment.NewLine;
        LogText.CaretIndex = LogText.Text.Length;

        AttachedToVisualTree += (s, e) => Log.LineAdded += OnLine;
        DetachedFromVisualTree += (s, e) => Log.LineAdded -= OnLine;
    }

    private void OnLine(string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogText.Text += line + Environment.NewLine;
            LogText.CaretIndex = LogText.Text?.Length ?? 0;
        });
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;

        if (clipboard != null)
        {
            await clipboard.SetTextAsync(LogText.Text ?? "");
        }
    }

    private void OpenFile_Click(object? sender, RoutedEventArgs e)
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
