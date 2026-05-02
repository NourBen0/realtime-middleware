using RealtimeMiddleware.Application.DTOs;

namespace RealtimeMiddleware.Application.Interfaces;

public interface IMessageService
{
    Task<MessageResponse> PublishAsync(PublishMessageRequest request, CancellationToken ct = default);
    Task<MessageResponse?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<MessageResponse>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<MessageResponse>> GetByTopicAsync(string topic, CancellationToken ct = default);
    Task<StatsResponse> GetStatsAsync(CancellationToken ct = default);
    Task RetryFailedAsync(CancellationToken ct = default);
}

public interface IWebSocketManager
{
    Task RegisterClientAsync(string clientId, System.Net.WebSockets.WebSocket socket, CancellationToken ct = default);
    Task UnregisterClientAsync(string clientId);
    Task BroadcastAsync(string message, CancellationToken ct = default);
    Task SendToClientAsync(string clientId, string message, CancellationToken ct = default);
    int GetConnectedClientsCount();
}

public interface IRetryService
{
    Task ScheduleRetryAsync(Guid messageId, CancellationToken ct = default);
    Task ProcessRetriesAsync(CancellationToken ct = default);
}
