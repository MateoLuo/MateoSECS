using MateoSECS.Exceptions;
using MateoSECS.Interface;
using MateoSECS.Secs;
using System.Buffers;
using System.Buffers.Binary;
using System.Net.Sockets;

namespace MateoSECS.Hsms;

internal sealed class HsmsTransport : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly HsmsOptions _options;
    private readonly HsmsTimeouts _timeouts;
    private readonly ISecsLogger _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private Task? _receiveLoop;
    private int _closed;

    public HsmsTransport(Socket socket, HsmsOptions options, ISecsLogger logger)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullSecsLogger.Instance;
        _timeouts = options.Timeouts;
        _timeouts.Validate();

        _socket.NoDelay = true;
        _stream = new NetworkStream(_socket, ownsSocket: false);
        RemoteEndPoint = socket.RemoteEndPoint?.ToString() ?? "unknown";
    }

    public string RemoteEndPoint { get; }
    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public CancellationToken LifetimeToken => _lifetimeCts.Token;

    /// <summary>收到完整报文（解析失败不上报，直接 continue，与原来一致）。</summary>
    public event Func<HsmsMsg, CancellationToken, Task>? MessageReceived;
    /// <summary>底层错误（报文解析失败等），由门面转发到 ErrorOccurred。</summary>
    public event EventHandler<Exception>? ErrorOccurred;
    /// <summary>连接已关闭，参数为关闭原因（正常为 null）。</summary>
    public event Action<Exception?>? Closed;

    public void StartReceiving()
        => _receiveLoop = Task.Run(() => ReceiveLoopAsync(_lifetimeCts.Token), CancellationToken.None);

    public async Task SendAsync(HsmsMsg message, CancellationToken cancellationToken)
    {
        if (IsClosed)
            throw new SecsCommunicationException($"[{_options.Name}] 连接已关闭，无法发送。");

        byte[] frame = message.Encode();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeCts.Token);

        await _sendLock.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(frame, linked.Token).ConfigureAwait(false);
            await _stream.FlushAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            await CloseAsync(ex).ConfigureAwait(false);
            throw new SecsCommunicationException($"[{_options.Name}] 发送失败：{ex.Message}", ex);
        }
        finally
        {
            _sendLock.Release();
        }

        LogMessage("TX", message);
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var lengthBuffer = new byte[HsmsMsg.LengthPrefixSize];
        try
        {
            while (!token.IsCancellationRequested)
            {
                int read = await _stream.ReadAsync(lengthBuffer.AsMemory(0, 1), token).ConfigureAwait(false);
                if (read == 0)
                    throw new SecsCommunicationException($"[{_options.Name}] 对端关闭了连接。");

                await ReadExactAsync(lengthBuffer.AsMemory(1, 3), token).ConfigureAwait(false);

                uint length = BinaryPrimitives.ReadUInt32BigEndian(lengthBuffer);
                if (!HsmsMsg.IsValidMessageLength(length, _options.MaxMessageLength))
                    throw new SecsCommunicationException(
                        $"[{_options.Name}] 非法消息长度 {length}（允许 {HsmsMsg.MinMessageLength}..{_options.MaxMessageLength}）。");

                byte[] payload = ArrayPool<byte>.Shared.Rent((int)length);
                try
                {
                    await ReadExactAsync(payload.AsMemory(0, (int)length), token).ConfigureAwait(false);

                    HsmsMsg message;
                    try { message = HsmsMsg.DecodePayload(payload.AsSpan(0, (int)length)); }
                    catch (Exception ex)
                    {
                        _logger.Error($"[{_options.Name}] 报文解析失败：{ex.Message} | HEX={SecsDecoder.ToHexString(payload.AsSpan(0, (int)Math.Min(length, 32)))}", ex);
                        ErrorOccurred?.Invoke(this, ex);
                        continue;
                    }

                    LogMessage("RX", message);
                    if (MessageReceived is { } handler)
                        await handler(message, token).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(payload);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await CloseAsync(ex).ConfigureAwait(false);
            return;
        }

        await CloseAsync(null).ConfigureAwait(false);
    }

    private async ValueTask ReadExactAsync(Memory<byte> buffer, CancellationToken token)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            using var t8Cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            t8Cts.CancelAfter(_timeouts.T8);

            int read;
            try { read = await _stream.ReadAsync(buffer[total..], t8Cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new SecsTimeoutException(HsmsTimerType.T8, _timeouts.T8,
                    $"[{_options.Name}] T8 网络字符间超时：已收 {total}/{buffer.Length} 字节。");
            }

            if (read == 0)
                throw new SecsCommunicationException($"[{_options.Name}] 接收中对端关闭了连接（已收 {total}/{buffer.Length} 字节）。");

            total += read;
        }
    }

    public async Task CloseAsync(Exception? reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;

        try { _lifetimeCts.Cancel(); } catch (ObjectDisposedException) { }

        try
        {
            _stream.Dispose();
            if (_socket.Connected)
                _socket.Shutdown(SocketShutdown.Both);
        }
        catch { }
        finally { _socket.Dispose(); }

        Closed?.Invoke(reason);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void LogMessage(string direction, HsmsMsg message)
    {
        if (!_logger.IsEnabled(SecsLogLevel.Debug)) return;

        string head = $"[{_options.Name}] {direction} {HsmsControlMsg.Describe(message)} SysBytes=0x{message.SystemBytes:X8}";
        if (_options.LogSml && message.IsDataMsg && message.RawBody.Length > 0)
            _logger.Debug($"{head}{Environment.NewLine}{message.ToSML()}");
        else
            _logger.Debug(head);
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(null).ConfigureAwait(false);
        if (_receiveLoop is not null)
        {
            try { await _receiveLoop.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false); } catch { }
        }
        _sendLock.Dispose();
        _lifetimeCts.Dispose();
    }
}
