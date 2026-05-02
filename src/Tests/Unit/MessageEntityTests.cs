using NUnit.Framework;
using RealtimeMiddleware.Domain.Entities;
using RealtimeMiddleware.Domain.Enums;

namespace RealtimeMiddleware.Tests.Unit;

[TestFixture]
public class MessageEntityTests
{
    [Test]
    public void NewMessage_HasPendingStatus()
    {
        var msg = new Message { Topic = "test", Payload = "data" };
        Assert.That(msg.Status, Is.EqualTo(MessageStatus.Pending));
        Assert.That(msg.RetryCount, Is.EqualTo(0));
    }

    [Test]
    public void MarkAsProcessed_SetsStatusToProcessed()
    {
        var msg = new Message { Topic = "test", Payload = "data" };
        msg.MarkAsProcessed();
        Assert.That(msg.Status, Is.EqualTo(MessageStatus.Processed));
    }

    [Test]
    public void MarkAsFailed_SetsStatusAndErrorMessage()
    {
        var msg = new Message { Topic = "test", Payload = "data" };
        msg.MarkAsFailed("Connection timeout");
        Assert.That(msg.Status, Is.EqualTo(MessageStatus.Failed));
        Assert.That(msg.ErrorMessage, Is.EqualTo("Connection timeout"));
    }

    [Test]
    public void IncrementRetry_ResetsToP ending_And_ClearsError()
    {
        var msg = new Message { Topic = "test", Payload = "data" };
        msg.MarkAsFailed("error");
        msg.IncrementRetry();

        Assert.That(msg.RetryCount, Is.EqualTo(1));
        Assert.That(msg.Status, Is.EqualTo(MessageStatus.Pending));
        Assert.That(msg.ErrorMessage, Is.Null);
    }

    [Test]
    public void CanRetry_ReturnsFalse_WhenMaxRetriesExceeded()
    {
        var msg = new Message { Topic = "test", Payload = "data" };
        msg.IncrementRetry();
        msg.IncrementRetry();
        msg.IncrementRetry();

        Assert.That(msg.CanRetry(maxRetries: 3), Is.False);
    }

    [Test]
    public void CanRetry_ReturnsTrue_WhenBelowMaxRetries()
    {
        var msg = new Message { Topic = "test", Payload = "data" };
        msg.IncrementRetry();

        Assert.That(msg.CanRetry(maxRetries: 3), Is.True);
    }

    [Test]
    public void NewMessage_GeneratesUniqueIds()
    {
        var msg1 = new Message { Topic = "t", Payload = "p" };
        var msg2 = new Message { Topic = "t", Payload = "p" };
        Assert.That(msg1.Id, Is.Not.EqualTo(msg2.Id));
    }
}
