using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Kiosk.Services;

/// <summary>
/// Relays cart state from the till to any customer display. Routing through the
/// server instead of the browser's BroadcastChannel lets the display run in a
/// separate browser instance, its own profile, or on another machine entirely.
/// </summary>
public class DisplayHub
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subscribers = new();

    private const string EmptyCart = """{"type":"update","cart":[]}""";

    private readonly object _gate = new();

    // Last known cart, replayed to a display the moment it connects.
    private volatile string _snapshot = EmptyCart;

    // Who pushed that cart, so one person signing out cannot blank the order
    // another person is still ringing up.
    private string? _snapshotOwner;

    public string Snapshot => _snapshot;

    public void Publish(string payload, bool isSnapshot, string? owner = null)
    {
        lock (_gate)
        {
            if (isSnapshot)
            {
                _snapshot = payload;
                _snapshotOwner = owner;
            }
            WriteToSubscribers(payload);
        }
    }

    /// <summary>
    /// Takes a signed-out user's cart off the customer screen. The till cannot do
    /// this itself: it only pushes while it is open, and the sign-out replaces it.
    /// </summary>
    public void ClearFor(string? owner)
    {
        if (owner is null) return;
        lock (_gate)
        {
            if (_snapshotOwner != owner) return;
            _snapshot = EmptyCart;
            _snapshotOwner = null;
            WriteToSubscribers(EmptyCart);
        }
    }

    private void WriteToSubscribers(string payload)
    {
        foreach (var channel in _subscribers.Values) channel.Writer.TryWrite(payload);
    }

    public (Guid Id, ChannelReader<string> Reader) Subscribe()
    {
        var id = Guid.NewGuid();
        // Drop stale frames rather than stalling the till if a display is slow.
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(32)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subscribers.TryRemove(id, out var channel)) channel.Writer.TryComplete();
    }
}
