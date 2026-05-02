using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RealtimeMiddleware.Domain.Entities;
using RealtimeMiddleware.Domain.Enums;
using RealtimeMiddleware.Infrastructure.MessageBus;

namespace RealtimeMiddleware.Tests.Unit;

[TestFixture]
public class PriorityMessageBusTests
{
    private PriorityMessageBus _bus = null!;

    [SetUp]
    public void SetUp()
    {
        _bus = new PriorityMessageBus(NullLogger<PriorityMessageBus>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _bus.Dispose();
    }

    [Test]
    public async Task Publish_And_Subscribe_MessageIsReceived()
    {
        // Arrange
        var received = new TaskCompletionSource<Message>();
        await _bus.SubscribeAsync("test.topic", msg =>
        {
            received.TrySetResult(msg);
            return Task.CompletedTask;
        });

        var message = new Message { Topic = "test.topic", Payload = "hello", Priority = MessagePriority.Normal };

        // Act
        await _bus.PublishAsync(message);

        // Assert
        var result = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(result.Id, Is.EqualTo(message.Id));
        Assert.That(result.Payload, Is.EqualTo("hello"));
    }

    [Test]
    public async Task CriticalMessages_ProcessedBeforeNormal()
    {
        // Arrange
        var received = new List<MessagePriority>();
        var tcs = new TaskCompletionSource();
        int count = 0;

        await _bus.SubscribeAsync("priority.test", msg =>
        {
            lock (received) received.Add(msg.Priority);
            if (Interlocked.Increment(ref count) == 3) tcs.TrySetResult();
            return Task.CompletedTask;
        });

        // Act - publish in reverse priority order
        await _bus.PublishAsync(new Message { Topic = "priority.test", Priority = MessagePriority.Low, Payload = "low" });
        await _bus.PublishAsync(new Message { Topic = "priority.test", Priority = MessagePriority.Normal, Payload = "normal" });
        await _bus.PublishAsync(new Message { Topic = "priority.test", Priority = MessagePriority.Critical, Payload = "critical" });

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The first message processed should be the Critical one
        Assert.That(received[0], Is.EqualTo(MessagePriority.Critical));
    }

    [Test]
    public async Task Wildcard_Subscription_ReceivesAllMessages()
    {
        // Arrange
        var received = new List<Message>();
        await _bus.SubscribeAsync("*", msg =>
        {
            lock (received) received.Add(msg);
            return Task.CompletedTask;
        });

        // Act
        await _bus.PublishAsync(new Message { Topic = "topic.a", Payload = "A" });
        await _bus.PublishAsync(new Message { Topic = "topic.b", Payload = "B" });
        await Task.Delay(500); // Give processor time

        // Assert
        Assert.That(received.Count, Is.EqualTo(2));
    }

    [Test]
    public async Task Unsubscribe_StopsReceivingMessages()
    {
        // Arrange
        int callCount = 0;
        await _bus.SubscribeAsync("unsub.test", _ =>
        {
            Interlocked.Increment(ref callCount);
            return Task.CompletedTask;
        });

        await _bus.UnsubscribeAsync("unsub.test");
        await _bus.PublishAsync(new Message { Topic = "unsub.test", Payload = "should not be received" });
        await Task.Delay(300);

        Assert.That(callCount, Is.EqualTo(0));
    }
}
