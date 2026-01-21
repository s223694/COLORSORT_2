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
    // Service der kan sende URScript til robotten og modtage robot-log via TCP.
    private readonly RobotService _robot = new();

    // Repository til SQLite inventory-tabellen (Red/Green/Blue counts).
    private readonly InventoryRepository _inventory = new();

    // Log-linjer som UI viser i en liste (ObservableCollection opdaterer UI automatisk).
    private readonly ObservableCollection<string> _logLines = new();

    // PC-port hvor vi lytter efter log fra robotten.
    // VIGTIGT: Denne port skal matche det robot-script bruger, når det "socket_open" tilbage til GUI.
    private const int RobotLogPort = 45123; // MUST match URScript gui_port

    // Bruges til at undgå dobbelt DB-opdatering hvis den samme "Place DONE" linje kommer igen.
    // (Robot kan nogle gange sende samme linje flere gange).
    private string? _lastPlaceLine;

    // 1) Når vi ser en log-linje: "STEP EyesLocate DONE cnt=<n>"
    //    så gemmer vi <n> i _lastEyesLocateCount.
    //
    // 2) Hvis vi i stedet ser en fallback linje: "DATA EyesWorkpCount=<n>"
    //    så gemmer vi den i _lastEyesWorkpCount.
    //
    // 3) Når robotten senere melder: "STEP Place DONE color=<COLOR>"
    //    så bruger vi den senest gemte count som "delta" i inventory.
    //
    // 4) Efter vi har brugt count'en til DB-opdatering, resetter vi counts til 1,
    //    så næste cyklus ikke "arver" et gammelt tal ved en fejl.
    private int _lastEyesLocateCount = 1;

    // Fallback count source (hvis cnt ikke sendes)
    private int _lastEyesWorkpCount = 1;

   
    // Flag der betyder om vi er midt i en "Sort All" sekvens.
    private bool _queueRunning = false;

    // Index i køen (hvilket script vi er nået til).
    private int _queueIndex = 0;

    // NOTE: RunName skal matche jeres URScript output format: "RUN <name> END"
    // Når vi sender et script, venter vi på at robotten sender "RUN <name> END" før vi sender næste.
    private readonly (string Script, string RunName)[] _queue =
    {
        ("blaa_26.script",  "blaa_26"),
        ("groen_26.script", "groen_26"),
        ("roed_26.script",  "roed_26"),
    };

    public ControlView()
    {
        InitializeComponent();

        // Binder log-listen (fx ListBox) til vores ObservableCollection.
        LogList.ItemsSource = _logLines;

        // Start PC-server, så robotten kan sende log-linjer til GUI'en.
        _robot.StartLogListener(RobotLogPort);

        // Abonner på log event. RobotService kalder denne callback fra baggrundstråde,
        // så UI-opdateringer skal ind på UI-tråden -> Dispatcher.UIThread.Post(...)
        _robot.RobotLog += line =>
            Dispatcher.UIThread.Post(() =>
            {
                // Nogle scripts kan sende "\n" inde i teksten.
                // Derfor splitter vi både på rigtige newlines og tekst-escapes ("\\n").
                foreach (var part in line.Split(new[] { "\n", "\r\n", "\\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    // Trim whitespace
                    var trimmed = part.Trim();

                    // Ignorer tomme linjer
                    if (string.IsNullOrEmpty(trimmed))
                        continue;

                    // Tilføj linjen til log-viewet i UI
                    _logLines.Add(trimmed);

                    // Hold log-listen bounded (max 500 linjer) så UI ikke bliver tungt.
                    if (_logLines.Count > 500) _logLines.RemoveAt(0);

                    // Auto-scroll så den nyeste linje er synlig.
                    LogList.ScrollIntoView(trimmed);

                    // ----------------- EYES COUNT OPSAMLING -----------------
                    // Disse metoder kigger efter specifikke loglinjer og gemmer count i variabler.
                    // De ændrer IKKE DB direkte - de gemmer kun "seneste count" til senere.
                    TryHandleEyesLocateCount(trimmed); // <-- foretrukket kilde
                    TryHandleEyesWorkpCount(trimmed);  // <-- fallback kilde

                    // ----------------- AUTO DB UPDATE -----------------
                    // Når robotten har placeret emnerne og melder det er DONE,
                    // så bruger vi den senest opsamlede EyesCount til at opdatere inventory.
                    TryHandlePlaceDone(trimmed);

                    // ----------------- SORT ALL KØ -----------------
                    // Hvis Sort All kører, så lytter vi efter "RUN <name> END" for at vide hvornår vi skal sende næste.
                    TryHandleRunEnd(trimmed);
                }
            });

        // Kør init (DB + counts) uden at blokere UI.
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            // Opret tabeller + seed hvis DB ikke findes endnu.
            await DatabaseService.InitializeAsync();

            // Læs inventory counts og vis dem på UI
            await RefreshCountsAsync();

            StatusText.Text = "Ready.";
        }
        catch (Exception ex)
        {
            // Hvis DB init fejler, viser vi det til brugeren.
            StatusText.Text = $"DB init failed: {ex.Message}";
        }
    }

    // ---- REFRESH COUNTS ----
    // Læser counts fra DB og opdaterer UI-tekster.
    private async Task RefreshCountsAsync()
    {
        var counts = await _inventory.GetCountsAsync();
        RedCountText.Text = counts[ComponentColor.Red].ToString();
        GreenCountText.Text = counts[ComponentColor.Green].ToString();
        BlueCountText.Text = counts[ComponentColor.Blue].ToString();
    }

    // ---- ADJUST COUNT (MANUELT) ----
    // Bruges af +/- knapperne til manuelt at justere counts i DB.
    private async Task AdjustAsync(ComponentColor color, int delta)
    {
        try
        {
            // (Kan diskuteres om dette er nødvendigt hver gang, men det sikrer at DB er init.)
            await DatabaseService.InitializeAsync();

            // Opdater count for valgt farve med delta (+1 / -1)
            var newValue = await _inventory.ChangeCountAsync(color, delta);

            // Opdater UI kun for den relevante farve
            switch (color)
            {
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

    // ----------------- EYES COUNT OPSAMLING (FORETRUKKET) -----------------
    // Parse eksempel: "STEP EyesLocate DONE cnt=2"
    //
    // Når robotten afslutter EyesLocate-step, sender den hvor mange emner den fandt.
    // Vi gemmer n i _lastEyesLocateCount, så det kan bruges senere når Place DONE kommer.
    private void TryHandleEyesLocateCount(string line)
    {
        const string prefix = "STEP EyesLocate DONE cnt=";

        // Hvis linjen ikke starter med det forventede prefix, gør vi ingenting.
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return;

        // Alt efter prefix forventes at være tallet (cnt)
        var s = line.Substring(prefix.Length).Trim();

        // Parse tallet og clamp det til fornuftige grænser (sikkerhed mod fejl i log)
        if (int.TryParse(s, out var n))
        {
            if (n <= 0) n = 1;   // vi tillader ikke 0/negativt, da det ville "fjerne" inventory eller give mærkelig logik
            if (n > 200) n = 200; // sikkerhed mod ekstremt store tal

            // GEM count’en:
            // Denne variabel repræsenterer nu "senest kendte" antal fundet komponenter i den aktuelle cyklus.
            _lastEyesLocateCount = n;
        }
    }

    // ----------------- EYES COUNT OPSAMLING (FALLBACK) -----------------
    // Parse eksempel: "DATA EyesWorkpCount=12"
    //
    // Hvis scripts ikke sender "EyesLocate DONE cnt=...",
    // kan vi i stedet bruge en anden datapunkt-linje som fallback.
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

            // GEM fallback count’en (bruges hvis EyesLocate count ikke er tilgængelig)
            _lastEyesWorkpCount = n;
        }
    }

    // ----------------- AUTO INVENTORY UPDATE -----------------
    // Trigger eksempel: "STEP Place DONE color=RED"
    //
    // Når Place-step er færdig, betyder det: robotten har placeret komponenterne i en bestemt farve-bakke.
    // På det tidspunkt opdaterer vi inventory i DB med "hvor mange" der blev placeret.
    //
    // Hvor mange = den count vi tidligere opsamlede fra EyesLocate/EyesWorkp.
    private void TryHandlePlaceDone(string line)
    {
        const string prefix = "STEP Place DONE color=";

        // Anti double-count: hvis vi har behandlet præcis samme linje før, så returnér.
        if (line == _lastPlaceLine) return;

        // Hvis det ikke er en Place DONE linje, så gør vi intet.
        if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;

        // Udtræk farvetekst efter prefix (fx "RED")
        var colorText = line.Substring(prefix.Length).Trim().ToUpperInvariant();

        // Konverter tekst -> enum
        if (!TryParseColor(colorText, out var color)) return;

        // Husk linjen så vi ikke tæller den samme igen
        _lastPlaceLine = line;

        // Beregn delta:
        // - Vi "foretrækker" EyesLocate count, da den er tættest på vision-resultatet for cyklussen
        // - Hvis den ikke var tilgængelig, bruger vi fallback EyesWorkpCount
        var delta = _lastEyesLocateCount > 0 ? _lastEyesLocateCount : _lastEyesWorkpCount;

        // VIGTIGT: Reset counts efter vi har "forbrugt" dem,
        // så næste Place DONE ikke bruger et gammelt tal ved en fejl.
        _lastEyesLocateCount = 1;
        _lastEyesWorkpCount = 1;

        // Kør DB-opdatering i baggrunden, så UI ikke fryser.
        _ = Task.Run(async () =>
        {
            try
            {
                // Sikrer DB/tabeller eksisterer
                await DatabaseService.InitializeAsync();

                // Opdater count i DB med delta
                var newValue = await _inventory.ChangeCountAsync(color, delta);

                // UI opdatering skal ske på UI-tråden
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
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

    // Konverterer farvestreng fra robotlog ("RED"/"GREEN"/"BLUE") til enum
    private static bool TryParseColor(string colorText, out ComponentColor color)
    {
        color = ComponentColor.Blue; // default (skal overskrives ved success)

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
    // Sender et specifikt script til robotten (manual sort per farve)
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

    
    private async void IncRed_Click(object? sender, RoutedEventArgs e) => await AdjustAsync(ComponentColor.Red, +1);
    private async void DecRed_Click(object? sender, RoutedEventArgs e) => await AdjustAsync(ComponentColor.Red, -1);

    private async void IncGreen_Click(object? sender, RoutedEventArgs e) => await AdjustAsync(ComponentColor.Green, +1);
    private async void DecGreen_Click(object? sender, RoutedEventArgs e) => await AdjustAsync(ComponentColor.Green, -1);

    private async void IncBlue_Click(object? sender, RoutedEventArgs e) => await AdjustAsync(ComponentColor.Blue, +1);
    private async void DecBlue_Click(object? sender, RoutedEventArgs e) => await AdjustAsync(ComponentColor.Blue, -1);

   
    // Starter en sekvens som sender scripts i rækkefølge: Blue -> Green -> Red
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

    // Stopper kun kølogikken i GUI (robotten kan stadig være i gang med det script der allerede er sendt).
    private void CancelQueue_Click(object? sender, RoutedEventArgs e)
    {
        _queueRunning = false;
        StatusText.Text = "Sort All cancelled.";
    }

    // Sender det script som køen peger på lige nu.
    private async Task SendCurrentQueueScriptAsync()
    {
        if (!_queueRunning) return;

        // Hvis vi er færdige, stop køen.
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

    // Regex til at finde: "RUN <name> END"
    private static readonly Regex RunEndRegex =
        new(@"^\s*RUN\s+(?<name>.+?)\s+END\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Kører når der kommer log-linjer, og Sort All er aktiv.
    // Hvis vi ser END for det forventede runName, sender vi næste script.
    private void TryHandleRunEnd(string line)
    {
        if (!_queueRunning)
            return;

        // Rens typiske escape-ting ud
        line = line.Replace("\\n", "").Trim();

        var m = RunEndRegex.Match(line);
        if (!m.Success)
            return;

        var runName = m.Groups["name"].Value.Trim();

        // Sikkerhed: index skal være gyldigt
        if (_queueIndex < 0 || _queueIndex >= _queue.Length)
            return;

        var expected = _queue[_queueIndex].RunName;

        // Hvis vi får END for et andet script end det vi venter på, ignorer vi det.
        if (!runName.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            _logLines.Add($"QUEUE: END ignored (got '{runName}', expected '{expected}')");
            LogList.ScrollIntoView(_logLines[^1]);
            return;
        }

        // END for forventet script er modtaget -> næste i køen
        _queueIndex++;

        if (_queueIndex >= _queue.Length)
        {
            _queueRunning = false;
            StatusText.Text = "Sort All finished.";
            return;
        }

        _logLines.Add($"QUEUE: {runName} END detected → sending next");
        LogList.ScrollIntoView(_logLines[^1]);

        // Kør på UI-tråden (SendCurrentQueueScriptAsync opdaterer StatusText)
        _ = Dispatcher.UIThread.InvokeAsync(async () => await SendCurrentQueueScriptAsync());
    }
}
