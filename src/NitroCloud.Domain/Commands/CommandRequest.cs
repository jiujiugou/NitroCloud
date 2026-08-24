namespace NitroCloud.Domain.Commands;

/// <summary>
/// 下行命令发布载荷（云 → 网关，commands topic；DESIGN.md §4.3）。
/// JSON camelCase 序列化发布；网关补订阅处理器后按此契约执行写值。
/// </summary>
public sealed record CommandRequest
{
    /// <summary>命令唯一标识（幂等键）</summary>
    public required Guid CommandId { get; init; }

    /// <summary>命令类型</summary>
    public string Type { get; init; } = "WritePoint";

    /// <summary>目标点位 ID</summary>
    public required string PointId { get; init; }

    /// <summary>写入值</summary>
    public double Value { get; init; }

    /// <summary>发起时间（UTC）</summary>
    public DateTime RequestedAt { get; init; } = DateTime.UtcNow;
}
