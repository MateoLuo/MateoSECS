using MateoSECS.Event;
using MateoSECS.Interface;
using MateoSECS.Secs;
using System.Collections.Concurrent;

namespace MateoSECS.Core;

/// <summary>
/// SxFy 消息路由表。把「收到什么消息 → 谁来处理 → 回什么」集中声明，
/// 业务代码不必再写一堆 switch (S, F)。
///
/// 用法：
///     var dispatcher = new SecsEventDispatcher(logger);
///     dispatcher.On(1, 13, _ => Task.FromResult&lt;SecsItem?&gt;(
///         Item.L(Item.B(0x00), Item.L(Item.A("EQP"), Item.A("1.0")))));
///     connection.MessageHandler = dispatcher.DispatchAsync;
/// </summary>
public sealed class SecsEventDispatcher
{
    private readonly ConcurrentDictionary<int, Func<SecsMsgEventArgs, Task>> _handlers = new();
    private readonly ISecsLogger _logger;

    public SecsEventDispatcher(ISecsLogger? logger = null)
    {
        _logger = logger ?? NullSecsLogger.Instance;
    }

    /// <summary>未注册消息的兜底处理器。为 null 时走默认策略。</summary>
    public Func<SecsMsgEventArgs, Task>? FallbackHandler { get; set; }

    /// <summary>
    /// 未注册且带 W-Bit 的消息是否自动回 SxF0（Abort）。默认 true。
    /// 关闭后对端会一直等到 T3，通常只有在调试抓包时才需要。
    /// </summary>
    public bool AutoAbortUnhandled { get; init; } = true;

    /// <summary>
    /// 未注册消息是否上报 S9F5（Unrecognized Function）。
    /// 需要同时设置 <see cref="StreamFunctionErrorSender"/>。
    /// </summary>
    public bool ReportS9OnUnhandled { get; init; }

    /// <summary>S9Fx 的发送通道（一般直接挂 connection.SendAsync）。</summary>
    public Func<SecsMsg, CancellationToken, Task>? StreamFunctionErrorSender { get; set; }

    /// <summary>已注册的 SxFy 列表（诊断用）。</summary>
    public IReadOnlyCollection<string> RegisteredFunctions
    {
        get
        {
            var list = new List<string>(_handlers.Count);
            foreach (int key in _handlers.Keys)
                list.Add($"S{key >> 8}F{key & 0xFF}");

            list.Sort(StringComparer.Ordinal);
            return list;
        }
    }

    // ---------------- 注册 ----------------

    /// <summary>注册完全自定义的处理器（自行决定是否回复）。</summary>
    public SecsEventDispatcher On(byte s, byte f, Func<SecsMsgEventArgs, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        int key = MakeKey(s, f);
        if (!_handlers.TryAdd(key, handler))
            throw new InvalidOperationException($"S{s}F{f} 已注册处理器。");

        _logger.Debug($"注册消息处理器 S{s}F{f}。");
        return this;
    }

    /// <summary>注册「返回回复 Item」的处理器，W-Bit 时框架自动回 SxF(y+1)。</summary>
    public SecsEventDispatcher On(byte s, byte f, Func<SecsMsgEventArgs, Task<SecsItem?>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return On(s, f, async args =>
        {
            SecsItem? reply = await handler(args).ConfigureAwait(false);

            if (args.ReplyExpected && !args.IsReplied)
                await args.ReplyAsync(reply).ConfigureAwait(false);
        });
    }

    /// <summary>注册同步处理器（返回回复 Item）。</summary>
    public SecsEventDispatcher On(byte s, byte f, Func<SecsMsgEventArgs, SecsItem?> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return On(s, f, args => Task.FromResult(handler(args)));
    }

    /// <summary>注册只消费不回复的处理器（用于 W-Bit=false 的通知类消息）。</summary>
    public SecsEventDispatcher OnNotify(byte s, byte f, Action<SecsMsgEventArgs> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        return On(s, f, args =>
        {
            handler(args);
            return Task.CompletedTask;
        });
    }

    public bool Remove(byte s, byte f) => _handlers.TryRemove(MakeKey(s, f), out _);

    public bool IsRegistered(byte s, byte f) => _handlers.ContainsKey(MakeKey(s, f));

    // ---------------- 分发 ----------------

    /// <summary>把消息路由到对应处理器。直接赋给 <c>connection.MessageHandler</c>。</summary>
    public async Task DispatchAsync(SecsMsgEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        SecsMsg message = args.Message;
        int key = MakeKey(message.S, message.F);

        if (_handlers.TryGetValue(key, out Func<SecsMsgEventArgs, Task>? handler))
        {
            await handler(args).ConfigureAwait(false);

            // 处理器忘了回复且对端在等：兜底回 SxF(y+1) 空消息，保护对端不被 T3 拖死
            if (args.ReplyExpected && !args.IsReplied)
            {
                _logger.Warn($"S{message.S}F{message.F} 处理器未回复，自动回复空 Secondary。");
                await args.ReplyAsync(message).ConfigureAwait(false);
            }

            return;
        }

        await HandleUnregisteredAsync(args).ConfigureAwait(false);
    }

    private async Task HandleUnregisteredAsync(SecsMsgEventArgs args)
    {
        SecsMsg message = args.Message;

        if (FallbackHandler is { } fallback)
        {
            await fallback(args).ConfigureAwait(false);

            if (args.ReplyExpected && !args.IsReplied && AutoAbortUnhandled)
                await args.ReplyAbortAsync().ConfigureAwait(false);

            return;
        }

        _logger.Warn($"收到未注册消息 {message}（SysBytes=0x{args.SystemBytes:X8}）。");

        if (args.ReplyExpected && AutoAbortUnhandled && !args.IsReplied)
            await args.ReplyAbortAsync().ConfigureAwait(false);

        if (ReportS9OnUnhandled && StreamFunctionErrorSender is { } sender)
        {
            try
            {
                // S9F5 Unrecognized Function Type，Body 为被拒消息的 10 字节头（此处仅回填 S/F 语义）
                var s9f5 = new SecsMsg(9, 5, false,
                    Item.B((byte)message.S, message.F),
                    "UnrecognizedFunctionType");

                await sender(s9f5, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn($"发送 S9F5 失败：{ex.Message}", ex);
            }
        }
    }

    private static int MakeKey(byte s, byte f) => (s << 8) | f;

    public override string ToString() => $"SecsEventDispatcher[已注册 {_handlers.Count} 条]";
}
