using System.Threading.Channels;

namespace Sanyappc.Extensions.RabbitMq.Tests;

internal sealed class Inbox
{
    private readonly Channel<string> received = Channel.CreateUnbounded<string>();

    public void Deliver(string text) => received.Writer.TryWrite(text);

    // Skips everything else: a message whose ack was still in flight when the connection dropped is redelivered.
    public async Task WaitForAsync(string text, TimeSpan patience, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(patience);

        while (await received.Reader.ReadAsync(deadline.Token) != text)
        {
        }
    }
}
