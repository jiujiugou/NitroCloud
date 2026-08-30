using NitroCloud.Storage.Models;

namespace NitroCloud.Influx;

/// <summary>
/// Flux 查询语句构造（纯字符串构建，便于单测；ADR-008 D2：Flux 查询封装）。
/// 生成形如 from(bucket) |> range |> filter(siteId/deviceId/devicePointId) |> sort |> limit 的查询。
/// </summary>
public static class FluxQueryBuilder
{
    /// <summary>
    /// 由 <see cref="TimeseriesQuery"/> 生成 Flux 查询。
    /// siteId 为强制过滤（ADR-004）；时间窗口闭区间 [From, To] 转 RFC3339 UTC。
    /// </summary>
    public static string Build(TimeseriesQuery query, string bucket, string measurement)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.From > query.To)
            throw new ArgumentException("From 不能晚于 To（空时间窗）", nameof(query));

        var start = FormatTimeLiteral(query.From);
        var stop = FormatTimeLiteral(query.To);

        var flux = new System.Text.StringBuilder()
            .Append("from(bucket: \"").Append(EscapeDoubleQuoted(bucket)).Append("\")")
            .Append("\n  |> range(start: ").Append(start).Append(", stop: ").Append(stop).Append(')')
            .Append("\n  |> filter(fn: (r) => r._measurement == \"").Append(EscapeDoubleQuoted(measurement)).Append("\")")
            .Append("\n  |> filter(fn: (r) => r.siteId == \"").Append(EscapeDoubleQuoted(query.SiteId)).Append("\")");

        if (!string.IsNullOrWhiteSpace(query.DeviceId))
            flux.Append("\n  |> filter(fn: (r) => r.deviceId == \"").Append(EscapeDoubleQuoted(query.DeviceId)).Append("\")");
        if (!string.IsNullOrWhiteSpace(query.DevicePointId))
            flux.Append("\n  |> filter(fn: (r) => r.devicePointId == \"").Append(EscapeDoubleQuoted(query.DevicePointId)).Append("\")");

        flux.Append("\n  |> filter(fn: (r) => r._field == \"value\")")
            .Append("\n  |> sort(columns: [\"_time\"], desc: false)")
            .Append("\n  |> limit(n: ").Append(Math.Clamp(query.Limit, 1, 5000)).Append(')')
            .Append("\n  |> keep(columns: [\"_time\", \"_value\", \"siteId\", \"deviceId\", \"devicePointId\", \"pointName\", \"quality\"])");

        return flux.ToString();
    }

    /// <summary>
    /// 把 <see cref="DateTime"/> 转为 Flux 的 time 字面量：RFC3339 UTC（不带引号）。
    /// 注意：Flux 中带引号的 "2026-08-23T...Z" 会被解析为 string，range(start, stop)
    /// 会报 "value is not a time, got string"；裸时间戳才是 time 字面量。
    /// </summary>
    private static string FormatTimeLiteral(DateTime value)
        => value.ToUniversalTime().ToString("O");

    /// <summary>转义 Flux 双引号字符串内的引号</summary>
    private static string EscapeDoubleQuoted(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
