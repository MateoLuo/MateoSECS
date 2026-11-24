namespace MateoSECS.Hsms;

public static class HsmsControlMsg
{
    public static HsmsMsg SelectRequest(ushort sessionId, uint systemBytes)
    => HsmsMsg.CreateControl(HsmsSType.SelectReq, systemBytes, sessionId);
    public static HsmsMsg SelectResponse(ushort sessionId, uint systemBytes, HsmsSelectStatus status)
    => HsmsMsg.CreateControl(HsmsSType.SelectRsp, systemBytes, sessionId, 0, (byte)status);
    public static HsmsMsg DeselectRequest(ushort sessionId, uint systemBytes)
    => HsmsMsg.CreateControl(HsmsSType.DeselectReq, systemBytes, sessionId);
    public static HsmsMsg DeselectResponse(ushort sessionId, uint systemBytes, byte status = 0)
    => HsmsMsg.CreateControl(HsmsSType.DeselectRsp, systemBytes, sessionId, 0, status);
    public static HsmsMsg LinktestRequest(uint systemBytes)
    => HsmsMsg.CreateControl(HsmsSType.LinktestReq, systemBytes);
    public static HsmsMsg LinktestResponse(uint systemBytes)
    => HsmsMsg.CreateControl(HsmsSType.LinktestRsp, systemBytes);
    public static HsmsMsg RejectRequest(ushort sessionId, uint systemBytes, byte rejectedType, HsmsRejectReason reason)
    => HsmsMsg.CreateControl(HsmsSType.RejectReq, systemBytes, sessionId, rejectedType, (byte)reason);
    public static HsmsMsg RejectFor(HsmsMsg offending, HsmsRejectReason reason)
    {
        ArgumentNullException.ThrowIfNull(offending);

        byte rejectedType = reason == HsmsRejectReason.PTypeNotSupported
            ? (byte)offending.Header.PType
            : (byte)offending.Header.SType;

        return RejectRequest(offending.Header.SessionId, offending.Header.SystemBytes, rejectedType, reason);
    }
    public static HsmsMsg SeparateRequest(ushort sessionId, uint systemBytes)
    => HsmsMsg.CreateControl(HsmsSType.SeparateReq, systemBytes, sessionId);
    public static bool RequiresResponse(HsmsSType sType) => sType is
        HsmsSType.SelectReq or HsmsSType.DeselectReq or HsmsSType.LinktestReq;
    public static HsmsSType GetResponseType(HsmsSType requestType) => requestType switch
    {
        HsmsSType.SelectReq => HsmsSType.SelectRsp,
        HsmsSType.DeselectReq => HsmsSType.DeselectRsp,
        HsmsSType.LinktestReq => HsmsSType.LinktestRsp,
        _ => throw new ArgumentOutOfRangeException(nameof(requestType), requestType, "该控制消息不需要响应。"),
    };
    public static bool IsResponse(HsmsSType sType) => sType is
        HsmsSType.SelectRsp or HsmsSType.DeselectRsp or HsmsSType.LinktestRsp;
    public static bool IsSupported(HsmsSType sType) => sType is
        HsmsSType.DataMessage or
        HsmsSType.SelectReq or HsmsSType.SelectRsp or
        HsmsSType.DeselectReq or HsmsSType.DeselectRsp or
        HsmsSType.LinktestReq or HsmsSType.LinktestRsp or
        HsmsSType.RejectReq or HsmsSType.SeparateReq;
    public static string Describe(HsmsMsg message)
    {
        ArgumentNullException.ThrowIfNull(message);

        HsmsHeader h = message.Header;
        return h.SType switch
        {
            HsmsSType.SelectReq => $"Select.req    (SessionId=0x{h.SessionId:X4})",
            HsmsSType.SelectRsp => $"Select.rsp    (Status={h.SelectStatus})",
            HsmsSType.DeselectReq => "Deselect.req",
            HsmsSType.DeselectRsp => $"Deselect.rsp  (Status={h.Byte3})",
            HsmsSType.LinktestReq => "Linktest.req",
            HsmsSType.LinktestRsp => "Linktest.rsp",
            HsmsSType.RejectReq => $"Reject.req    (Reason={h.RejectReason}, Type=0x{h.Byte2:X2})",
            HsmsSType.SeparateReq => "Separate.req",
            HsmsSType.DataMessage => $"S{h.Stream}F{h.Function}{(h.WBit ? " W" : string.Empty)}",
            _ => $"Unknown SType=0x{(byte)h.SType:X2}",
        };
    }
}
