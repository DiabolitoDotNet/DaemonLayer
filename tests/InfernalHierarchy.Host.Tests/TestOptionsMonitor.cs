using Microsoft.Extensions.Options;

namespace InfernalHierarchy.Host.Tests;

internal sealed class TestOptionsMonitor<T> : IOptionsMonitor<T>
{
    private readonly object _gate = new();
    private readonly List<Action<T, string?>> _listeners = new();
    private T _value;

    public TestOptionsMonitor(T value)
    {
        _value = value;
    }

    public int ListenerCount
    {
        get
        {
            lock (_gate)
            {
                return _listeners.Count;
            }
        }
    }

    public T CurrentValue => _value;

    public T Get(string? name) => _value;

    public IDisposable OnChange(Action<T, string?> listener)
    {
        lock (_gate)
        {
            _listeners.Add(listener);
        }

        return new DisposableAction(() =>
        {
            lock (_gate)
            {
                _listeners.Remove(listener);
            }
        });
    }

    public void Set(T value)
    {
        List<Action<T, string?>> snapshot;
        lock (_gate)
        {
            _value = value;
            snapshot = _listeners.ToList();
        }

        foreach (var listener in snapshot)
        {
            listener(value, null);
        }
    }

    private sealed class DisposableAction : IDisposable
    {
        private readonly Action _dispose;
        private int _disposed;

        public DisposableAction(Action dispose)
        {
            _dispose = dispose;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _dispose();
        }
    }
}
