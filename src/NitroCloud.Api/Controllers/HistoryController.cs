using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using NitroCloud.Api.Dtos;
using NitroCloud.Api.Models;
using NitroCloud.Storage;
using NitroCloud.Storage.Models;

namespace NitroCloud.Api.Controllers;

/// <summary>
/// 时序历史查询 API（前端 web/src/api/history.ts）。
/// 时序数据只进 InfluxDB（ADR-001 载荷墙），此端点经 <see cref="ITimeseriesStore"/> 查询。
/// </summary>
[ApiController, Route("api/history")]
public sealed class HistoryController : ControllerBase
{
    private readonly ITimeseriesStore _store;

    /// <summary>创建控制器</summary>
    public HistoryController(ITimeseriesStore store) => _store = store;

    /// <summary>时序查询（siteId 必填；from/to 可选；limit 夹紧 [1,5000]）</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PointSnapshotDto>>>> Query(
        [FromQuery] HistoryQueryDto query, CancellationToken ct)
    {
        if (!TryParse(query, out var timeseriesQuery, out var error))
            return BadRequest(ApiResponse<IReadOnlyList<PointSnapshotDto>>.Fail("InvalidQuery", error!));

        var points = await _store.QueryAsync(timeseriesQuery!, ct);
        return Ok(ApiResponse<IReadOnlyList<PointSnapshotDto>>.Ok(points.Select(ApiMappings.ToDto).ToList()));
    }

    /// <summary>时序 CSV 导出（text/csv，附件下载）</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] HistoryQueryDto query, CancellationToken ct)
    {
        if (!TryParse(query, out var timeseriesQuery, out var error))
            return BadRequest(ApiResponse<object>.Fail("InvalidQuery", error!));

        var points = await _store.QueryAsync(timeseriesQuery!, ct);
        var csv = BuildCsv(points);
        // 带 BOM，便于 Excel 直接识别 UTF-8
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        var filename = $"history_{timeseriesQuery!.SiteId}_{timeseriesQuery.DevicePointId ?? "all"}.csv";
        return File(bytes, "text/csv; charset=utf-8", filename);
    }

    /// <summary>解析查询参数（siteId 必填、时间合法、from ≤ to、limit 夹紧）；失败时 error 说明原因</summary>
    private static bool TryParse(HistoryQueryDto q, out TimeseriesQuery? query, out string? error)
    {
        query = null;
        error = null;

        if (string.IsNullOrWhiteSpace(q.SiteId))
        {
            error = "siteId 必填（第一隔离维度，ADR-004）";
            return false;
        }

        if (!TryParseTime(q.From, out var from) || !TryParseTime(q.To, out var to))
        {
            error = "from/to 需为合法 ISO 8601 时间";
            return false;
        }
        from ??= DateTime.UtcNow.AddHours(-24);
        to ??= DateTime.UtcNow;
        if (from.Value > to.Value)
        {
            error = "from 不能晚于 to";
            return false;
        }

        query = new TimeseriesQuery
        {
            SiteId = q.SiteId.Trim(),
            DeviceId = string.IsNullOrWhiteSpace(q.DeviceId) ? null : q.DeviceId.Trim(),
            DevicePointId = string.IsNullOrWhiteSpace(q.DevicePointId) ? null : q.DevicePointId.Trim(),
            From = from.Value,
            To = to.Value,
            Limit = Math.Clamp(q.Limit ?? 1000, 1, 5000)
        };
        return true;
    }

    /// <summary>解析可空时间（空 → true 且 value=null；非法 → false）</summary>
    private static bool TryParseTime(string? s, out DateTime? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(s))
            return true;
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
        {
            value = dt.ToUniversalTime();
            return true;
        }
        return false;
    }

    /// <summary>构建 CSV（首行表头 + 数据行；字段全部引号包裹并转义内部双引号）</summary>
    private static string BuildCsv(IReadOnlyList<MeasurementPoint> points)
    {
        var sb = new StringBuilder();
        sb.AppendLine("siteId,deviceId,devicePointId,pointName,value,quality,timestamp");
        foreach (var p in points)
        {
            sb.Append(Csv(p.SiteId)).Append(',')
              .Append(Csv(p.DeviceId)).Append(',')
              .Append(Csv(p.DevicePointId)).Append(',')
              .Append(Csv(p.PointName)).Append(',')
              .Append(p.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(Csv(p.Quality)).Append(',')
              .Append(p.Time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
              .AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>CSV 字段转义：双引号包裹，内部双引号加倍</summary>
    private static string Csv(string s) => $"\"{s.Replace("\"", "\"\"")}\"";
}
