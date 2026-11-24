using MateoSECS.Interface;

namespace MateoSECS.Hsms;

public sealed class HsmsTimer : IDisposable
{
    private readonly string _name;
    private readonly TimeSpan _interval;
    private readonly bool _periodic;
    private readonly ISecsLogger _logger;
    private readonly object _sync = new();
    private readonly Func<CancellationToken, Task> _callback;
    private bool _disposed;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    public bool IsRunning
    {
        get
        {
            lock (_sync)
                return _cts is { IsCancellationRequested: false };
        }
    }
    public HsmsTimer(
        string name,
        TimeSpan interval,
        Func<CancellationToken, Task> callback,
        bool periodic,
        ISecsLogger? logger = null)
    {
        _name = name;
        _interval = interval;
        _periodic = periodic;
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        _logger = logger ?? NullSecsLogger.Instance;
    }
    public void Start()
    {
        if (_interval <= TimeSpan.Zero) return;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_cts is not null) return;
            _cts = new CancellationTokenSource();
            _loop = RunAsync(_cts.Token);
            _logger.Trace($"Timer [{_name}] Start!,Interval {_interval.TotalSeconds}S.");
        }
    }
    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            do
            {
                await Task.Delay(_interval, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                try
                {
                    await _callback(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Timer[{_name}] Callback Exception：{ex.Message}", ex);
                    if (!_periodic)
                        return;
                }
            } while (_periodic && !token.IsCancellationRequested);
        }
        catch (ObjectDisposedException)
        {

        }
    }
    public void Restart()
    {
        Stop();
        Start();
    }
    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_sync)
        {
            cts = _cts;
            _cts = null;
            _loop = null;
        }
        if (cts is null) return;
        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            cts.Dispose();
            _logger.Trace($"Timer [{_name}] Stop!");
        }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
        }
        Stop();
    }
}
