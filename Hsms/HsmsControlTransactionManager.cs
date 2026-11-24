using MateoSECS.Exceptions;
using MateoSECS.Interface;
using System.Collections.Concurrent;

namespace MateoSECS.Hsms;

internal sealed class HsmsControlTransactionManager : IDisposable
{
    private readonly string _name;
    private readonly HsmsTimeouts _timeouts;
    private readonly ISecsLogger _logger;
    private readonly CancellationToken _lifetimeToken;
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<HsmsMsg>> _pending = new();

    public HsmsControlTransactionManager(string name, HsmsTimeouts timeouts, ISecsLogger logger, CancellationToken lifetimeToken)
    {
        _name = name;
        _timeouts = timeouts;
        _logger = logger ?? NullSecsLogger.Instance;
        _lifetimeToken = lifetimeToken;
    }

    public async Task<HsmsMsg> RequestAsync(
        HsmsMsg request,
        Func<HsmsMsg, CancellationToken, Task> sendAsync,
        CancellationToken cancellationToken)
    {
        uint systemBytes = request.SystemBytes;
        var tcs = new TaskCompletionSource<HsmsMsg>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(systemBytes, tcs))
            throw new InvalidOperationException($"SystemBytes 0x{systemBytes:X8} 冲突。");

        try
        {
            await sendAsync(request, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
            timeoutCts.CancelAfter(_timeouts.T6);

            try { return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new SecsTimeoutException(HsmsTimerType.T6, _timeouts.T6,
                    $"[{_name}] {HsmsControlMsg.Describe(request)} 等待响应超时（T6={_timeouts.T6.TotalSeconds}s）。");
            }
        }
        finally
        {
            _pending.TryRemove(systemBytes, out _);
        }
    }

    public bool TryComplete(HsmsMsg response)
    {
        if (_pending.TryRemove(response.SystemBytes, out var tcs)) { tcs.TrySetResult(response); return true; }
        return false;
    }

    public bool TryFail(uint systemBytes, Exception ex)
    {
        if (_pending.TryRemove(systemBytes, out var tcs)) { tcs.TrySetException(ex); return true; }
        return false;
    }

    public void FailAll(Exception ex)
    {
        foreach (var kv in _pending) kv.Value.TrySetException(ex);
        _pending.Clear();
    }

    public void Dispose() => FailAll(new ObjectDisposedException(nameof(HsmsControlTransactionManager)));
}
