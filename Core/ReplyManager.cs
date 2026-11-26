using MateoSECS.Exceptions;
using MateoSECS.Interface;
using MateoSECS.Secs;
using System.Collections.Concurrent;

namespace MateoSECS.Core;

internal class ReplyManager : IDisposable
{
    public bool ValidateStreamFunction { get; init; } = true;
    private readonly ConcurrentDictionary<uint, PendingRequest> _pending = new();
    private int _disposed;
    private readonly ISecsLogger _logger;
    private long _totalTimeouts;
    private long _totalRequests;
    private long _totalReplies;
    public int PendingCount => _pending.Count;
    public long TotalRequests => Interlocked.Read(ref _totalRequests);

    public long TotalReplies => Interlocked.Read(ref _totalReplies);

    public long TotalTimeouts => Interlocked.Read(ref _totalTimeouts);
    public IReadOnlyList<PendingRequest> Snapshot() => _pending.Values.ToArray();

    public ReplyManager(ISecsLogger? logger)
    {
        _logger = logger ?? NullSecsLogger.Instance;
    }

    public PendingRequest Register(SecsMsg msg, uint systemBytes, TimeSpan t3)
    {
        //Register a pending request
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        ArgumentNullException.ThrowIfNull(msg);
        if (!msg.Wbit)
            throw new ArgumentException($"{msg} 未设置 W-Bit，不应登记等待回复。", nameof(msg));
        if (t3 <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(t3), t3, "T3 必须大于 0。");

        var request = new PendingRequest(systemBytes, msg, t3);
        if (!_pending.TryAdd(systemBytes, request))
        {
            request.Dispose();
            throw new InvalidOperationException($"SystemBytes 0x{systemBytes:X8} 已存在在途事务，无法重复登记。");
        }

        if (request.IsSettled)
            OnSettled(request);

        //已经被释放了
        if (Volatile.Read(ref _disposed) != 0)
        {
            request.TryFail(new ObjectDisposedException(nameof(ReplyManager)));
            TryRemove(request);
            throw new ObjectDisposedException(nameof(ReplyManager));
        }

        Interlocked.Increment(ref _totalRequests);

        _logger.Trace($"登记事务 {msg} SysBytes=0x{systemBytes:X8}，在途 {_pending.Count}。");

        return request;
    }

    /// <summary>
    /// 移除Request，如果是超时 会记录超时次数
    /// </summary>
    /// <param name="request"></param>
    private void OnSettled(PendingRequest request)
    {
        if (!TryRemove(request))
            return;
        if (request.IsTimeout)
            Interlocked.Increment(ref _totalTimeouts);
    }
    /// <summary>
    /// 使用这种方式条件删除
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    private bool TryRemove(PendingRequest request)
    => ((ICollection<KeyValuePair<uint, PendingRequest>>)_pending)
        .Remove(new KeyValuePair<uint, PendingRequest>(request.SystemBytes, request));

    public bool TryComplete(SecsMsg secondary, uint systemBytes)
    {
        ArgumentNullException.ThrowIfNull(secondary);

        if (!_pending.TryGetValue(systemBytes, out PendingRequest? request))
        {
            _logger.Warn($"收到 {secondary} 但无对应事务（SysBytes=0x{systemBytes:X8}），可能已 T3 超时或对端重发。");
            return false;
        }

        if (!request.TryComplete(secondary, ValidateStreamFunction, out string? reason))
        {
            if (reason is not null)
            {
                _logger.Error($"事务 0x{systemBytes:X8} 配对校验失败：{reason}，已丢弃该回复。");

                // 配对错误属于对端协议实现问题，直接让该事务失败，避免业务侧一直等到 T3
                request.TryFail(new SecsCommunicationException(
                    $"回复与请求不配对：{reason}（SysBytes=0x{systemBytes:X8}）。")
                {
                    RelatedMessage = request.PrimaryMsg,
                    SystemBytes = systemBytes,
                });
            }

            return false;
        }

        Interlocked.Increment(ref _totalReplies);
        _logger.Trace($"事务 0x{systemBytes:X8} 完成：{request.PrimaryMsg} → {secondary}，耗时 {request.Elapsed.TotalMilliseconds:F0}ms。");
        return true;
    }

    public bool TryFail(uint systemBytes, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (!_pending.TryGetValue(systemBytes, out PendingRequest? request))
            return false;

        bool failed = request.TryFail(error);
        if (failed)
            _logger.Debug($"事务 0x{systemBytes:X8} 失败：{error.Message}");

        return failed;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        FailAll(new SecsCommunicationException("ReplyManager 已释放，在途事务被取消。"));
    }

    public void FailAll(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (_pending.IsEmpty)
            return;

        _logger.Warn($"链路中断，{_pending.Count} 个在途事务全部失败：{error.Message}");

        foreach (PendingRequest request in _pending.Values.ToArray())
            request.TryFail(error);
    }
    public override string ToString()
    => $"ReplyManager[在途={PendingCount}, 请求={TotalRequests}, 回复={TotalReplies}, 超时={TotalTimeouts}]";
}
