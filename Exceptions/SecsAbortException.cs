namespace MateoSECS.Exceptions;


/// <summary>
/// 对端回复了 SxF0（Abort），表示不支持该 Function 或无法处理。
/// 与 T3 超时区分开：这是「明确拒绝」，重试无意义。
/// </summary>
public class SecsAbortException : SecsException
{
    public SecsAbortException(byte stream, string message) : base(message)
    {
        Stream = stream;
    }

    public byte Stream { get; }
    public byte Function { get; }
}
