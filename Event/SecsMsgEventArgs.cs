using MateoSECS.Hsms;
using MateoSECS.Secs;

namespace MateoSECS.Event;

public sealed class SecsMsgEventArgs : System.EventArgs
{
    private readonly HsmsConnection _connection;
    private byte _replied; 
    public bool IsReplied => Volatile.Read(ref _replied) != 0;

    public SecsMsg Message { get; }
    public uint SystemBytes { get; }
    public bool ReplyExpected => Message.Wbit;

    internal SecsMsgEventArgs(HsmsConnection connection, SecsMsg msg, uint systemBytes)
    {
        _connection = connection;
        Message = msg;
        SystemBytes = systemBytes;
    }
    public async Task ReplyAsync(SecsMsg reply, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reply);

        if (Interlocked.Exchange(ref _replied, 1) != 0)
            throw new InvalidOperationException($"S{Message.S}F{Message.F} (SysBytes=0x{SystemBytes:X8}) Already replied。");
        await _connection.SendReplyAsync(reply, SystemBytes, cancellationToken);
    }
    public async Task ReplyAsync(SecsItem? item, CancellationToken cancellationToken = default)
        => await ReplyAsync(Message.CreateReply(item), cancellationToken);
    public async Task ReplyAbortAsync(CancellationToken cancellationToken = default)
        => await ReplyAsync(Message.CreateAbortReply(), cancellationToken);
}
