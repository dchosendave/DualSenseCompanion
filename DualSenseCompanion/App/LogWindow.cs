namespace DualSenseCompanion.App;

public sealed class LogWindow : Form
{
    private readonly TextBox _text;

    public LogWindow(Icon? icon)
    {
        Text = "DualSense Companion - Log";
        Width = 760;
        Height = 460;
        StartPosition = FormStartPosition.CenterScreen;

        if (icon != null)
        {
            Icon = icon;
        }

        _text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            WordWrap = false,
        };

        Controls.Add(_text);

        _text.Lines = Log.RecentLines();
        Log.LineAdded += OnLineAdded;
    }

    private void OnLineAdded(string line)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() =>
        {
            _text.AppendText(line + Environment.NewLine);
        });
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _text.SelectionStart = _text.TextLength;
        _text.ScrollToCaret();
    }

    //Closing just hides the window so it can be reopened from the tray
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        Log.LineAdded -= OnLineAdded;
        base.OnFormClosing(e);
    }
}
