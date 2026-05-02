namespace RealtimeMiddleware.Domain.Entities;

public class Message
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Topic { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public MessagePriority Priority { get; init; } = MessagePriority.Normal;
    public string Source { get; init; } = string.Empty;
    public string? Target { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public int RetryCount { get; private set; } = 0;
    public MessageStatus Status { get; private set; } = MessageStatus.Pending;
    public string? ErrorMessage { get; private set; }

    public void MarkAsProcessed() => Status = MessageStatus.Processed;

    public void MarkAsFailed(string error)
    {
        Status = MessageStatus.Failed;
        ErrorMessage = error;
    }

    public void IncrementRetry()
    {
        RetryCount++;
        Status = MessageStatus.Pending;
        ErrorMessage = null;
    }

    public bool CanRetry(int maxRetries = 3) => RetryCount < maxRetries;
}
