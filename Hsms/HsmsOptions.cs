using System.Net;

namespace MateoSECS.Hsms;

public sealed class HsmsOptions
{
    /// <summary>连接名，用于日志区分（如 "EQP-BOND-01"）。</summary>
    public string Name { get; init; } = "Host001";

    /// <summary>对端 IP（Active）或监听地址（Passive）。</summary>
    public string IpAddress { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 5000;

    /// <summary>SessionID / DeviceID。</summary>
    public ushort SessionId { get; init; }

    public HsmsConnectionMode Mode { get; init; } = HsmsConnectionMode.Active;

    public HsmsTimeouts Timeouts { get; init; } = HsmsTimeouts.Default;

    /// <summary>断线后是否自动重连（仅 Active 有效）。</summary>
    public bool AutoReconnect { get; init; } = true;

    /// <summary>是否启用周期 Linktest。</summary>
    public bool EnableLinktest { get; init; } = true;

    /// <summary>允许接收的最大单条消息长度。</summary>
    public int MaxMessageLength { get; init; } = HsmsMsg.DefaultMaxMessageLength;

    /// <summary>是否在日志中输出完整 SML（量大时可关闭）。</summary>
    public bool LogSml { get; init; } = true;

    public IPEndPoint ToEndPoint() => new(IPAddress.Parse(IpAddress), Port);

    public override string ToString() => $"{Name}[{IpAddress}:{Port}, {Mode}, SessionId=0x{SessionId:X4}]";
}
