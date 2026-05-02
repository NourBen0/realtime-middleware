using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RealtimeMiddleware.Domain.Entities;
using RealtimeMiddleware.Domain.Enums;
using RealtimeMiddleware.Domain.Interfaces;

namespace RealtimeMiddleware.Infrastructure.MessageBus;

/// <summary>
/// Priority-based in-memory message bus. Messages with higher priority are dequeued first.
/// Thread-safe implementation using PriorityQueue + SemaphoreSlim.
/// </summary>
public class PriorityMessageBus : IMessageBus, IDisposable
{
    // PriorityQueue uses lowest int = highest priority, so we negate the enum value
    private readonly PriorityQueue<Message, int> _queue = new();
    private readonly ConcurrentDictionary<string, List<Func<Message, Task>>> _subscriptions = new();
    private readonly SemaphoreSlim _semaphore = new(0);
    private readonly ILogger<PriorityMessageBus> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processorTask;
    private readonly object _lock = new();

    public PriorityMessageBus(ILogger<PriorityMessageBus> logger)
    {
        _logger = logger;
        _processorTask = Task.Run(ProcessQueueAsync);
    }

    public Task PublishAsync(Message message, CancellationToken ct = default)
    {
        lock (_lock)
        {
            // Negate so Critical(3) → -3 = highest priority in PriorityQueue
            _queue.Enqueue(message, -(int)message.Priority);
        }

        _semaphore.Release();
        _logger.LogDebug("Message {Id} enqueued on topic '{Topic}' with priority {Priority}",
            message.Id, message.Topic, message.Priority);

        return Task.CompletedTask;
    }

    public Task SubscribeAsync(string topic, Func<Message, Task> handler, CancellationToken ct = default)
    {
        _subscriptions.AddOrUpdate(
            topic,
            _ => [handler],
            (_, existing) => { existing.Add(handler); return existing; }
        );

        _logger.LogInformation("New subscription registered for topic '{Topic}'", topic);
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(string topic)
    {
        _subscriptions.TryRemove(topic, out _);
        _logger.LogInformation("Subscription removed for topic '{Topic}'", topic);
        return Task.CompletedTask;
    }

    private async Task ProcessQueueAsync()
    {
        _logger.LogInformation("Message bus processor started");

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await _semaphore.WaitAsync(_cts.Token);

                Message? message;
                lock (_lock)
                {
                    _queue.TryDequeue(out message, out _);
                }

                if (message == null) continue;

                await DispatchMessageAsync(message);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in message bus processor");
            }
        }

        _logger.LogInformation("Message bus processor stopped");
    }

    private async Task DispatchMessageAsync(Message message)
    {
        // Try exact topic match, then wildcard "*"
        var topics = new[] { message.Topic, "*" };

        foreach (var topic in topics)
        {
            if (!_subscriptions.TryGetValue(topic, out var handlers)) continue;

            foreach (var handler in handlers.ToList())
            {
                try
                {
                    await handler(message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Handler error for topic '{Topic}', message {Id}",
                        topic, message.Id);
                }
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _processorTask.Wait(TimeSpan.FromSeconds(5));
        _semaphore.Dispose();
        _cts.Dispose();
    }
}
