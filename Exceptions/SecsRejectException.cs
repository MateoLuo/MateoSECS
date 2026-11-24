using MateoSECS.Hsms;

namespace MateoSECS.Exceptions;

public class SecsRejectException : SecsException
{
    public SecsRejectException(HsmsRejectReason reason, string message) : base(message)
    {
        Reason = reason;
    }

    public HsmsRejectReason Reason { get; }
}
