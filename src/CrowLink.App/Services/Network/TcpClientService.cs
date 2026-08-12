using System.Net;
using System.Net.Sockets;

namespace CrowLink.Services.Network;

public sealed class TcpClientService
{
    public async Task<TcpClient> ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken)
    {
        var client = new TcpClient(address.AddressFamily) { NoDelay = true };
        try
        {
            await client.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
