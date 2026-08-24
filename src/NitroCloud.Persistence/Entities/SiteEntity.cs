namespace NitroCloud.Persistence.Entities;

/// <summary>sites 表实体（EF Core 映射到 <see cref="Domain.Sites.Site"/>）</summary>
public sealed class SiteEntity
{
    /// <summary>站点 ID（= 上行 topic 第三段 siteId）</summary>
    public string Id { get; set; } = "";

    /// <summary>站点显示名</summary>
    public string Name { get; set; } = "";

    /// <summary>站点位置</summary>
    public string Location { get; set; } = "";

    /// <summary>状态（Active/Disabled）</summary>
    public string Status { get; set; } = "Active";

    /// <summary>创建时间（O 格式 UTC 字符串，字典序即时间序）</summary>
    public string CreatedAt { get; set; } = "";
}
