using Microsoft.Extensions.Logging;
using RealtimeMiddleware.Application.DTOs;
using RealtimeMiddleware.Application.Interfaces;
using RealtimeMiddleware.Domain.Entities;
using RealtimeMiddleware.Domain.Interfaces;

namespace RealtimeMiddleware.Application.Services;

public class MessageService : IMessageService
{
    private readonly IMessageRepository _repository;
    private readonly IMessageBus _messageBus;
    private readonly IWebSocketManager _wsManager;
    private readonly ILogger<MessageService> _logger;

    public MessageService(
        IMessageRepository repository,
        IMessageBus messageBus,
        IWebSocketManager wsManager,
        ILogger<MessageService> logger)
    {
        _repository = repository;
        _messageBus = messageBus;
        _wsManager = wsManager;
        _logger = logger;
    }

    public async Task<MessageResponse> PublishAsync(PublishMessageRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Publishing message to topic {Topic} with priority {Priority}",
            request.Topic, request.Priority);

        var message = new Message
        {
            Topic = request.Topic,
            Payload = request.Payload,
            Priority = request.Priority,
            Source = request.Source,
            Target = request.Target
        };

        await _repository.AddAsync(message, ct);

        // Publish to message bus (async fire-and-forget with error handling)
        _ = Task.Run(async () =>
        {
            try
            {
                await _messageBus.PublishAsync(message, ct);

                // Broadcast to WebSocket clients
                var wsMessage = System.Text.Json.JsonSerializer.Serialize(new WebSocketMessage(
                    "message",
                    message.Topic,
                    message.Payload,
                    message.Priority,
                    message.Source,
                    message.Target,
                    message.CreatedAt
                ));

                if (message.Target != null)
                    await _wsManager.SendToClientAsync(message.Target, wsMessage, ct);
                else
                    await _wsManager.BroadcastAsync(wsMessage, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error dispatching message {MessageId}", message.Id);
            }
        }, ct);

        return ToResponse(message);
    }

    public async Task<MessageResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var msg = await _repository.GetByIdAsync(id, ct);
        return msg == null ? null : ToResponse(msg);
    }

    public async Task<IEnumerable<MessageResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var messages = await _repository.GetAllAsync(ct);
        return messages.Select(ToResponse);
    }

    public async Task<IEnumerable<MessageResponse>> GetByTopicAsync(string topic, CancellationToken ct = default)
    {
        var messages = await _repository.GetByTopicAsync(topic, ct);
        return messages.Select(ToResponse);
    }

    public async Task<StatsResponse> GetStatsAsync(CancellationToken ct = default)
    {
        var all = (await _repository.GetAllAsync(ct)).ToList();
        return new StatsResponse(
            all.Count,
            all.Count(m => m.Status == Domain.Enums.MessageStatus.Pending),
            all.Count(m => m.Status == Domain.Enums.MessageStatus.Processed),
            all.Count(m => m.Status == Domain.Enums.MessageStatus.Failed),
            _wsManager.GetConnectedClientsCount(),
            DateTime.UtcNow
        );
    }

    public async Task RetryFailedAsync(CancellationToken ct = default)
    {
        var failed = (await _repository.GetFailedAsync(ct)).ToList();
        _logger.LogInformation("Retrying {Count} failed messages", failed.Count);

        foreach (var msg in failed.Where(m => m.CanRetry()))
        {
            msg.IncrementRetry();
            await _repository.UpdateAsync(msg, ct);
            await _messageBus.PublishAsync(msg, ct);
        }
    }

    private static MessageResponse ToResponse(Message m) => new(
        m.Id, m.Topic, m.Payload, m.Priority,
        m.Source, m.Target, m.CreatedAt, m.RetryCount, m.Status, m.ErrorMessage
    );
}
