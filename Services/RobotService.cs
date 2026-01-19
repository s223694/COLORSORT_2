using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ColorSorterGUI.Services;
// Service to communicate with the robot via TCP/IP
public class RobotService
{
    private const string RobotIp = "172.20.254.208";
    private const int UrscriptPort = 30002;

    public event Action<string>? RobotLog;

    private TcpListener? _logListener;
    private CancellationTokenSource? _logCts;

    private void EmitLog(string line)
    {
        if (!string.IsNullOrWhiteSpace(line))
            RobotLog?.Invoke(line);
    }


    public void StartLogListener(int port)
    {
        if (_logListener != null) return;

        try
        {
            _logCts = new CancellationTokenSource();
            _logListener = new TcpListener(IPAddress.Any, port);
            _logListener.Start();
        }
        catch (SocketException ex)
        {
            EmitLog($"Log listener FAILED on port {port}: {ex.Message}");
            _logListener = null;
            _logCts = null;
            return;
        }

        EmitLog($"Log listener started on port {port}");

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_logCts!.IsCancellationRequested)
                {
                    var client = await _logListener.AcceptTcpClientAsync(_logCts.Token);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var c = client;
                            using var stream = c.GetStream();
                            using var reader = new StreamReader(stream, Encoding.UTF8);

                            string? line;
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

    public async Task SendScriptAsync(string scriptPath)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(RobotIp, UrscriptPort);

        using var stream = client.GetStream();
        var script = await File.ReadAllTextAsync(scriptPath);

        var bytes = Encoding.ASCII.GetBytes(script + "\n");
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();

        await Task.Delay(200);
    }
}
