namespace NitroCloud.Shared;

/// <summary>
/// 时间工具：云侧统一以 UTC 的 DateTime（Kind=Utc）表示时间，避免跨站点时区歧义。
/// 上行载荷时间戳可能带时区偏移（ISO 8601），入库前一律转 UTC。
/// </summary>
public static class TimeUtil
{
    /// <summary>当前 UTC 时间（Kind=Utc）</summary>
    public static DateTime UtcNow() => DateTime.UtcNow;

    /// <summary>
    /// 将 ISO 8601 字符串解析为 UTC 时间。
    /// 支持带偏移（+08:00）、带 Z 或本地无偏移格式；解析失败返回 null（调用方按错误处理）。
    /// 沿用网关"时间戳以 O 格式字符串存、字典序即时间序"的约定。
    /// </summary>
    public static DateTime? FromIso(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
            return null;

        if (DateTimeOffset.TryParse(iso, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var dto))
            return dto.UtcDateTime;

        // 兜底：尝试无偏移解析（假定 UTC）
        return DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var dt)
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : null;
    }

    /// <summary>格式化为 O 格式（round-trip）UTC 字符串，字典序即时间序</summary>
    public static string ToIso(DateTime utc) => utc.ToUniversalTime().ToString("O");
}
