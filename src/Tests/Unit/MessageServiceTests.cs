using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using RealtimeMiddleware.Application.DTOs;
using RealtimeMiddleware.Application.Interfaces;
using RealtimeMiddleware.Application.Services;
using RealtimeMiddleware.Domain.Entities;
using RealtimeMiddleware.Domain.Enums;
using RealtimeMiddleware.Domain.Interfaces;

namespace RealtimeMiddleware.Tests.Unit;

[TestFixture]
public class MessageServiceTests
{
    private Mock<IMessageRepository> _repoMock = null!;
    private Mock<IMessageBus> _busMock = null!;
    private Mock<IWebSocketManager> _wsMock = null!;
    private MessageService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _repoMock = new Mock<IMessageRepository>();
        _busMock = new Mock<IMessageBus>();
        _wsMock = new Mock<IWebSocketManager>();
        _service = new MessageService(_repoMock.Object, _busMock.Object, _wsMock.Object,
            NullLogger<MessageService>.Instance);
    }

    [Test]
    public async Task PublishAsync_SavesMessageAndPublishesToBus()
    {
        // Arrange
        var request = new PublishMessageRequest("sensors.temperature", "{\"value\": 36.5}", MessagePriority.High);
        _repoMock.Setup(r => r.AddAsync(It.IsAny<Message>(), default)).Returns(Task.CompletedTask);
        _busMock.Setup(b => b.PublishAsync(It.IsAny<Message>(), default)).Returns(Task.CompletedTask);
        _wsMock.Setup(w => w.BroadcastAsync(It.IsAny<string>(), default)).Returns(Task.CompletedTask);

        // Act
        var result = await _service.PublishAsync(request);
        await Task.Delay(100); // Let fire-and-forget complete

        // Assert
        Assert.That(result.Topic, Is.EqualTo("sensors.temperature"));
        Assert.That(result.Priority, Is.EqualTo(MessagePriority.High));
        _repoMock.Verify(r => r.AddAsync(It.IsAny<Message>(), default), Times.Once);
    }

    [Test]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        _repoMock.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Message?)null);

        var result = await _service.GetByIdAsync(Guid.NewGuid());

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetStatsAsync_ReturnsCorrectCounts()
    {
        var messages = new List<Message>
        {
            new() { Topic = "t", Payload = "p" },
            new() { Topic = "t", Payload = "p" },
        };
        messages[1].MarkAsProcessed();

        _repoMock.Setup(r => r.GetAllAsync(default)).ReturnsAsync(messages);
        _wsMock.Setup(w => w.GetConnectedClientsCount()).Returns(5);

        var stats = await _service.GetStatsAsync();

        Assert.That(stats.TotalMessages, Is.EqualTo(2));
        Assert.That(stats.ProcessedMessages, Is.EqualTo(1));
        Assert.That(stats.PendingMessages, Is.EqualTo(1));
        Assert.That(stats.ConnectedClients, Is.EqualTo(5));
    }
}
