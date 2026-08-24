namespace NitroCloud.Domain.Commands;

/// <summary>
/// 命令回执（网关 → 云，commands/ack 载荷）。
/// result/error 必填（ADR-003 载荷墙）；CommandManager 收执后更新命令状态并推送 OnCommandAck。
/// </summary>
public sealed record CommandAck
{
    /// <summary>对应命令 ID（网关按此去重）</summary>
    public required Guid CommandId { get; init; }

    /// <summary>执行结果</summary>
    public CommandResult Result { get; init; }

    /// <summary>失败原因（Success 时为空）</summary>
    public string Error { get; init; } = "";

    /// <summary>回执时间（UTC）</summary>
    public DateTime At { get; init; }
}
