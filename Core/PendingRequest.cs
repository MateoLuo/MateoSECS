using MateoSECS.Exceptions;
using MateoSECS.Hsms;
using MateoSECS.Secs;
using System.Diagnostics;

namespace MateoSECS.Core;

/// <summary>
/// 我发一条消息出去，然后等待对方回一条消息，有T3超时功能
/// </summary>
public sealed class PendingRequest : IDisposable
{
    private readonly TaskCompletionSource<SecsMsg> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PendingRequestStatus Status => (PendingRequestStatus)_status;
    private int _status;
    private int _disposed;
    public bool IsSettled => Status != PendingRequestStatus.Pending;
    public bool IsTimeout => Status == PendingRequestStatus.TimedOut;
    public uint SystemBytes { get; set; }
    public SecsMsg PrimaryMsg { get; private set; }
    public TimeSpan Timeout { get; }
    public TimeSpan Elapsed => Stopwatch.GetElapsedTime(_startTimestamp);
    private readonly Action<PendingRequest>? _onSettled;
    private readonly CancellationTokenSource _timeoutCts;
    private readonly CancellationTokenRegistration _timeoutRegistration;
    private readonly long _startTimestamp;
    public DateTimeOffset CreatedAt { get; }
    public PendingRequest(
        uint systemBytes,
        SecsMsg primaryMsg,
        TimeSpan timeout,
        Action<PendingRequest>? onSettled = null)
    {
        SystemBytes = systemBytes;
        PrimaryMsg = primaryMsg ?? throw new ArgumentNullException(nameof(primaryMsg));
        Timeout = timeout;
        _onSettled = onSettled;

        _timeoutCts = new CancellationTokenSource(timeout);
        _timeoutRegistration = _timeoutCts.Token.Register(
            static status => ((PendingRequest)status!).OnTimeout(), this
            );
        _startTimestamp = Stopwatch.GetTimestamp();
        CreatedAt = DateTimeOffset.Now;
    }
    /// <summary>
    /// if return true is ok , false reply message have question
    /// </summary>
    /// <param name="secondary"></param>
    /// <param name="validateStreamFuncation"></param>
    /// <param name="mismatchReason"></param>
    /// <returns></returns>
    internal bool TryComplete(SecsMsg secondary, bool validateStreamFuncation, out string? mismatchReason)
    {
        mismatchReason = null;
        if (validateStreamFuncation && !IsValidPair(secondary, out mismatchReason))
            return false;
        if (!TrySetStatus(PendingRequestStatus.Replied))
            return false;

        if (secondary.F == 0)
        {
            _tcs.TrySetException(new SecsAbortException(
                secondary.S,
                $"对端以 S{secondary.S}F0 拒绝了 {PrimaryMsg}（SysBytes=0x{SystemBytes:X8}）。")
            {
                RelatedMessage = PrimaryMsg,
                SystemBytes = SystemBytes,
            });
        }
        else
        {
            _tcs.TrySetResult(secondary);
        }

        Settle();
        return true;

    }
    private bool IsValidPair(SecsMsg secondary, out string? reason)
    {
        if (secondary.S != PrimaryMsg.S)
        {
            reason = $"Stream 不匹配：期望 S{PrimaryMsg.S}，实际 S{secondary.S}F{secondary.F}";
            return false;
        }
        if (secondary.F != 0 && secondary.F != PrimaryMsg.F + 1)
        {
            reason = $"Function 不匹配：期望 S{PrimaryMsg.S}F{PrimaryMsg.F + 1} 或 S{PrimaryMsg.S}F0，实际 S{secondary.S}F{secondary.F}";
            return false;
        }
        reason = null;
        return true;
    }
    internal bool TryFail(Exception error)
    {
        if (!TrySetStatus(PendingRequestStatus.Failed))
            return false;
        _tcs.TrySetException(error);
        Settle();
        return true;
    }
    private void OnTimeout()
    {
        if (!TrySetStatus(PendingRequestStatus.TimedOut))
            return;
        _tcs.TrySetException(
            new SecsTimeoutException(
                HsmsTimerType.T3, Timeout, $"Waiting {PrimaryMsg} Reply Timeout [T3={Timeout.TotalSeconds}S,SystemBytes={SystemBytes:X8}]")
            {
                RelatedMessage = PrimaryMsg,
                SystemBytes = SystemBytes
            });
        Settle();
    }
    private void Settle()
    {
        try
        {
            _timeoutCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        _onSettled?.Invoke(this);
    }
    /// <summary>
    /// 只有在Pending的时候才能允许改变状态，其余都是False
    /// </summary>
    /// <param name="status"></param>
    /// <returns></returns>
    private bool TrySetStatus(PendingRequestStatus status)
    => Interlocked.CompareExchange(ref _status, (int)status, (int)PendingRequestStatus.Pending) == (int)PendingRequestStatus.Pending;
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        if (TrySetStatus(PendingRequestStatus.Canceled))
        {
            _tcs.TrySetCanceled();
            _onSettled?.Invoke(this);
        }
        _timeoutRegistration.Dispose();
        _timeoutCts.Dispose();
    }
    public Task<SecsMsg> WaitAsync(CancellationToken cancellationToken = default)
        => cancellationToken.CanBeCanceled ? _tcs.Task.WaitAsync(cancellationToken) : _tcs.Task;
    public override string ToString()
        => $"Pending[{PrimaryMsg}, SysBytes=0x{SystemBytes:X8}, Elapsed={Elapsed.TotalMilliseconds:F0}ms, Settled={IsSettled}]";
    private enum PendingRequestStatus
    {
        Pending,
        Replied,
        Failed,
        TimedOut,
        Canceled,
    }
}
