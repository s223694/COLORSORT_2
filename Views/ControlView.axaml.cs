using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ColorSorterGUI.Models;
using ColorSorterGUI.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace ColorSorterGUI.Views;

public partial class ControlView : Window
{
    private readonly RobotService _robot = new();
    private readonly InventoryRepository _inventory = new();

    private readonly ObservableCollection<string> _logLines = new();

    private const int RobotLogPort = 45123; // MUST match URScript gui_port

    // Anti double-count for same "Place DONE" line
    private string? _lastPlaceLine;

    // We prefer this for counting:
    // STEP EyesLocate DONE cnt=2
    private int _lastEyesLocateCount = 1;

    // Fallback if cnt isn't available
    private int _lastEyesWorkpCount = 1;

    // ---- SORT ALL QUEUE ----
    private bool _queueRunning = false;
    private int _queueIndex = 0;

    // NOTE: RunName must match your URScript: "RUN <name> END"
    private readonly (string Script, string RunName)[] _queue =
    {
        ("blaa_26.script",  "blaa_26"),
        ("groen_26.script", "groen_26"),
        ("roed_26.script",  "roed_26"),
    };

    public ControlView()
    {
        InitializeComponent();

        LogList.ItemsSource = _logLines;

        // Start robot->PC listener
        _robot.StartLogListener(RobotLogPort);

        // Receive lines from robot
        _robot.RobotLog += line =>
            Dispatcher.UIThread.Post(() =>
            {
                // Some scripts may include "\n" as text inside a single line:
                foreach (var part in line.Split(new[] { "\n", "\r\n", "\\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    // Trim and ignore empty lines
                    var trimmed = part.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                        continue;
                    // Add to log view
                    _logLines.Add(trimmed);
                    if (_logLines.Count > 500) _logLines.RemoveAt(0);
                    LogList.ScrollIntoView(trimmed);

                    // Capture counts from log
                    TryHandleEyesLocateCount(trimmed); // <-- preferred
                    TryHandleEyesWorkpCount(trimmed);  // <-- fallback

                    // Auto DB update on "STEP Place DONE color=..."
                    TryHandlePlaceDone(trimmed);

                    // Queue logic on "RUN <name> END"
                    TryHandleRunEnd(trimmed);
                }
            });

        _ = InitializeAsync();
    }
    
    private async Task InitializeAsync()
    {
        try
        {
            // Initialize DB
            await DatabaseService.InitializeAsync();
            // Load initial counts
            await RefreshCountsAsync();
            // Ready
            StatusText.Text = "Ready.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"DB init failed: {ex.Message}";
        }
    }
    // ---- REFRESH COUNTS ----
    private async Task RefreshCountsAsync()
    {
        // Get counts from DB
        var counts = await _inventory.GetCountsAsync();
        RedCountText.Text = counts[ComponentColor.Red].ToString();
        GreenCountText.Text = counts[ComponentColor.Green].ToString();
        BlueCountText.Text = counts[ComponentColor.Blue].ToString();
    }
    // ---- ADJUST COUNT ----
    private async Task AdjustAsync(ComponentColor color, int delta)
    {
        // Update DB
        try
        {
            await DatabaseService.InitializeAsync();
            // Change count
            var newValue = await _inventory.ChangeCountAsync(color, delta);

            // Update UI
            switch (color)
            {
                // Update relevant text box
                case ComponentColor.Red: RedCountText.Text = newValue.ToString(); break;
                case ComponentColor.Green: GreenCountText.Text = newValue.ToString(); break;
                case ComponentColor.Blue: BlueCountText.Text = newValue.ToString(); break;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"DB error: {ex.Message}";
        }
    }

    // Parse: "STEP EyesLocate DONE cnt=2"
    private void TryHandleEyesLocateCount(string line)
    {
        // Preferred count source
        const string prefix = "STEP EyesLocate DONE cnt=";
        // Check prefix
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return;
        // Extract number
        var s = line.Substring(prefix.Length).Trim();


        // Clamp values
        if (int.TryParse(s, out var n))
        {
            if (n <= 0) n = 1;
            if (n > 200) n = 200;
            _lastEyesLocateCount = n;
        }
    }

    // Parse: "DATA EyesWorkpCount=12" (fallback)
    private void TryHandleEyesWorkpCount(string line)
    {
        const string prefix = "DATA EyesWorkpCount=";

        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return;

        var s = line.Substring(prefix.Length).Trim();

        if (int.TryParse(s, out var n))
        {
            if (n <= 0) n = 1;
            if (n > 200) n = 200;
            _lastEyesWorkpCount = n;
        }
    }

    // ---- AUTO COUNT ----
    private void TryHandlePlaceDone(string line)
    {
        // Example: "STEP Place DONE color=RED"
        const string prefix = "STEP Place DONE color=";
        

        if (line == _lastPlaceLine) return;
        // Avoid double-counting same line
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;

        // Extract color
        var colorText = line.Substring(prefix.Length).Trim().ToUpperInvariant();
        if (!TryParseColor(colorText, out var color)) return;

        // Remember last processed line
        _lastPlaceLine = line;

        // Prefer EyesLocate count. Fallback to EyesWorkpCount.
        var delta = _lastEyesLocateCount > 0 ? _lastEyesLocateCount : _lastEyesWorkpCount;

        // Reset after consuming so next cycle doesn't inherit the old number
        _lastEyesLocateCount = 1;
        _lastEyesWorkpCount = 1;

        _ = Task.Run(async () =>
        {
            // Update DB
            try
            {
                await DatabaseService.InitializeAsync();
                // Change count
                var newValue = await _inventory.ChangeCountAsync(color, delta);


                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Update relevant text box
                    switch (color)
                    {
                        case ComponentColor.Red: RedCountText.Text = newValue.ToString(); break;
                        case ComponentColor.Green: GreenCountText.Text = newValue.ToString(); break;
                        case ComponentColor.Blue: BlueCountText.Text = newValue.ToString(); break;
                    }
                });
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusText.Text = $"Auto count failed: {ex.Message}";
                });
            }
        });
    }

    private static bool TryParseColor(string colorText, out ComponentColor color)
    {
        
        color = ComponentColor.Blue;

        switch (colorText)
        {
            case "RED":
                color = ComponentColor.Red;
                return true;
            case "GREEN":
                color = ComponentColor.Green;
                return true;
            case "BLUE":
                color = ComponentColor.Blue;
                return true;
            default:
                return false;
        }
    }

    // ---- SORT BUTTONS ----
    private async void SortBlue_Click(object? sender, RoutedEventArgs e)
    {
        StatusText.Text = "Sending: sort blue...";
        await _robot.SendScriptAsync("blaa_26.script");
        StatusText.Text = "Command sent.";
    }

    private async void SortGreen_Click(object? sender, RoutedEventArgs e)
    {
        StatusText.Text = "Sending: sort green...";
        await _robot.SendScriptAsync("groen_26.script");
        StatusText.Text = "Command sent.";
    }

    private async void SortRed_Click(object? sender, RoutedEventArgs e)
    {
        StatusText.Text = "Sending: sort red...";
        await _robot.SendScriptAsync("roed_26.script");
        StatusText.Text = "Command sent.";
    }

    // ---- MANUAL INVENTORY BUTTONS ----
    private async void IncRed_Click(object? sender, RoutedEventArgs e) => await AdjustAsync(ComponentColor.Red, +1);
    private async void DecRed_Click(object? sender, RoutedEventArgs e) => await AdjustAsync(ComponentColor.Red, -1);

    private async void IncGreen_Click(object? sender, RoutedEventArgs e) => await AdjustAsync(ComponentColor.Green, +1);
    private async void DecGreen_Click(object? sender, RoutedEventArgs e) => await AdjustAsync(ComponentColor.Green, -1);

    private async void IncBlue_Click(object? sender, RoutedEventArgs e) => await AdjustAsync(ComponentColor.Blue, +1);
    private async void DecBlue_Click(object? sender, RoutedEventArgs e) => await AdjustAsync(ComponentColor.Blue, -1);

    // ---- SORT ALL QUEUE ----
    private async void SortAll_Click(object? sender, RoutedEventArgs e)
    {
        if (_queueRunning)
        {
            StatusText.Text = "Sort All already running.";
            return;
        }

        _queueRunning = true;
        _queueIndex = 0;

        StatusText.Text = "Sort All started (Blue → Green → Red)";
        await SendCurrentQueueScriptAsync();
    }

    private void CancelQueue_Click(object? sender, RoutedEventArgs e)
    {
        _queueRunning = false;
        StatusText.Text = "Sort All cancelled.";
    }

    private async Task SendCurrentQueueScriptAsync()
    {
        if (!_queueRunning) return;

        if (_queueIndex >= _queue.Length)
        {
            _queueRunning = false;
            StatusText.Text = "Sort All finished.";
            return;
        }

        var (script, runName) = _queue[_queueIndex];

        try
        {
            StatusText.Text = $"Sort All: sending {runName}...";
            await _robot.SendScriptAsync(script);
            StatusText.Text = $"Sort All: {runName} sent – waiting for END.";
        }
        catch (Exception ex)
        {
            _queueRunning = false;
            StatusText.Text = $"Sort All failed: {ex.Message}";
            _logLines.Add($"QUEUE ERROR: {ex}");
            LogList.ScrollIntoView(_logLines[^1]);
        }
    }

    private static readonly Regex RunEndRegex =
        new(@"^\s*RUN\s+(?<name>.+?)\s+END\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private void TryHandleRunEnd(string line)
    {
        if (!_queueRunning)
            return;

        line = line.Replace("\\n", "").Trim();

        var m = RunEndRegex.Match(line);
        if (!m.Success)
            return;

        var runName = m.Groups["name"].Value.Trim();

        if (_queueIndex < 0 || _queueIndex >= _queue.Length)
            return;

        var expected = _queue[_queueIndex].RunName;

        if (!runName.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            _logLines.Add($"QUEUE: END ignored (got '{runName}', expected '{expected}')");
            LogList.ScrollIntoView(_logLines[^1]);
            return;
        }

        _queueIndex++;

        if (_queueIndex >= _queue.Length)
        {
            _queueRunning = false;
            StatusText.Text = "Sort All finished.";
            return;
        }

        _logLines.Add($"QUEUE: {runName} END detected → sending next");
        LogList.ScrollIntoView(_logLines[^1]);

        // Run on UI thread (SendCurrentQueueScriptAsync updates StatusText)
        _ = Dispatcher.UIThread.InvokeAsync(async () => await SendCurrentQueueScriptAsync());
    }
}
