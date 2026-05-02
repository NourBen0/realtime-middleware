using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using RealtimeMiddleware.Application.DTOs;
using RealtimeMiddleware.Application.Interfaces;
using RealtimeMiddleware.Domain.Enums;

namespace RealtimeMiddleware.Infrastructure.WebSocket;

public class WebSocketHandler
{
    private readonly IWebSocketManager _wsManager;
    private readonly IMessageService _messageService;
    private readonly ILogger<WebSocketHandler> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WebSocketHandler(
        IWebSocketManager wsManager,
        IMessageService messageService,
        ILogger<WebSocketHandler> logger)
    {
        _wsManager = wsManager;
        _messageService = messageService;
        _logger = logger;
    }

    public async Task HandleAsync(System.Net.WebSockets.WebSocket socket, string clientId, CancellationToken ct)
    {
        await _wsManager.RegisterClientAsync(clientId, socket, ct);
        var buffer = new byte[4096];

        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Client {ClientId} requested close", clientId);
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var raw = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await ProcessMessageAsync(raw, clientId, ct);
                }
            }
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogInformation("Client {ClientId} disconnected abruptly", clientId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling WebSocket for client {ClientId}", clientId);
        }
        finally
        {
            await _wsManager.UnregisterClientAsync(clientId);
        }
    }

    private async Task ProcessMessageAsync(string raw, string clientId, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Received from {ClientId}: {Message}", clientId, raw);

            var wsMsg = JsonSerializer.Deserialize<WebSocketMessage>(raw, JsonOptions);
            if (wsMsg == null)
            {
                await SendErrorAsync(clientId, "Invalid message format", ct);
                return;
            }

            switch (wsMsg.Type.ToLowerInvariant())
            {
                case "publish":
                    await _messageService.PublishAsync(new PublishMessageRequest(
                        wsMsg.Topic,
                        wsMsg.Payload,
                        wsMsg.Priority,
                        clientId,
                        wsMsg.Target
                    ), ct);
                    break;

                case "ping":
                    var pong = JsonSerializer.Serialize(new { type = "pong", timestamp = DateTime.UtcNow }, JsonOptions);
                    await _wsManager.SendToClientAsync(clientId, pong, ct);
                    break;

                default:
                    await SendErrorAsync(clientId, $"Unknown message type: {wsMsg.Type}", ct);
                    break;
            }
        }
        catch (JsonException)
        {
            await SendErrorAsync(clientId, "Malformed JSON", ct);
        }
    }

    private async Task SendErrorAsync(string clientId, string error, CancellationToken ct)
    {
        var msg = JsonSerializer.Serialize(new { type = "error", message = error }, JsonOptions);
        await _wsManager.SendToClientAsync(clientId, msg, ct);
    }
}
