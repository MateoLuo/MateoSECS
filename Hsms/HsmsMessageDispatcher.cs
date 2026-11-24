using MateoSECS.Core;
using MateoSECS.Exceptions;
using MateoSECS.Interface;
using MateoSECS.Secs;

namespace MateoSECS.Hsms;

internal sealed class HsmsMsgDispatcher
{
    private readonly HsmsOptions _options;
    private readonly ISecsLogger _logger;
    private readonly HsmsSessionStatus _status;
    private readonly Func<HsmsMsg, CancellationToken, Task> _sendAsync;
    private readonly HsmsControlTransactionManager _controlTx;
    private readonly ReplyManager _replyManager;
    private readonly Action<SecsMsg, uint> _dispatchPrimary;
    private readonly Action<Exception> _raiseError;
    private readonly Func<Exception?, Task> _closeAsync;

    public HsmsMsgDispatcher(
        HsmsOptions options,
        ISecsLogger logger,
        HsmsSessionStatus Status,
        Func<HsmsMsg, CancellationToken, Task> sendAsync,
        HsmsControlTransactionManager controlTx,
        ReplyManager replyManager,
        Action<SecsMsg, uint> dispatchPrimary,
        Action<Exception> raiseError,
        Func<Exception?, Task> closeAsync)
    {
        _options = options;
        _logger = logger ?? NullSecsLogger.Instance;
        _status = Status;
        _sendAsync = sendAsync;
        _controlTx = controlTx;
        _replyManager = replyManager;
        _dispatchPrimary = dispatchPrimary;
        _raiseError = raiseError;
        _closeAsync = closeAsync;
    }

    public async Task DispatchAsync(HsmsMsg Msg, CancellationToken token)
    {
        switch (Msg.Header.SType)
        {
            case HsmsSType.DataMessage:
                await HandleDataMsgAsync(Msg, token).ConfigureAwait(false);
                break;

            case HsmsSType.SelectReq:
                await HandleSelectRequestAsync(Msg, token).ConfigureAwait(false);
                break;

            case HsmsSType.DeselectReq:
                await _sendAsync(
                    HsmsControlMsg.DeselectResponse(Msg.Header.SessionId, Msg.SystemBytes),
                    token).ConfigureAwait(false);
                _status.TransitionTo(HsmsConnectionStatus.NotSelected);
                break;

            case HsmsSType.LinktestReq:
                await _sendAsync(HsmsControlMsg.LinktestResponse(Msg.SystemBytes), token).ConfigureAwait(false);
                break;

            case HsmsSType.SelectRsp:
            case HsmsSType.DeselectRsp:
            case HsmsSType.LinktestRsp:
                CompleteControl(Msg);
                break;

            case HsmsSType.RejectReq:
                HandleReject(Msg);
                break;

            case HsmsSType.SeparateReq:
                _logger.Info($"[{_options.Name}] 收到 Separate.req，断开会话。");
                await _closeAsync(null).ConfigureAwait(false);
                break;

            default:
                _logger.Warn($"[{_options.Name}] 不支持的 SType=0x{(byte)Msg.Header.SType:X2}，回 Reject.req。");
                await _sendAsync(
                    HsmsControlMsg.RejectFor(Msg, HsmsRejectReason.STypeNotSupported),
                    token).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleSelectRequestAsync(HsmsMsg Msg, CancellationToken token)
    {
        HsmsSelectStatus status = _status.IsSelected ? HsmsSelectStatus.AlreadyActive : HsmsSelectStatus.Success;

        await _sendAsync(
            HsmsControlMsg.SelectResponse(Msg.Header.SessionId, Msg.SystemBytes, status),
            token).ConfigureAwait(false);

        if (status == HsmsSelectStatus.Success)
            _status.TransitionTo(HsmsConnectionStatus.Selected);   // 内部会停 T7
    }

    private async Task HandleDataMsgAsync(HsmsMsg Msg, CancellationToken token)
    {
        if (!_status.IsSelected)
        {
            _logger.Warn($"[{_options.Name}] 未 Selected 状态收到 {Msg}，回 Reject.req。");
            await _sendAsync(
                HsmsControlMsg.RejectFor(Msg, HsmsRejectReason.EntityNotSelected),
                token).ConfigureAwait(false);
            return;
        }

        SecsMsg secs;
        try { secs = Msg.ToSecsMsg(); }
        catch (SecsDecodeException ex)
        {
            _logger.Error($"[{_options.Name}] SECS-II 消息体解析失败：{ex}", ex);
            _raiseError(ex);

            if (Msg.Header.WBit)
            {
                var abort = new SecsMsg(Msg.Header.Stream, 0);
                var frame = HsmsMsg.CreateDataMsg(_options.SessionId, abort, Msg.SystemBytes);
                await _sendAsync(frame, token).ConfigureAwait(false);
            }
            return;
        }

        if (secs.IsSecondary)
        {
            if (!_replyManager.TryComplete(secs, Msg.SystemBytes))
                _logger.Warn($"[{_options.Name}] 收到无主回复 {secs}（SysBytes=0x{Msg.SystemBytes:X8}），可能已 T3 超时，丢弃。");
            return;
        }

        _dispatchPrimary(secs, Msg.SystemBytes);
    }

    private void CompleteControl(HsmsMsg Msg)
    {
        if (!_controlTx.TryComplete(Msg))
            _logger.Warn($"[{_options.Name}] 收到无主控制响应 {HsmsControlMsg.Describe(Msg)}（SysBytes=0x{Msg.SystemBytes:X8}）。");
    }

    private void HandleReject(HsmsMsg Msg)
    {
        HsmsRejectReason reason = Msg.Header.RejectReason;
        var ex = new SecsRejectException(reason,
            $"[{_options.Name}] 对端 Reject.req：{reason}（Type=0x{Msg.Header.Byte2:X2}）。");
        _logger.Warn(ex.Message);

        if (!_controlTx.TryFail(Msg.SystemBytes, ex))
            _replyManager.TryFail(Msg.SystemBytes, ex);

        _raiseError(ex);
    }
}
