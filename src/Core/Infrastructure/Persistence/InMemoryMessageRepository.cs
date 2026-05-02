using System.Collections.Concurrent;
using RealtimeMiddleware.Domain.Entities;
using RealtimeMiddleware.Domain.Enums;
using RealtimeMiddleware.Domain.Interfaces;

namespace RealtimeMiddleware.Infrastructure.Persistence;

/// <summary>
/// In-memory repository for development/testing.
/// Replace with EF Core / Redis for production.
/// </summary>
public class InMemoryMessageRepository : IMessageRepository
{
    private readonly ConcurrentDictionary<Guid, Message> _store = new();

    public Task<Message?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_store.GetValueOrDefault(id));

    public Task<IEnumerable<Message>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult<IEnumerable<Message>>(_store.Values.OrderByDescending(m => m.CreatedAt).ToList());

    public Task<IEnumerable<Message>> GetByTopicAsync(string topic, CancellationToken ct = default)
        => Task.FromResult<IEnumerable<Message>>(
            _store.Values.Where(m => m.Topic == topic).OrderByDescending(m => m.CreatedAt).ToList());

    public Task<IEnumerable<Message>> GetFailedAsync(CancellationToken ct = default)
        => Task.FromResult<IEnumerable<Message>>(
            _store.Values.Where(m => m.Status == MessageStatus.Failed).ToList());

    public Task AddAsync(Message message, CancellationToken ct = default)
    {
        _store[message.Id] = message;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Message message, CancellationToken ct = default)
    {
        _store[message.Id] = message;
        return Task.CompletedTask;
    }

    public Task<int> GetCountAsync(CancellationToken ct = default)
        => Task.FromResult(_store.Count);
}
