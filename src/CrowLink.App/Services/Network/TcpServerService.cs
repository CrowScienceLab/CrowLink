using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using CrowLink.Services.Logging;

namespace CrowLink.Services.Network;

public sealed class TcpServerService : IAsyncDisposable
{
    private readonly int _port;
    private readonly LogService _log;
    private TcpListener? _listener;
    private CancellationTokenSource? _lifetimeCts;
    private Task? _acceptTask;
    private readonly ConcurrentDictionary<int, Task> _clientTasks = new();
    private int _nextClientId;

    public TcpServerService(int port, LogService log)
    {
        _port = port;
        _log = log;
    }

    public Task StartAsync(Func<TcpClient, CancellationToken, Task> clientHandler, CancellationToken cancellationToken = default)
    {
        if (_acceptTask is not null)
        {
            return Task.CompletedTask;
        }

        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _acceptTask = AcceptLoopAsync(clientHandler, _lifetimeCts.Token);
        return _log.InfoAsync($"TCP server started on port {_port}");
    }

    private async Task AcceptLoopAsync(Func<TcpClient, CancellationToken, Task> clientHandler, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                client.NoDelay = true;
                var clientId = Interlocked.Increment(ref _nextClientId);
                var task = HandleSafelyAsync(client, clientHandler, cancellationToken);
                _clientTasks[clientId] = task;
                _ = task.ContinueWith(
                    completedTask =>
                    {
                        _clientTasks.TryRemove(clientId, out var removedTask);
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                await _log.ErrorAsync("TCP accept failed", exception).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleSafelyAsync(TcpClient client, Func<TcpClient, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        try
        {
            await handler(client, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
        }
        catch (Exception exception)
        {
            await _log.ErrorAsync("Incoming connection failed", exception).ConfigureAwait(false);
            client.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetimeCts is not null)
        {
            await _lifetimeCts.CancelAsync().ConfigureAwait(false);
        }

        _listener?.Stop();
        if (_acceptTask is not null)
        {
            try
            {
                await _acceptTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await Task.WhenAll(_clientTasks.Values).ConfigureAwait(false);

        _lifetimeCts?.Dispose();
    }
}
