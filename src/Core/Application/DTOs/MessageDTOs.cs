using RealtimeMiddleware.Domain.Enums;

namespace RealtimeMiddleware.Application.DTOs;

public record PublishMessageRequest(
    string Topic,
    string Payload,
    MessagePriority Priority = MessagePriority.Normal,
    string Source = "api",
    string? Target = null
);

public record MessageResponse(
    Guid Id,
    string Topic,
    string Payload,
    MessagePriority Priority,
    string Source,
    string? Target,
    DateTime CreatedAt,
    int RetryCount,
    MessageStatus Status,
    string? ErrorMessage
);

public record WebSocketMessage(
    string Type,
    string Topic,
    string Payload,
    MessagePriority Priority,
    string Source,
    string? Target,
    DateTime Timestamp
);

public record ApiResponse<T>(bool Success, T? Data, string? Error = null);

public record StatsResponse(
    int TotalMessages,
    int PendingMessages,
    int ProcessedMessages,
    int FailedMessages,
    int ConnectedClients,
    DateTime ServerTime
);
