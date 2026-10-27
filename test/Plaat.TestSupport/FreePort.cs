using System.Net;
using System.Net.Sockets;

namespace Plaat.TestSupport;

/// <summary>
/// Grabs a currently-free TCP port on loopback by briefly binding to port 0 and reading back what
/// the OS assigned. There is an inherent (tiny) race between releasing it here and the real server
/// binding it a moment later, but in practice this is the same technique used throughout the
/// .NET ecosystem for exactly this purpose and collisions are rare enough not to matter for tests.
/// </summary>
public static class FreePort
{
    public static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
