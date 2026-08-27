using NitroCloud.Domain.Commands;

namespace NitroCloud.Storage;

/// <summary>
/// 命令存储接口（ADR-010 D2，接口只增不删）。SQLite 实现（命令落库：审计 + 查询 + 状态机持久化）。
/// 状态机 Pending → Sent → Acked/Failed/Timeout，终态（Acked/Failed/Timeout）不覆盖。
/// </summary>
public interface ICommandStore
{
    /// <summary>新增一条命令（初始 Pending；commandId 为幂等键，重试不更换）</summary>
    Task AddAsync(CommandRecord cmd, CancellationToken ct = default);

    /// <summary>
    /// 更新命令状态（终态不覆盖：已是终态则直接返回）。
    /// attempts / sentAt / ackedAt 为可选字段，仅在调用方显式传入时更新（默认 null = 不修改）。
    /// </summary>
    Task UpdateStatusAsync(
        Guid commandId,
        CommandStatus status,
        string? error = null,
        int? attempts = null,
        DateTime? sentAt = null,
        DateTime? ackedAt = null,
        CancellationToken ct = default);

    /// <summary>按命令 ID 查询（不存在返回 null）</summary>
    Task<CommandRecord?> GetAsync(Guid commandId, CancellationToken ct = default);

    /// <summary>查询在途命令（Status = Pending 或 Sent，供后台超时扫描）</summary>
    Task<IReadOnlyList<CommandRecord>> QueryInFlightAsync(CancellationToken ct = default);
}
