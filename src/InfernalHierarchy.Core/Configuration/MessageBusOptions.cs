namespace InfernalHierarchy.Core.Configuration;

public enum MessageQueueOverflowPolicy
{
    Block = 0,
    DropOldest = 1,
    Reject = 2
}

public sealed class MessageBusOptions
{
    public int QueueCapacity { get; set; } = 1000;

    public MessageQueueOverflowPolicy OverflowPolicy { get; set; } = MessageQueueOverflowPolicy.Block;
}