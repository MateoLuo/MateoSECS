using Mateo.SECS;
using MateoSECS.Secs;
using System.Buffers;
using System.Buffers.Binary;

namespace MateoSECS.Hsms;

public sealed class HsmsMsg
{
    #region Properties
    public const int LengthPrefixSize = 4;
    /// <summary>
    /// 整个消息长度四字节+Hsms消息10字节
    /// </summary>
    public const int HeadLength = LengthPrefixSize + HsmsHeader.Size;
    public const int MinMessageLength = HsmsHeader.Size;
    /// <summary>
    /// 消息最大8M
    /// </summary>
    public const int DefaultMaxMessageLength = 8 * 1024 * 1024;

    public int WireLength => LengthPrefixSize + HsmsHeader.Size + RawBody.Length;
    public HsmsHeader Header { get; }
    public ReadOnlyMemory<byte> RawBody { get; }
    public bool IsDataMsg => Header.IsDataMsg;

    public bool IsControlMsg => Header.SType != HsmsSType.DataMessage;
    public uint SystemBytes => Header.SystemBytes;
    private SecsItem? _cachedItem;
    private bool _bodyDecoded;
    public SecsItem? SecsItem
    {
        get
        {
            if (_bodyDecoded)
                return _cachedItem;
            _cachedItem = RawBody.Length == 0 ? null : SecsDecoder.Decode(RawBody.Span);
            _bodyDecoded = true;
            return _cachedItem;
        }
    }
    #endregion
    #region Constructor
    public HsmsMsg(HsmsHeader header, ReadOnlyMemory<byte> rawBody = default)
    {
        Header = header;
        RawBody = rawBody;
    }
    public HsmsMsg(HsmsHeader header, ReadOnlyMemory<byte> rawBody, SecsItem? secsItem)
        : this(header, rawBody)
    {
        _cachedItem = secsItem;
    }
    #endregion
    #region Factory Methods
    public static HsmsMsg CreateDataMsg(ushort sessionId, SecsMsg msg, uint systemBytes)
    {
        ArgumentNullException.ThrowIfNull(msg);

        var header = HsmsHeader.CreateData(sessionId, msg.S, msg.F, msg.Wbit, systemBytes);
        ReadOnlyMemory<byte> rawBody = msg.SecsItem is null
            ? ReadOnlyMemory<byte>.Empty
            : msg.SecsItem.Encode();

        return new HsmsMsg(header, rawBody, msg.SecsItem);
    }
    public static HsmsMsg CreateControl(
        HsmsSType sType,
        uint systemBytes,
        ushort sessionId = HsmsHeader.ControlSessionId,
        byte byte2 = 0,
        byte byte3 = 0)
        => new(HsmsHeader.CreateControl(sType, systemBytes, sessionId, byte2, byte3));
    #endregion
    #region Encode/Decode
    public static HsmsMsg DecodePayload(ReadOnlySpan<byte> payload)
    {
        var header = HsmsHeader.Decode(payload);
        ReadOnlyMemory<byte> body = payload.Length > HsmsHeader.Size
            ? payload[HsmsHeader.Size..].ToArray()
            : ReadOnlyMemory<byte>.Empty;

        return new HsmsMsg(header, body);
    }
    public static HsmsMsg DecodeFrame(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < HeadLength)
            throw new ArgumentException("HSMS Frame length insufficient。", nameof(frame));

        uint length = BinaryPrimitives.ReadUInt32BigEndian(frame);
        if (length < MinMessageLength || length > frame.Length - LengthPrefixSize)
            throw new ArgumentException($"HSMS Length field is invalid：{length}。", nameof(frame));

        return DecodePayload(frame.Slice(LengthPrefixSize, (int)length));
    }
    public void EncodeTo(IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        int messageLength = HsmsHeader.Size + RawBody.Length;

        Span<byte> prefixAndHeader = writer.GetSpan(HeadLength);
        BinaryPrimitives.WriteUInt32BigEndian(prefixAndHeader, (uint)messageLength);
        Header.Encode(prefixAndHeader[LengthPrefixSize..]);
        writer.Advance(HeadLength);

        if (RawBody.Length > 0)
            writer.Write(RawBody.Span);
    }
    public byte[] Encode()
    {
        int msgLength = HsmsHeader.Size + RawBody.Length;
        var buffer = new byte[LengthPrefixSize + msgLength];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)msgLength);
        Header.Encode(buffer.AsSpan(LengthPrefixSize));
        if (RawBody.Length > 0)
            RawBody.Span.CopyTo(buffer.AsSpan(HeadLength));
        return buffer;
    }
    #endregion
    #region Overwrite
    public static bool IsValidMessageLength(uint length, int maxMessageLength = DefaultMaxMessageLength)
        => length >= MinMessageLength && length <= (uint)maxMessageLength;
    public override string ToString()
    {
        if (IsControlMsg)
            return Header.ToString();

        return $"S{Header.Stream}F{Header.Function}{(Header.WBit ? " W" : string.Empty)} " +
               $"[SysBytes=0x{Header.SystemBytes:X8}, Body={RawBody.Length}B]";
    }

    public string ToSML()
    {
        if (IsControlMsg)
            return Header.ToString();
        return ToSecsMsg().ToSml();
    }
    #endregion
    #region Methods
    public SecsMsg ToSecsMsg()
    {
        if (!IsDataMsg)
            throw new InvalidOperationException("Only DataMsg can be converted into SECSMsg");
        return new SecsMsg(Header.Stream, Header.Function, Header.WBit, SecsItem);
    }
    #endregion
}
