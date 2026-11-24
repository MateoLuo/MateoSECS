namespace MateoSECS.Hsms;

public enum HsmsTimerType
{
    /// <summary>T3：Reply Timeout，等待 SxFy 回复。</summary>
    T3,

    /// <summary>T5：Connect Separation Timeout，两次连接尝试之间的最小间隔。</summary>
    T5,

    /// <summary>T6：Control Transaction Timeout，控制消息（Select/Linktest…）事务超时。</summary>
    T6,

    /// <summary>T7：NOT SELECTED Timeout，TCP 建立后未完成 Select 的最长等待。</summary>
    T7,

    /// <summary>T8：Network Intercharacter Timeout，单条消息内字节间隔超时。</summary>
    T8,

    /// <summary>Linktest 周期（非标准计时器，属于配置项）。</summary>
    Linktest,
}
