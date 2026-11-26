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
public sealed class ConsoleSecsLogger : ISecsLogger
{
    private static readonly object SyncRoot = new();

    private readonly string _prefix;

    public ConsoleSecsLogger(string prefix = "SECS", SecsLogLevel minimumLevel = SecsLogLevel.Debug)
    {
        _prefix = prefix;
        MinimumLevel = minimumLevel;
    }

    public SecsLogLevel MinimumLevel { get; set; }

    public bool IsEnabled(SecsLogLevel level) => level >= MinimumLevel;

    public void Log(SecsLogLevel level, string message, Exception? exception = null)
    {
        if (!IsEnabled(level))
            return;

        lock (SyncRoot)
        {
            ConsoleColor original = Console.ForegroundColor;
            Console.ForegroundColor = level switch
            {
                SecsLogLevel.Trace => ConsoleColor.DarkGray,
                SecsLogLevel.Debug => ConsoleColor.Gray,
                SecsLogLevel.Info => ConsoleColor.White,
                SecsLogLevel.Warning => ConsoleColor.Yellow,
                SecsLogLevel.Error => ConsoleColor.Red,
                _ => original,
            };

            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{level,-7}] [{_prefix}] {message}");
            if (exception is not null)
                Console.WriteLine(exception);

            Console.ForegroundColor = original;
        }
    }
}
