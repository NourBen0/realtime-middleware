using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RealtimeMiddleware.Application.Interfaces;

namespace RealtimeMiddleware.Infrastructure.MessageBus;

public class RetryBackgroundService : BackgroundService
{
    private readonly IMessageService _messageService;
    private readonly ILogger<RetryBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public RetryBackgroundService(IMessageService messageService, ILogger<RetryBackgroundService> logger)
    {
        _messageService = messageService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Retry service started. Interval: {Interval}s", _interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, stoppingToken);
                await _messageService.RetryFailedAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in retry background service");
            }
        }

        _logger.LogInformation("Retry service stopped");
    }
}
