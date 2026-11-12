using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DualSenseCompanion.App;
using DualSenseCompanion.Device;
using DualSenseCompanion.Features;

namespace DualSenseCompanion.UI.Pages;

public partial class TestPage : UserControl, ILivePage
{
    private enum ChipState { Untested, Pressed, Tested }

    private enum Phase { Idle, Rest, Circle, Triggers }

    private const double RestSeconds = 3;
    private const double CircleSeconds = 6;
    private const double TriggerSeconds = 6;

    private static readonly Brush UntestedBorder = Frozen(0xE5, 0xE5, 0xEA);
    private static readonly Brush UntestedText = Frozen(0x6E, 0x6E, 0x73);
    private static readonly Brush TestedBack = Frozen(0xEA, 0xF7, 0xEE);
    private static readonly Brush TestedBorder = Frozen(0xA7, 0xD9, 0xB5);
    private static readonly Brush TestedText = Frozen(0x14, 0x6C, 0x2E);
    private static readonly Brush PressedBack = Frozen(0xEA, 0xF0, 0xFF);
    private static readonly Brush PressedBorder = Frozen(0x2F, 0x6B, 0xFF);
    private static readonly Brush Pass = Frozen(0x16, 0xA3, 0x4A);
    private static readonly Brush Fail = Frozen(0xDC, 0x26, 0x26);
    private static readonly Brush Skipped = Frozen(0x9B, 0xA0, 0xAA);

    private readonly List<(ChecklistItem Item, Border Chip, TextBlock Text)> _chips = new();
    private readonly ChipState[] _shown;
    private readonly HashSet<ChecklistItem> _tested = new();

    //Health check
    private readonly Stopwatch _phaseClock = new Stopwatch();
    private Phase _phase = Phase.Idle;
    private readonly List<(byte X, byte Y)> _restLeft = new(), _restRight = new(), _circleLeft = new(), _circleRight = new();
    private readonly List<float> _restGyro = new();
    private byte _restL2, _restR2, _pressL2, _pressR2;

    private sealed record ResultRow(string Icon, Brush IconBrush, string Name, string Detail);

    public TestPage()
    {
        InitializeComponent();
        BuildChecklist();
        _shown = new ChipState[_chips.Count];
        Array.Fill(_shown, (ChipState)(-1));
        UpdateProgress();
    }

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void BuildChecklist()
    {
        foreach (var group in ControllerChecklist.Items.GroupBy(i => i.Group))
        {
            Groups.Children.Add(new TextBlock
            {
                Text = group.Key,
                Style = (Style)FindResource("Caption"),
                Margin = new Thickness(0, 6, 0, 4),
            });

            var row = new WrapPanel();

            foreach (var item in group)
            {
                var text = new TextBlock { Text = item.Name, FontSize = 12.5 };
                var chip = new Border
                {
                    Child = text,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(9, 3, 9, 3),
                    Margin = new Thickness(0, 0, 6, 6),
                    BorderThickness = new Thickness(1),
                };

                row.Children.Add(chip);
                _chips.Add((item, chip, text));
            }

            Groups.Children.Add(row);
        }
    }

    public void Update(LiveSnapshot snapshot)
    {
        var input = snapshot.Input;
        float gyro = Math.Max(Math.Abs(snapshot.GyroPitch), Math.Max(Math.Abs(snapshot.GyroYaw), Math.Abs(snapshot.GyroRoll)));

        for (int i = 0; i < _chips.Count; i++)
        {
            var (item, chip, text) = _chips[i];
            bool active = input != null && item.IsActive(input, input.HasExtendedData ? gyro : 0);

            if (active)
            {
                _tested.Add(item);
            }

            var state = active ? ChipState.Pressed : _tested.Contains(item) ? ChipState.Tested : ChipState.Untested;

            if (state == _shown[i])
            {
                continue;
            }

            _shown[i] = state;

            switch (state)
            {
                case ChipState.Pressed:
                    chip.Background = PressedBack;
                    chip.BorderBrush = PressedBorder;
                    text.Foreground = PressedBorder;
                    break;
                case ChipState.Tested:
                    chip.Background = TestedBack;
                    chip.BorderBrush = TestedBorder;
                    text.Foreground = TestedText;
                    break;
                default:
                    chip.Background = Brushes.Transparent;
                    chip.BorderBrush = UntestedBorder;
                    text.Foreground = UntestedText;
                    break;
            }
        }

        UpdateProgress();
        RunHealthCheck(snapshot);
    }

    private void UpdateProgress()
    {
        int total = _chips.Count;
        int done = _tested.Count;
        ChecklistProgress.Text = done == total ? $"All {total} inputs work." : $"{done} of {total} tested";
        ChecklistFill.Width = ChecklistBar.ActualWidth * done / Math.Max(1, total);
    }

    private void ResetChecklist_Click(object sender, RoutedEventArgs e)
    {
        _tested.Clear();
        Array.Fill(_shown, (ChipState)(-1));
    }

    private void StartHealth_Click(object sender, RoutedEventArgs e)
    {
        _restLeft.Clear();
        _restRight.Clear();
        _circleLeft.Clear();
        _circleRight.Clear();
        _restGyro.Clear();
        _restL2 = _restR2 = _pressL2 = _pressR2 = 0;
        HealthResults.ItemsSource = null;
        Verdict.Text = "";

        HealthButton.IsEnabled = false;
        StepPanel.Visibility = Visibility.Visible;
        StartPhase(Phase.Rest);
    }

    private void StartPhase(Phase phase)
    {
        _phase = phase;
        _phaseClock.Restart();
    }

    private void RunHealthCheck(LiveSnapshot snapshot)
    {
        if (_phase == Phase.Idle)
        {
            return;
        }

        var input = snapshot.Input;

        if (input == null || !snapshot.Status.Connected)
        {
            StopHealthCheck("Controller disconnected. Connect it and start again.");
            return;
        }

        double t = _phaseClock.Elapsed.TotalSeconds;

        switch (_phase)
        {
            case Phase.Rest:
                ShowStep("Step 1 of 3: Put the controller down", "Lay it on a table and don't touch it.", t / RestSeconds);
                _restLeft.Add((input.LeftX, input.LeftY));
                _restRight.Add((input.RightX, input.RightY));
                _restL2 = Math.Max(_restL2, input.L2);
                _restR2 = Math.Max(_restR2, input.R2);

                if (input.HasExtendedData)
                {
                    _restGyro.Add(snapshot.GyroYaw);
                }

                if (t >= RestSeconds)
                {
                    StartPhase(Phase.Circle);
                }
                break;

            case Phase.Circle:
                ShowStep("Step 2 of 3: Rotate both sticks", "Pick it up and slowly roll both sticks around their outer edge, a few full circles.", t / CircleSeconds);
                _circleLeft.Add((input.LeftX, input.LeftY));
                _circleRight.Add((input.RightX, input.RightY));

                if (t >= CircleSeconds)
                {
                    StartPhase(Phase.Triggers);
                }
                break;

            case Phase.Triggers:
                ShowStep("Step 3 of 3: Press both triggers", "Press L2 and R2 all the way down.", t / TriggerSeconds);
                _pressL2 = Math.Max(_pressL2, input.L2);
                _pressR2 = Math.Max(_pressR2, input.R2);

                if (t >= TriggerSeconds || (_pressL2 >= 250 && _pressR2 >= 250 && t > 1))
                {
                    FinishHealthCheck();
                }
                break;
        }
    }

    private void ShowStep(string title, string detail, double progress)
    {
        StepTitle.Text = title;
        StepDetail.Text = detail;
        StepFill.Width = StepBar.ActualWidth * Math.Clamp(progress, 0, 1);
    }

    private void FinishHealthCheck()
    {
        var results = new List<HealthResult>
        {
            ControllerHealth.StickDrift("Left stick", _restLeft),
            ControllerHealth.StickDrift("Right stick", _restRight),
            ControllerHealth.StickJitter("Left stick", _restLeft),
            ControllerHealth.StickJitter("Right stick", _restRight),
            ControllerHealth.StickReach("Left stick", _circleLeft),
            ControllerHealth.StickReach("Right stick", _circleRight),
            ControllerHealth.Trigger("L2", _restL2, _pressL2),
            ControllerHealth.Trigger("R2", _restR2, _pressR2),
            ControllerHealth.GyroNoise(_restGyro),
        };

        HealthResults.ItemsSource = results.Select(r => r.Result switch
        {
            CheckResult.Pass => new ResultRow("", Pass, r.Name, r.Detail),
            CheckResult.Fail => new ResultRow("", Fail, r.Name, r.Detail),
            _ => new ResultRow("", Skipped, r.Name, r.Detail),
        }).ToList();

        Console.WriteLine("Health check: " + string.Join("; ", results.Select(r => $"{r.Name}: {r.Result} ({r.Detail})")));
        StopHealthCheck(ControllerHealth.Verdict(results));
    }

    private void StopHealthCheck(string message)
    {
        _phase = Phase.Idle;
        _phaseClock.Reset();
        StepPanel.Visibility = Visibility.Collapsed;
        HealthButton.IsEnabled = true;
        HealthButton.Content = "Run again";
        Verdict.Text = message;
    }
}
