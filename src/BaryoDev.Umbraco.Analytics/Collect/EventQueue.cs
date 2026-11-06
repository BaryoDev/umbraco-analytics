using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.Analytics.Collect;

/// <summary>Bounded in-memory queue. A full queue drops the event so a slow database never slows a page.</summary>
public sealed class EventQueue
{
    private readonly Channel<Hit> _channel;

    public EventQueue(IOptions<AnalyticsOptions> options)
    {
        _channel = Channel.CreateBounded<Hit>(new BoundedChannelOptions(Math.Max(100, options.Value.QueueCapacity))
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropWrite,
        });
    }

    public bool TryEnqueue(Hit hit) => _channel.Writer.TryWrite(hit);

    public ChannelReader<Hit> Reader => _channel.Reader;
}
