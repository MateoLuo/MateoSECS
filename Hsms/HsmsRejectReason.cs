namespace MateoSECS.Hsms;

public enum HsmsRejectReason : byte
{
    /// <summary>SType 不被支持。</summary>
    STypeNotSupported = 1,
    /// <summary>PType 不被支持。</summary>
    PTypeNotSupported = 2,
    /// <summary>当前状态不允许该事务。</summary>
    TransactionNotOpen = 3,
    /// <summary>实体未处于 SELECTED 状态。</summary>
    EntityNotSelected = 4,
}
