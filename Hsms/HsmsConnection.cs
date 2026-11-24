using MateoSECS.Core;
using MateoSECS.Event;
using MateoSECS.Exceptions;
using MateoSECS.Interface;
using MateoSECS.Secs;
using System.Net.Sockets;

namespace MateoSECS.Hsms;

public sealed class HsmsConnection : IAsyncDisposable
{
    private readonly HsmsTransport _transport;
    private readonly HsmsSessionStatus _status;
    private readonly HsmsControlTransactionManager _controlTx;
    private readonly HsmsLinktestService _linktest;
    private readonly HsmsMsgDispatcher _dispatcher;
    private readonly ReplyManager _replyManager;
    private readonly SystemByteGenerator _systemByteGenerator;
    private readonly HsmsOptions _options;
    private readonly HsmsTimeouts _timeouts;
    private readonly ISecsLogger _logger;
    private int _disposed;

    internal HsmsConnection(
        Socket socket,
        HsmsOptions options,
        ISecsLogger logger,
        SystemByteGenerator? systemByteGenerator = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullSecsLogger.Instance;
        _timeouts = options.Timeouts;
        _systemByteGenerator = systemByteGenerator ?? new SystemByteGenerator();

        _transport = new HsmsTransport(socket, options, _logger);
        _status = new HsmsSessionStatus(options, _logger);
        _controlTx = new HsmsControlTransactionManager(options.Name, _timeouts, _logger, _transport.LifetimeToken);
        _replyManager = new ReplyManager(_logger);

        _dispatcher = new HsmsMsgDispatcher(
            options, _logger, _status, _transport.SendAsync,
            _controlTx, _replyManager,
            DispatchPrimary, RaiseError, CloseAsync);

        _linktest = new HsmsLinktestService(options, _logger, LinktestAsync, ex => CloseAsync(ex));

        // ---- 接线 ----
        _transport.MessageReceived += _dispatcher.DispatchAsync;
        _transport.Closed += OnTransportClosed;
        _transport.ErrorOccurred += (_, ex) => RaiseError(ex);

        _status.StatusChanged += (_, s) => StatusChanged?.Invoke(this, s);
        _status.StatusChanged += (_, s) => _linktest.OnStatusChanged(s);
        _status.T7Expired += (_, ex) => _ = CloseAsync(ex);
    }

    // ---------------- 公开 API ----------------

    public string Name => _options.Name;
    public ushort SessionId => _options.SessionId;
    public string RemoteEndPoint => _transport.RemoteEndPoint;
    public HsmsConnectionStatus Status => _status.Status;
    public bool IsSelected => _status.IsSelected;

    public event EventHandler<HsmsConnectionStatus>? StatusChanged;
    public event EventHandler<SecsMsgEventArgs>? PrimaryMessageReceived;
    public Func<SecsMsgEventArgs, Task>? MessageHandler { get; set; }
    public event EventHandler<Exception?>? Closed;
    public event EventHandler<Exception>? ErrorOccurred;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _status.TransitionTo(HsmsConnectionStatus.NotSelected);   // Passive 端会顺手启动 T7

        _transport.StartReceiving();
        _logger.Info($"[{Name}] TCP 已建立：{RemoteEndPoint}（{_options.Mode}），{_timeouts}");

        if (_options.Mode == HsmsConnectionMode.Active)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _transport.LifetimeToken);
            await SelectAsync(linked.Token).ConfigureAwait(false);
        }
    }

    public async Task SelectAsync(CancellationToken cancellationToken = default)
    {
        if (IsSelected) return;

        HsmsMsg response = await _controlTx.RequestAsync(
            HsmsControlMsg.SelectRequest(SessionId, NextSystemBytes()),
            _transport.SendAsync, cancellationToken).ConfigureAwait(false);

        HsmsSelectStatus status = response.Header.SelectStatus;
        if (status is not (HsmsSelectStatus.Success or HsmsSelectStatus.AlreadyActive))
            throw new SecsCommunicationException($"[{Name}] Select 被拒绝：{status}。");

        _status.TransitionTo(HsmsConnectionStatus.Selected);
    }

    public async Task DeselectAsync(CancellationToken cancellationToken = default)
    {
        await _controlTx.RequestAsync(
            HsmsControlMsg.DeselectRequest(SessionId, NextSystemBytes()),
            _transport.SendAsync, cancellationToken).ConfigureAwait(false);
        _status.TransitionTo(HsmsConnectionStatus.NotSelected);
    }

    public Task LinktestAsync(CancellationToken cancellationToken = default)
        => _controlTx.RequestAsync(
            HsmsControlMsg.LinktestRequest(NextSystemBytes()),
            _transport.SendAsync, cancellationToken);

    public async Task SeparateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _transport.SendAsync(
                HsmsControlMsg.SeparateRequest(SessionId, NextSystemBytes()),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger.Warn($"[{Name}] 发送 Separate.req 失败：{ex.Message}"); }

        await CloseAsync(null).ConfigureAwait(false);
    }

    public async Task<SecsMsg?> SendAsync(SecsMsg message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        _status.EnsureSelected();

        uint systemBytes = NextSystemBytes();
        HsmsMsg frame = HsmsMsg.CreateDataMsg(SessionId, message, systemBytes);

        if (!message.Wbit)
        {
            await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false);
            return null;
        }

        using PendingRequest pending = _replyManager.Register(message, systemBytes, _timeouts.T3);
        try { await _transport.SendAsync(frame, cancellationToken).ConfigureAwait(false); }
        catch
        {
            _replyManager.TryFail(systemBytes,
                new SecsCommunicationException($"[{Name}] 发送 {message} 失败。"));
            throw;
        }

        return await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SendNoReplyAsync(SecsMsg message, CancellationToken cancellationToken = default)
        => SendAsync(message, cancellationToken);

    public Task CloseAsync(Exception? reason) => _transport.CloseAsync(reason);

    // ---------------- 内部：转发 / 编排 ----------------

    private void OnTransportClosed(Exception? reason)
    {
        var error = reason ?? new SecsCommunicationException($"[{Name}] 连接已关闭。");
        _controlTx.FailAll(error);
        _replyManager.FailAll(error);
        _status.TransitionTo(HsmsConnectionStatus.NotConnected);

        if (reason is null) _logger.Info($"[{Name}] 连接正常关闭。");
        else _logger.Warn($"[{Name}] 连接异常关闭：{reason.Message}");

        try { Closed?.Invoke(this, reason); }
        catch (Exception ex) { _logger.Warn($"[{Name}] Closed 处理器异常：{ex.Message}", ex); }
    }

    private void DispatchPrimary(SecsMsg secs, uint systemBytes)
    {
        var args = new SecsMsgEventArgs(this, secs, systemBytes);
        _ = Task.Run(() => InvokePrimaryHandlerAsync(args), CancellationToken.None);
    }

    private async Task InvokePrimaryHandlerAsync(SecsMsgEventArgs args)
    {
        try
        {
            PrimaryMessageReceived?.Invoke(this, args);
            if (MessageHandler is { } handler) await handler(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Error($"[{Name}] 处理 {args.Message} 时异常：{ex.Message}", ex);
            RaiseError(ex);

            if (args.ReplyExpected && !args.ReplyExpected)
            {
                try { await args.ReplyAbortAsync().ConfigureAwait(false); }
                catch (Exception replyEx) { _logger.Warn($"[{Name}] 回复 S{args.Message.S}F0 失败：{replyEx.Message}"); }
            }
        }
    }

    private uint NextSystemBytes() => _systemByteGenerator.Next();

    private void RaiseError(Exception ex)
    {
        try { ErrorOccurred?.Invoke(this, ex); }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        await _transport.DisposeAsync().ConfigureAwait(false);

        _status.Dispose();
        _linktest.Dispose();
        _controlTx.Dispose();
        _replyManager.Dispose();
    }

    public override string ToString() => $"{Name}@{RemoteEndPoint} [{Status}]";

    internal async Task SendReplyAsync(SecsMsg reply, uint systemBytes, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
