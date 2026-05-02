using RealtimeMiddleware.Domain.Entities;

namespace RealtimeMiddleware.Domain.Interfaces;

public interface IMessageRepository
{
    Task<Message?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<Message>> GetAllAsync(CancellationToken ct = default);
    Task<IEnumerable<Message>> GetByTopicAsync(string topic, CancellationToken ct = default);
    Task<IEnumerable<Message>> GetFailedAsync(CancellationToken ct = default);
    Task AddAsync(Message message, CancellationToken ct = default);
    Task UpdateAsync(Message message, CancellationToken ct = default);
    Task<int> GetCountAsync(CancellationToken ct = default);
}

public interface IMessageBus
{
    Task PublishAsync(Message message, CancellationToken ct = default);
    Task SubscribeAsync(string topic, Func<Message, Task> handler, CancellationToken ct = default);
    Task UnsubscribeAsync(string topic);
}
