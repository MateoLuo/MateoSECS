using MateoSECS.Core;
using MateoSECS.Event;
using MateoSECS.Exceptions;
using MateoSECS.Interface;
using MateoSECS.Secs;
using System.Net.Sockets;

namespace MateoSECS.Hsms;

/// <summary>
/// HSMS Active 端（通常是 Host / MES 侧，也可配置设备侧为 Active）。
/// 负责：TCP 连接 → Select → 维持会话 → 断线按 T5 间隔自动重连。
/// </summary>
public sealed class HsmsClient : IAsyncDisposable
{
    private readonly HsmsOptions _options;
    private readonly ISecsLogger _logger;
    private readonly SystemByteGenerator _systemBytes = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    private CancellationTokenSource? _runCts;
    private Task? _connectLoop;
    private HsmsConnection? _connection;
    private int _disposed;

    public HsmsClient(HsmsOptions options, ISecsLogger? logger = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Timeouts.Validate();
        _logger = logger ?? NullSecsLogger.Instance;

        if (_options.Mode != HsmsConnectionMode.Active)
            throw new ArgumentException("HsmsClient 必须使用 Active 模式。", nameof(options));
    }

    public string Name => _options.Name;

    public HsmsConnection? Connection => Volatile.Read(ref _connection);

    public HsmsConnectionStatus Status =>Connection?.Status ?? HsmsConnectionStatus.NotConnected;
    public bool IsSelected => Status == HsmsConnectionStatus.Selected;

    public event EventHandler<HsmsConnectionStatus>? StatusChanged;

    public event EventHandler<SecsMsgEventArgs>? PrimaryMessageReceived;

    public event EventHandler<Exception>? ErrorOccurred;

    /// <summary>异步 Primary 处理器，会挂到每条新建连接上。</summary>
    public Func<SecsMsgEventArgs, Task>? MessageHandler { get; set; }

    /// <summary>启动后台连接循环（不阻塞调用方）。</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_runCts is not null)
            throw new InvalidOperationException($"[{Name}] 客户端已启动。");

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _connectLoop = Task.Run(() => ConnectLoopAsync(_runCts.Token), CancellationToken.None);

        _logger.Info($"[{Name}] Active 客户端启动，目标 {_options.IpAddress}:{_options.Port}。");
        return Task.CompletedTask;
    }

    /// <summary>连接并完成 Select，最长等待 timeout。适合「必须连上才继续」的启动流程。</summary>
    public async Task<bool> WaitUntilSelectedAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (IsSelected)
                    return true;

                await Task.Delay(100, cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        return IsSelected;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts = _runCts;
        _runCts = null;

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            cts.Dispose();
        }

        HsmsConnection? connection = Interlocked.Exchange(ref _connection, null);
        if (connection is not null)
            await connection.DisposeAsync().ConfigureAwait(false);

        if (_connectLoop is not null)
        {
            try
            {
                await _connectLoop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch
            {
                // 忽略
            }

            _connectLoop = null;
        }

        _logger.Info($"[{Name}] 客户端已停止。");
    }

    /// <summary>发送 SECS-II 消息，W-Bit=true 时等待回复（T3）。</summary>
    public Task<SecsMsg?> SendAsync(SecsMsg message, CancellationToken cancellationToken = default)
    {
        HsmsConnection connection = Connection
            ?? throw new SecsStatusException($"[{Name}] 尚未建立连接，无法发送 {message}。");

        return connection.SendAsync(message, cancellationToken);
    }

    // ---------------- 连接循环 ----------------

    private async Task ConnectLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await ConnectOnceAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.Warn($"[{Name}] 连接失败：{ex.Message}");
                RaiseError(ex);
            }

            if (!_options.AutoReconnect || token.IsCancellationRequested)
                return;

            // T5：两次连接尝试之间的最小间隔
            try
            {
                _logger.Debug($"[{Name}] {_options.Timeouts.T5.TotalSeconds}s 后重试（T5）。");
                await Task.Delay(_options.Timeouts.T5, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task ConnectOnceAsync(CancellationToken token)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
        };

        try
        {
            await socket.ConnectAsync(_options.ToEndPoint(), token).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        var connection = new HsmsConnection(socket, _options, _logger, _systemBytes);
        AttachHandlers(connection);

        await _connectionLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            _connection = connection;
        }
        finally
        {
            _connectionLock.Release();
        }

        var closedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Closed += (_, _) => closedTcs.TrySetResult();

        try
        {
            await connection.StartAsync(token).ConfigureAwait(false);
            _logger.Info($"[{Name}] 会话已建立（SELECTED）。");
        }
        catch (Exception ex)
        {
            _logger.Warn($"[{Name}] Select 失败：{ex.Message}");
            await connection.DisposeAsync().ConfigureAwait(false);
            Interlocked.CompareExchange(ref _connection, null, connection);
            throw;
        }

        // 阻塞直到连接断开，然后由外层循环按 T5 重连
        using (token.Register(() => closedTcs.TrySetResult()))
        {
            await closedTcs.Task.ConfigureAwait(false);
        }

        Interlocked.CompareExchange(ref _connection, null, connection);
        await connection.DisposeAsync().ConfigureAwait(false);
    }

    private void AttachHandlers(HsmsConnection connection)
    {
        connection.StatusChanged += (_, Status) => StatusChanged?.Invoke(this, Status);
        connection.ErrorOccurred += (_, ex) => RaiseError(ex);
        connection.PrimaryMessageReceived += (_, args) => PrimaryMessageReceived?.Invoke(this, args);
        connection.MessageHandler = args => MessageHandler?.Invoke(args) ?? Task.CompletedTask;
    }

    private void RaiseError(Exception ex)
    {
        try
        {
            ErrorOccurred?.Invoke(this, ex);
        }
        catch
        {
            // 忽略
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await StopAsync().ConfigureAwait(false);
        _connectionLock.Dispose();
    }
}