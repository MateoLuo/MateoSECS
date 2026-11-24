using MateoSECS.Interface;

namespace MateoSECS.Hsms;

internal sealed class HsmsLinktestService : IDisposable
{
    private readonly HsmsOptions _options;
    private readonly ISecsLogger _logger;
    private readonly HsmsTimer? _timer;
    private readonly Func<CancellationToken, Task> _linktest;
    private readonly Func<Exception, Task> _onFailure;

    public HsmsLinktestService(
        HsmsOptions options,
        ISecsLogger logger,
        Func<CancellationToken, Task> linktest,
        Func<Exception, Task> onFailure)
    {
        _options = options;
        _logger = logger ?? NullSecsLogger.Instance;
        _linktest = linktest;
        _onFailure = onFailure;

        if (options.EnableLinktest && options.Timeouts.LinktestInterval > TimeSpan.Zero)
        {
            _timer = new HsmsTimer(
                $"{options.Name}-Linktest", options.Timeouts.LinktestInterval,
                OnElapsedAsync, periodic: true, _logger);
        }
    }

    /// <summary>由门面在 StatusChanged 时调用，控制定时器启停。</summary>
    public void OnStatusChanged(HsmsConnectionStatus Status)
    {
        if (_timer is null) return;
        if (Status == HsmsConnectionStatus.Selected) _timer.Start();
        else _timer.Stop();
    }

    private async Task OnElapsedAsync(CancellationToken token)
    {
        try
        {
            await _linktest(token).ConfigureAwait(false);
            _logger.Trace($"[{_options.Name}] Linktest OK。");
        }
        catch (Exception ex)
        {
            _logger.Error($"[{_options.Name}] Linktest 失败，判定链路异常并断开：{ex.Message}");
            await _onFailure(ex).ConfigureAwait(false);
        }
    }

    public void Dispose() => _timer?.Dispose();
}
