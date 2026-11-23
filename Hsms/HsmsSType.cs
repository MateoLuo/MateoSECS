namespace MateoSECS.Hsms;

public enum HsmsSType : byte
{
    /// <summary>SECS-II 数据消息（SxFy）。</summary>
    DataMessage = 0,
    SelectReq = 1,
    SelectRsp = 2,
    DeselectReq = 3,
    DeselectRsp = 4,
    LinktestReq = 5,
    LinktestRsp = 6,
    RejectReq = 7,
    SeparateReq = 9,
}
