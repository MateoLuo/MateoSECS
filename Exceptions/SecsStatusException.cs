using MateoSECS.Hsms;

namespace MateoSECS.Exceptions;

public class SecsStatusException : SecsException
{
    public SecsStatusException(string message) : base(message)
    {
    }

    public SecsStatusException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public HsmsConnectionStatus? CurrentState { get; init; }
}
