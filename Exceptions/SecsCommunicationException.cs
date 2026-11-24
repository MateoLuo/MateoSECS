namespace MateoSECS.Exceptions;

public class SecsCommunicationException : SecsException
{
    public SecsCommunicationException(string message) : base(message)
    {
    }

    public SecsCommunicationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
