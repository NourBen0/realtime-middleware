using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Logging;
using RealtimeMiddleware.Application.Interfaces;

namespace RealtimeMiddleware.Infrastructure.WebSocket;

public class WebSocketManager : IWebSocketManager
{
    private readonly ConcurrentDictionary<string, System.Net.WebSockets.WebSocket> _clients = new();
    private readonly ILogger<WebSocketManager> _logger;

    public WebSocketManager(ILogger<WebSocketManager> logger)
    {
        _logger = logger;
    }

    public async Task RegisterClientAsync(string clientId, System.Net.WebSockets.WebSocket socket, CancellationToken ct = default)
    {
        _clients[clientId] = socket;
        _logger.LogInformation("WebSocket client registered: {ClientId}. Total clients: {Count}",
            clientId, _clients.Count);

        // Send welcome message
        var welcome = $"{{\"type\":\"connected\",\"clientId\":\"{clientId}\",\"serverTime\":\"{DateTime.UtcNow:O}\"}}";
        await SendToClientAsync(clientId, welcome, ct);
    }

    public Task UnregisterClientAsync(string clientId)
    {
        _clients.TryRemove(clientId, out _);
        _logger.LogInformation("WebSocket client unregistered: {ClientId}. Total clients: {Count}",
            clientId, _clients.Count);
        return Task.CompletedTask;
    }

    public async Task BroadcastAsync(string message, CancellationToken ct = default)
    {
        var deadClients = new List<string>();
        var buffer = Encoding.UTF8.GetBytes(message);
        var segment = new ArraySegment<byte>(buffer);

        foreach (var (clientId, socket) in _clients)
        {
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.SendAsync(segment, WebSocketMessageType.Text, true, ct);
                else
                    deadClients.Add(clientId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send to client {ClientId}", clientId);
                deadClients.Add(clientId);
            }
        }

        foreach (var id in deadClients)
            await UnregisterClientAsync(id);
    }

    public async Task SendToClientAsync(string clientId, string message, CancellationToken ct = default)
    {
        if (!_clients.TryGetValue(clientId, out var socket))
        {
            _logger.LogWarning("Client {ClientId} not found", clientId);
            return;
        }

        if (socket.State != WebSocketState.Open)
        {
            await UnregisterClientAsync(clientId);
            return;
        }

        var buffer = Encoding.UTF8.GetBytes(message);
        await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, ct);
    }

    public int GetConnectedClientsCount() => _clients.Count;
}
