namespace NitroCloud.Api.Dtos;

/// <summary>
/// 云端异步写值请求体（前端 web/src/api/types.ts 的 WriteValueRequest 对齐）。
/// commandId 由服务端生成（幂等键，重试不换，ADR-008 D6）。
/// </summary>
public sealed record WriteValueRequestDto
{
    /// <summary>目标站点</summary>
    public string? SiteId { get; init; }

    /// <summary>目标设备</summary>
    public string? DeviceId { get; init; }

    /// <summary>目标点位</summary>
    public string? PointId { get; init; }

    /// <summary>写入值（WritePoint 为数值型）</summary>
    public double Value { get; init; }
}

/// <summary>写值响应（commandId + 初始状态 + 发起时间）</summary>
public sealed record WriteValueResponseDto
{
    /// <summary>命令 ID（幂等键）</summary>
    public required string CommandId { get; init; }

    /// <summary>初始状态（Pending）</summary>
    public required string Status { get; init; }

    /// <summary>发起时间（UTC，O 格式）</summary>
    public required string RequestedAt { get; init; }
}

/// <summary>
/// 命令记录 DTO（前端 web/src/api/types.ts 的 CommandRecord 对齐）。
/// 前端用 id 而非 commandId，故由领域 CommandRecord 映射。
/// </summary>
public sealed record CommandRecordDto
{
    /// <summary>命令 ID（幂等键）</summary>
    public required string Id { get; init; }

    /// <summary>所属站点</summary>
    public required string SiteId { get; init; }

    /// <summary>目标设备</summary>
    public required string DeviceId { get; init; }

    /// <summary>目标点位</summary>
    public required string PointId { get; init; }

    /// <summary>命令类型</summary>
    public required string Type { get; init; }

    /// <summary>写入值</summary>
    public double Value { get; init; }

    /// <summary>当前状态</summary>
    public required string Status { get; init; }

    /// <summary>发起时间（UTC，O 格式）</summary>
    public required string RequestedAt { get; init; }

    /// <summary>收到回执时间（UTC，O 格式）；未回执为 null</summary>
    public string? AckedAt { get; init; }

    /// <summary>最近失败/超时原因</summary>
    public string? Error { get; init; }
}
