namespace MateoSECS.Hsms;

public sealed class HsmsTimeouts
{
    /// <summary>T3 Reply Timeout，默认 45s。</summary>
    public TimeSpan T3 { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>T5 Connect Separation Timeout，默认 10s。</summary>
    public TimeSpan T5 { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>T6 Control Transaction Timeout，默认 5s。</summary>
    public TimeSpan T6 { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>T7 NOT SELECTED Timeout，默认 10s。</summary>
    public TimeSpan T7 { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>T8 Network Intercharacter Timeout，默认 5s。</summary>
    public TimeSpan T8 { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Linktest 发送周期，默认 30s；设为 <see cref="TimeSpan.Zero"/> 表示关闭。</summary>
    public TimeSpan LinktestInterval { get; init; } = TimeSpan.FromSeconds(30);

    public static HsmsTimeouts Default { get; } = new();

    public TimeSpan Get(HsmsTimerType type) => type switch
    {
        HsmsTimerType.T3 => T3,
        HsmsTimerType.T5 => T5,
        HsmsTimerType.T6 => T6,
        HsmsTimerType.T7 => T7,
        HsmsTimerType.T8 => T8,
        HsmsTimerType.Linktest => LinktestInterval,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public void Validate()
    {
        Check(T3, nameof(T3));
        Check(T5, nameof(T5));
        Check(T6, nameof(T6));
        Check(T7, nameof(T7));
        Check(T8, nameof(T8));

        if (LinktestInterval < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(LinktestInterval), LinktestInterval, "Linktest cycle must not be negative。");

        static void Check(TimeSpan value, string name)
        {
            if (value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(name, value, $"{name} must be greater than that 0。");
        }
    }

    public override string ToString()
        => $"T3={T3.TotalSeconds}s, T5={T5.TotalSeconds}s, T6={T6.TotalSeconds}s, " +
           $"T7={T7.TotalSeconds}s, T8={T8.TotalSeconds}s, Linktest={LinktestInterval.TotalSeconds}s";
}
