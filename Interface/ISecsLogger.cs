namespace MateoSECS.Interface;

public enum SecsLogLevel
{
    Trace = 0,
    Debug = 1,
    Info = 2,
    Warning = 3,
    Error = 4,
}
public interface ISecsLogger
{
    bool IsEnabled(SecsLogLevel level);

    void Log(SecsLogLevel level, string message, Exception? exception = null);
}
public sealed class NullSecsLogger : ISecsLogger
{
    public static readonly NullSecsLogger Instance = new();

    private NullSecsLogger()
    {
    }

    public bool IsEnabled(SecsLogLevel level) => false;

    public void Log(SecsLogLevel level, string message, Exception? exception = null)
    {
    }
}
public static class SecsLoggerExtensions
{
    public static void Trace(this ISecsLogger logger, string message) => logger.Log(SecsLogLevel.Trace, message);

    public static void Debug(this ISecsLogger logger, string message) => logger.Log(SecsLogLevel.Debug, message);

    public static void Info(this ISecsLogger logger, string message) => logger.Log(SecsLogLevel.Info, message);

    public static void Warn(this ISecsLogger logger, string message, Exception? ex = null)
        => logger.Log(SecsLogLevel.Warning, message, ex);

    public static void Error(this ISecsLogger logger, string message, Exception? ex = null)
        => logger.Log(SecsLogLevel.Error, message, ex);
}
