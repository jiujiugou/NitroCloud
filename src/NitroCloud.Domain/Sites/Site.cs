namespace NitroCloud.Domain.Sites;

/// <summary>
/// 站点（现场）。siteId 是第一隔离维度（ADR-004），来源于上行 topic 第三段，
/// 载荷内 siteId 仅作冗余校验，不一致记告警、不静默丢弃。
/// 站点是低频元数据，落 SQLite。
/// </summary>
public sealed class Site
{
    /// <summary>站点唯一标识（上行 topic 第三段，不可变）</summary>
    public required string Id { get; init; }

    /// <summary>站点显示名</summary>
    public string Name { get; set; } = "";

    /// <summary>站点位置描述（现场地址/说明）</summary>
    public string Location { get; set; } = "";

    /// <summary>站点运行状态</summary>
    public SiteStatus Status { get; set; } = SiteStatus.Active;

    /// <summary>创建时间（UTC）</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>站点运行状态</summary>
public enum SiteStatus
{
    /// <summary>正常运营</summary>
    Active,

    /// <summary>已停用/下线</summary>
    Disabled
}
