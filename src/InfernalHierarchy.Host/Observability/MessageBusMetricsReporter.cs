using InfernalHierarchy.Messaging.Bus;

namespace InfernalHierarchy.Host.Observability;

internal sealed class MessageBusMetricsReporter : BackgroundService
{
    private readonly IMessageBus _messageBus;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<MessageBusMetricsReporter> _logger;

    public MessageBusMetricsReporter(
        IMessageBus messageBus,
        MetricsCollector metrics,
        ILogger<MessageBusMetricsReporter> logger)
    {
        _messageBus = messageBus;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_messageBus is ChannelMessageBus bus)
                {
                    _metrics.SetGauge("message_bus.channels.active", bus.ActiveChannelCount);
                    _metrics.SetGauge("message_bus.broadcast_subscribers.active", bus.ActiveBroadcastSubscriberCount);
                    _metrics.SetGauge("message_bus.queue.targeted.depth", bus.TargetedQueueDepth);
                    _metrics.SetGauge("message_bus.queue.broadcast.depth", bus.BroadcastQueueDepth);
                    _metrics.SetGauge("message_bus.queue.depth.total", bus.TargetedQueueDepth + bus.BroadcastQueueDepth);
                    _metrics.SetGauge("message_bus.messages.dropped", bus.DroppedMessages);
                    _metrics.SetGauge("message_bus.messages.rejected", bus.RejectedMessages);
                    _metrics.SetGauge("message_bus.messages.deferred", bus.DeferredMessages);
                    _metrics.SetGauge("message_bus.backpressure.active", bus.IsBackpressureActive ? 1 : 0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to refresh message bus metrics");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken).ConfigureAwait(false);
        }
    }
}