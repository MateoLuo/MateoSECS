using MateoSECS.Secs;

namespace MateoSECS.Exceptions;

public class SecsException : Exception
{
    public SecsException(string message) : base(message)
    {
    }

    public SecsException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>出错时关联的 SECS 消息（可空）。</summary>
    public SecsMsg? RelatedMessage { get; init; }

    /// <summary>关联事务的 SystemBytes（0 表示无关联）。</summary>
    public uint SystemBytes { get; init; }
}
