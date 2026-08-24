namespace NitroCloud.Api.Dtos;

/// <summary>
/// 站点 DTO（前端 web/src/api/types.ts 的 Site 对齐）。
/// status 为在线状态（Online/Offline/Unknown/Maintenance，ADR-007 由「最后上报时间 + 阈值」计算），
/// 非元数据里的运营状态（Active/Disabled）。
/// </summary>
public sealed record SiteDto
{
    /// <summary>站点 ID（= 上行 topic 第三段 siteId）</summary>
    public required string Id { get; init; }

    /// <summary>站点显示名</summary>
    public required string Name { get; init; }

    /// <summary>站点位置描述</summary>
    public string Location { get; init; } = "";

    /// <summary>在线状态：Online/Offline/Unknown/Maintenance</summary>
    public required string Status { get; init; }

    /// <summary>最近上报时间（UTC，O 格式）；从未上报为 null</summary>
    public string? LastReportAt { get; init; }

    /// <summary>创建时间（UTC，O 格式）</summary>
    public required string CreatedAt { get; init; }
}

/// <summary>站点创建/更新请求体</summary>
public sealed record SiteRequestDto
{
    /// <summary>站点 ID（可选；创建时不传由服务端生成）</summary>
    public string? Id { get; init; }

    /// <summary>站点显示名（必填）</summary>
    public string? Name { get; init; }

    /// <summary>站点位置描述（可选）</summary>
    public string? Location { get; init; }

    /// <summary>运营状态（Active/Disabled，可选，默认 Active）</summary>
    public string? Status { get; init; }
}
