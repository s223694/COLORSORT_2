using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace ColorSorterGUI.Services;

public class RobotService
{
    private const string RobotIp = "172.20.254.208";
    private const int UrscriptPort = 30002;

    public async Task SendScriptAsync(string scriptPath)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(RobotIp, UrscriptPort);

        using var stream = client.GetStream();
        var script = await File.ReadAllTextAsync(scriptPath);

        var bytes = Encoding.ASCII.GetBytes(script + "\n");
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();

        // Allow robot to parse and start program
        await Task.Delay(200);
    }
}
