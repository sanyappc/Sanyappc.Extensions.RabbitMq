using System.Net;
using System.Net.Sockets;

namespace Sanyappc.Extensions.RabbitMq.Tests;

// Relays AMQP between the client and the broker so a test can drop every live connection the way a network does:
// the client reads end of stream, exactly what production logged.
internal sealed class SeverableProxy : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly List<TcpClient> connections = [];
    private readonly string brokerHostname;
    private readonly int brokerPort;
    private readonly Task accepting;
    private volatile bool refusing;

    public SeverableProxy(string brokerHostname, int brokerPort)
    {
        this.brokerHostname = brokerHostname;
        this.brokerPort = brokerPort;
        listener.Start();
        accepting = AcceptAsync();
    }

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    private static async Task PumpAsync(NetworkStream from, NetworkStream to, CancellationToken cancellationToken)
    {
        try
        {
            await from.CopyToAsync(to, cancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(stopping.Token);
                if (refusing)
                {
                    client.Dispose();
                    continue;
                }

                _ = RelayAsync(client);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RelayAsync(TcpClient client)
    {
        TcpClient broker = new();
        lock (connections)
        {
            connections.Add(client);
            connections.Add(broker);
        }

        try
        {
            await broker.ConnectAsync(brokerHostname, brokerPort, stopping.Token);
            NetworkStream clientStream = client.GetStream();
            NetworkStream brokerStream = broker.GetStream();

            await Task.WhenAny(
                PumpAsync(clientStream, brokerStream, stopping.Token),
                PumpAsync(brokerStream, clientStream, stopping.Token));
        }
        catch (Exception exception) when (exception is OperationCanceledException or SocketException or IOException or ObjectDisposedException)
        {
        }
        finally
        {
            client.Dispose();
            broker.Dispose();
        }
    }

    public void Sever()
    {
        lock (connections)
        {
            foreach (TcpClient connection in connections)
                connection.Dispose();

            connections.Clear();
        }
    }

    public void Refuse() => refusing = true;

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        await accepting;
        listener.Dispose();
        Sever();
        stopping.Dispose();
    }
}
