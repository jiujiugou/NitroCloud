namespace NitroCloud.Domain.Commands;

/// <summary>
/// 命令状态机（ADR-008 D6）：Pending → Sent → Acked/Failed/Timeout。
/// 重试不换 commandId（网关侧按 commandId 去重），超限标记 Timeout。
/// </summary>
public enum CommandStatus
{
    /// <summary>已创建，待发布（网关离线时命令保留 Pending，可人工重发）</summary>
    Pending,

    /// <summary>已发布到 commands topic，等待回执</summary>
    Sent,

    /// <summary>已收到网关回执 Success</summary>
    Acked,

    /// <summary>已收到网关回执 Failure（携带错误信息）</summary>
    Failed,

    /// <summary>重试超上限仍未收到回执</summary>
    Timeout
}
