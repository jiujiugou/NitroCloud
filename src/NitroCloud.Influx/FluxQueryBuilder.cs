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

        var start = query.From.ToUniversalTime().ToString("O");
        var stop = query.To.ToUniversalTime().ToString("O");

        var flux = new System.Text.StringBuilder()
            .Append("from(bucket: \"").Append(EscapeDoubleQuoted(bucket)).Append("\")")
            .Append("\n  |> range(start: ").Append(Rfc3339(start)).Append(", stop: ").Append(Rfc3339(stop)).Append(')')
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

    /// <summary>把 O 格式时间转为 Flux 可用的 RFC3339（去掉末尾的 Z 后保留）</summary>
    private static string Rfc3339(string isoO)
    {
        // DateTime.ToString("O") 输出形如 2026-08-23T10:00:00.0000000Z，Flux 可直接解析
        return "\"" + isoO + "\"";
    }

    /// <summary>转义 Flux 双引号字符串内的引号</summary>
    private static string EscapeDoubleQuoted(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
