using MateoSECS.Exceptions;
using MateoSECS.Interface;

namespace MateoSECS.Hsms;

internal sealed class HsmsSessionStatus : IDisposable
{
    private readonly HsmsOptions _options;
    private readonly HsmsTimeouts _timeouts;
    private readonly ISecsLogger _logger;
    private readonly HsmsTimer _t7Timer;
    private int _Status = (int)HsmsConnectionStatus.NotSelected;

    public HsmsSessionStatus(HsmsOptions options, ISecsLogger logger)
    {
        _options = options;
        _logger = logger ?? NullSecsLogger.Instance;
        _timeouts = options.Timeouts;
        _t7Timer = new HsmsTimer(
            $"{options.Name}-T7", _timeouts.T7, OnT7ElapsedAsync, periodic: false, _logger);
    }

    public HsmsConnectionStatus Status => (HsmsConnectionStatus)Volatile.Read(ref _Status);
    public bool IsSelected => Status == HsmsConnectionStatus.Selected;

    public event EventHandler<HsmsConnectionStatus>? StatusChanged;
    /// <summary>T7 超时：门面负责断开连接。</summary>
    public event EventHandler<SecsTimeoutException>? T7Expired;

    public void TransitionTo(HsmsConnectionStatus newStatus)
    {
        var old = (HsmsConnectionStatus)Interlocked.Exchange(ref _Status, (int)newStatus);
        _logger.Info($"[{_options.Name}] 状态变更：{old} → {newStatus}");

        if (newStatus == HsmsConnectionStatus.Selected)
            _t7Timer.Stop();
        else if (newStatus == HsmsConnectionStatus.NotSelected && _options.Mode == HsmsConnectionMode.Passive)
            _t7Timer.Restart();

        try { StatusChanged?.Invoke(this, newStatus); }
        catch (Exception ex) { _logger.Warn($"[{_options.Name}] StatusChanged 处理器异常：{ex.Message}", ex); }
    }

    public void EnsureSelected()
    {
        if (!IsSelected)
            throw new SecsStatusException($"[{_options.Name}] 当前状态为 {Status}，仅 SELECTED 状态可收发数据消息。");
    }

    private Task OnT7ElapsedAsync(CancellationToken token)
    {
        if (IsSelected) return Task.CompletedTask;

        var ex = new SecsTimeoutException(HsmsTimerType.T7, _timeouts.T7,
            $"[{_options.Name}] T7 超时：{_timeouts.T7.TotalSeconds}s 内未完成 Select，断开连接。");
        _logger.Warn(ex.Message);
        T7Expired?.Invoke(this, ex);
        return Task.CompletedTask;
    }

    public void Dispose() => _t7Timer.Dispose();
}
