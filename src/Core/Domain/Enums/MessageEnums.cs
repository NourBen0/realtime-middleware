namespace RealtimeMiddleware.Domain.Enums;

public enum MessagePriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public enum MessageStatus
{
    Pending,
    Processing,
    Processed,
    Failed,
    DeadLetter
}
