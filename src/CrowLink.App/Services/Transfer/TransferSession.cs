using CrowLink.Models;

namespace CrowLink.Services.Transfer;

public sealed class TransferSession
{
    public TransferSession(TransferItem item, CancellationTokenSource cancellation)
    {
        Item = item;
        Cancellation = cancellation;
    }

    public TransferItem Item { get; }
    public CancellationTokenSource Cancellation { get; }
}
