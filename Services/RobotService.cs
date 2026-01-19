using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Serviceklasse der håndterer TCP/IP kommunikation med robotten.
// - Sender URScript til robotten (port 30002)
// - Starter en lokal log-server så robotten kan sende status/log-linjer tilbage til GUI'en
namespace ColorSorterGUI.Services;

public class RobotService
{
    // Robotens IP-adresse på netværket (fast sat i koden).
    private const string RobotIp = "172.20.254.208";
    private const int UrscriptPort = 30002;

    
    // Event der kan abonnere UI på logbeskeder.
    // Fx: robotService.RobotLog += line => AppendToLog(line);
    public event Action<string>? RobotLog;
    
    // TcpListener: en lille server på PC'en som robotten kan forbinde til og sende log-linjer.
    private TcpListener? _logListener;
    
    // CancellationTokenSource bruges til at stoppe baggrundstråde/tasks pænt.
    private CancellationTokenSource? _logCts;
    
    // Sender en log-linje ud til alle subscribers (og filtrerer tomme linjer fra).
    private void EmitLog(string line)
    {
        if (!string.IsNullOrWhiteSpace(line))
            RobotLog?.Invoke(line);
    }

    // Starter log-serveren på en valgt port.
    // Robotten skal være sat op til at forbinde til PC'ens IP + den port for at kunne sende logs.
    public void StartLogListener(int port)
    {
        // Hvis vi allerede lytter, gør vi ingenting (forhindrer dobbelt-start).
        if (_logListener != null) return;

        try
        {
            // IPAddress.Any betyder: lyt på alle netkort/adaptere på PC'en.
            _logCts = new CancellationTokenSource();
            _logListener = new TcpListener(IPAddress.Any, port);
            _logListener.Start();
        }
        catch (SocketException ex)
        {
            // Hvis porten er optaget, eller der mangler rettigheder, så fejler det her.
            EmitLog($"Log listener FAILED on port {port}: {ex.Message}");
            _logListener = null;
            _logCts = null;
            return;
        }

        // Kører i baggrunden (fire-and-forget) og accepterer nye forbindelser.
        EmitLog($"Log listener started on port {port}");

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_logCts!.IsCancellationRequested)
                {
                    // Venter på at en klient forbinder (fx robotten).
                    var client = await _logListener.AcceptTcpClientAsync(_logCts.Token);
                    
                    // For hver forbindelse starter vi en separat task,
                    // så vi kan acceptere flere forbindelser over tid uden at blokere.
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            
                            using var c = client;
                            using var stream = c.GetStream();
                            // StreamReader læser tekst-linjer over TCP.
                            using var reader = new StreamReader(stream, Encoding.UTF8);
                            
                            string? line;
                            // Læs linje-for-linje indtil forbindelsen lukker.
                            while ((line = await reader.ReadLineAsync()) != null)
                            {
                                EmitLog(line);
                            }
                        }
                        catch (Exception ex)
                        {
                            EmitLog($"LOG ERROR: {ex.Message}");
                        }
                    }, _logCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // normal stop
            }
            catch (Exception ex)
            {
                EmitLog($"LISTENER ERROR: {ex.Message}");
            }
        }, _logCts.Token);
    }
    // Stopper log-listener og nulstiller felter.
    public void StopLogListener()
    {
        try
        {
            _logCts?.Cancel();
            _logListener?.Stop();
        }
        finally
        {
            _logListener = null;
            _logCts = null;
        }
    }
    // Sender et URScript til robotten.
    // scriptPath er typisk en fil som "groen_26.script", "roed_26.script", osv
    public async Task SendScriptAsync(string scriptPath)
    {   // Opretter forbindelse til robotten.
        using var client = new TcpClient();
        await client.ConnectAsync(RobotIp, UrscriptPort);
        
        using var stream = client.GetStream();
        // Læser hele script-filen ind som tekst.
        var script = await File.ReadAllTextAsync(scriptPath);
        
        // Robotten forventer typisk at script slutter med newline.
        var bytes = Encoding.ASCII.GetBytes(script + "\n");

        // Sender bytes til robotten.
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
        
        // Lille delay så forbindelsen ikke lukkes "for hurtigt"
        await Task.Delay(200);
    }
}
