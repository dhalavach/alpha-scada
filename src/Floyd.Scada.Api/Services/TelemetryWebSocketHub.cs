using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Floyd.Scada.Api.Models;

namespace Floyd.Scada.Api.Services;

public sealed class TelemetryWebSocketHub : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _clientsLock = new(1, 1);
    private readonly List<WebSocket> _clients = [];
    private readonly ScadaStore _store;

    public TelemetryWebSocketHub(ScadaStore store)
    {
        _store = store;
        _store.SamplesChanged += BroadcastAsync;
    }

    public async Task AttachAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        await _clientsLock.WaitAsync(cancellationToken);
        try
        {
            _clients.Add(socket);
        }
        finally
        {
            _clientsLock.Release();
        }

        try
        {
            await SendAsync(socket, new
            {
                type = "snapshot",
                tags = _store.GetCurrentTags()
            }, cancellationToken);

            var buffer = new byte[512];
            while (!cancellationToken.IsCancellationRequested &&
                   socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The app is shutting down or the browser disconnected.
        }
        catch (WebSocketException)
        {
            // Browser disconnects can abort the socket before close negotiation finishes.
        }
        finally
        {
            await RemoveAsync(socket);
        }
    }

    public void Dispose()
    {
        _store.SamplesChanged -= BroadcastAsync;
        _clientsLock.Dispose();
    }

    private async Task BroadcastAsync(IReadOnlyCollection<TagSample> samples)
    {
        WebSocket[] clients;

        await _clientsLock.WaitAsync();
        try
        {
            clients = _clients
                .Where(client => client.State == WebSocketState.Open)
                .ToArray();
        }
        finally
        {
            _clientsLock.Release();
        }

        var payload = new
        {
            type = "samples",
            tags = samples
        };

        foreach (var client in clients)
        {
            await SendAsync(client, payload, CancellationToken.None);
        }
    }

    private async Task RemoveAsync(WebSocket socket)
    {
        await _clientsLock.WaitAsync();
        try
        {
            _clients.Remove(socket);
        }
        finally
        {
            _clientsLock.Release();
        }

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived or WebSocketState.CloseSent)
        {
            try
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // Socket was already aborted by the peer or host shutdown.
            }
        }
    }

    private static async Task SendAsync(WebSocket socket, object payload, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }
}
