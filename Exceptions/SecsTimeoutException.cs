using MateoSECS.Hsms;

namespace MateoSECS.Exceptions;

public class SecsTimeoutException : SecsException
{
    public SecsTimeoutException(HsmsTimerType timerType, TimeSpan timeout, string message)
        : base(message)
    {
        TimerType = timerType;
        Timeout = timeout;
    }

    public HsmsTimerType TimerType { get; }

    public TimeSpan Timeout { get; }

    /// <summary>是否为业务级超时（T3）。true 时一般只需重试该事务，不必重连。</summary>
    public bool IsTransactionTimeout => TimerType == HsmsTimerType.T3;

    public override string ToString() => $"{TimerType} 超时（{Timeout.TotalSeconds}s）：{Message}";
}
