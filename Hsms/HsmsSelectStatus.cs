namespace MateoSECS.Hsms;

public enum HsmsSelectStatus : byte
{
    Success = 0,
    AlreadyActive = 1,
    NotReady = 2,
    ConnectExhaust = 3,
}
