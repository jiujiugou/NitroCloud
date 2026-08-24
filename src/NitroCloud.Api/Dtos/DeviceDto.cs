namespace NitroCloud.Api.Dtos;

/// <summary>
/// 设备 DTO（前端 web/src/api/types.ts 的 Device 对齐）。
/// status 为在线状态（Online/Offline/Error/Unknown，ADR-007 由最后上报时间 + 阈值计算）。
/// </summary>
public sealed record DeviceDto
{
    /// <summary>设备 ID（Guid 字符串）</summary>
    public required string Id { get; init; }

    /// <summary>所属站点</summary>
    public required string SiteId { get; init; }

    /// <summary>设备显示名</summary>
    public required string Name { get; init; }

    /// <summary>设备型号</summary>
    public string Model { get; init; } = "";

    /// <summary>在线状态：Online/Offline/Error/Unknown</summary>
    public required string Status { get; init; }

    /// <summary>最近上报时间（UTC，O 格式）；从未上报为 null</summary>
    public string? LastSeenAt { get; init; }
}

/// <summary>设备创建/更新请求体</summary>
public sealed record DeviceRequestDto
{
    /// <summary>设备 ID（可选；创建时不传由服务端生成）</summary>
    public string? Id { get; init; }

    /// <summary>所属站点（必填）</summary>
    public string? SiteId { get; init; }

    /// <summary>设备显示名（必填）</summary>
    public string? Name { get; init; }

    /// <summary>设备型号（可选）</summary>
    public string? Model { get; init; }
}
