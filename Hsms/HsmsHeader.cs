using System.Buffers.Binary;
using System.Drawing;

namespace MateoSECS.Hsms;

public readonly struct HsmsHeader : IEquatable<HsmsHeader>
{
    public const int Size = 10;
    /// <summary>
    /// Head 0-1 2 bytes
    /// </summary>
    public ushort SessionId { get; init; }   
    /// <summary>控制消息使用的保留 SessionID。</summary>
    public const ushort ControlSessionId = 0xFFFF;

    public byte Byte2 { get; init; }
    public byte Byte3 { get; init; }
    public HsmsPType PType { get; init; }
    public HsmsSType SType { get; init; }
    /// <summary>
    /// Secs transaction
    /// </summary>
    public uint SystemBytes { get; init; }

    public uint DeviceId => SessionId;
    public bool IsDataMsg => SType == HsmsSType.DataMessage;
    public byte Stream => (byte)(Byte2 & 0x7F);
    public byte Function => Byte3;
    public bool WBit => (Byte2 & 0x80) != 0;
    public bool IsPrimary => IsDataMsg && (Function & 0x01) == 1;
    public HsmsSelectStatus SelectStatus => (HsmsSelectStatus)Byte3;
    public HsmsRejectReason RejectReason => (HsmsRejectReason)Byte3;
    #region Overrides
    public bool Equals(HsmsHeader other)
    =>
    SessionId == other.SessionId &&
    Byte2 == other.Byte2 &&
    Byte3 == other.Byte3 &&
    PType == other.PType &&
    SType == other.SType &&
    SystemBytes == other.SystemBytes;
    public override bool Equals(object? obj) => obj is HsmsHeader other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(SessionId, Byte2, Byte3, PType, SType, SystemBytes);

    public static bool operator ==(HsmsHeader left, HsmsHeader right) => left.Equals(right);

    public static bool operator !=(HsmsHeader left, HsmsHeader right) => !left.Equals(right);

    public override string ToString()
    {
        return IsDataMsg
            ? $"[Data] S{Stream}F{Function}{(WBit ? " W" : string.Empty)} SessionId=0x{SessionId:X4} SysBytes=0x{SystemBytes:X8}"
            : $"[{SType}] SessionId=0x{SessionId:X4} B2=0x{Byte2:X2} B3=0x{Byte3:X2} SysBytes=0x{SystemBytes:X8}";
    }
    #endregion
    #region Encode/Decode
    /// <summary>写入 10 字节头到目标缓冲区（大端）。</summary>
    public void Encode(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"HSMS head part for need {Size} byte buffer。", nameof(destination));

        BinaryPrimitives.WriteUInt16BigEndian(destination, SessionId);
        destination[2] = Byte2;
        destination[3] = Byte3;
        destination[4] = (byte)PType;
        destination[5] = (byte)SType;
        BinaryPrimitives.WriteUInt32BigEndian(destination[6..], SystemBytes);
    }
    /// <summary>返回 10 字节头的新数组。</summary>
    public byte[] ToArray()
    {
        var buffer = new byte[Size];
        Encode(buffer);
        return buffer;
    }
    /// <summary>从 10 字节缓冲区解析头。</summary>
    public static HsmsHeader Decode(ReadOnlySpan<byte> source)
    {
        if (source.Length < Size)
            throw new ArgumentException($"HSMS head part for need  {Size} data，Actually {source.Length}。", nameof(source));

        return new HsmsHeader
        {
            SessionId = BinaryPrimitives.ReadUInt16BigEndian(source),
            Byte2 = source[2],
            Byte3 = source[3],
            PType = (HsmsPType)source[4],
            SType = (HsmsSType)source[5],
            SystemBytes = BinaryPrimitives.ReadUInt32BigEndian(source[6..]),
        };
    }
    #endregion
    #region Factory Func
    /// <summary>构造一个 SECS-II 数据消息头。</summary>
    public static HsmsHeader CreateData(ushort sessionId, byte stream, byte function, bool waitBit, uint systemBytes)
    {
        if (stream > 0x7F)
            throw new ArgumentOutOfRangeException(nameof(stream), stream, "Stream 必须位于 0..127。");

        return new HsmsHeader
        {
            SessionId = sessionId,
            Byte2 = (byte)((waitBit ? 0x80 : 0x00) | stream),
            Byte3 = function,
            PType = HsmsPType.SecsII,
            SType = HsmsSType.DataMessage,
            SystemBytes = systemBytes,
        };
    }

    /// <summary>构造一个 HSMS 控制消息头。</summary>
    public static HsmsHeader CreateControl(
        HsmsSType sType,
        uint systemBytes,
        ushort sessionId = ControlSessionId,
        byte byte2 = 0,
        byte byte3 = 0)
    {
        return new HsmsHeader
        {
            SessionId = sessionId,
            Byte2 = byte2,
            Byte3 = byte3,
            PType = HsmsPType.SecsII,
            SType = sType,
            SystemBytes = systemBytes,
        };
    }

    /// <summary>由当前 Primary 头派生出 Secondary（回复）头：SystemBytes 保持一致，W-Bit 清零。</summary>
    public HsmsHeader CreateReplyHeader(byte function)
    {
        return new HsmsHeader
        {
            SessionId = SessionId,
            Byte2 = (byte)(Byte2 & 0x7F),
            Byte3 = function,
            PType = PType,
            SType = SType,
            SystemBytes = SystemBytes,
        };
    }

    #endregion
}
