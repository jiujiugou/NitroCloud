using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using NitroCloud.Api.Dtos;
using NitroCloud.Api.Models;
using NitroCloud.Domain.Alarms;
using NitroCloud.Persistence;
using NitroCloud.Storage;

namespace NitroCloud.Api.Controllers;

/// <summary>
/// 告警查询 / 确认 API（前端 web/src/api/alarms.ts）。
/// 告警汇总（active/today）为 ADR-008 清单之外的补充端点，前端已按此消费。
/// </summary>
[ApiController, Route("api/alarms")]
public sealed class AlarmsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAlarmStore _store;

    /// <summary>创建控制器</summary>
    public AlarmsController(AppDbContext db, IAlarmStore store)
    {
        _db = db;
        _store = store;
    }

    /// <summary>按站点/级别/状态查询告警（时间降序）</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<AlarmRecord>>>> Query(
        [FromQuery] AlarmQueryDto query, CancellationToken ct)
    {
        var alarmQuery = query.ToAlarmQuery();
        if (alarmQuery is null)
            return BadRequest(ApiResponse<IReadOnlyList<AlarmRecord>>.Fail("InvalidQuery", "severity/state 需为合法枚举值"));

        var alarms = await _store.QueryAsync(alarmQuery, ct);
        return Ok(ApiResponse<IReadOnlyList<AlarmRecord>>.Ok(alarms));
    }

    /// <summary>
    /// 告警汇总 KPI：活跃数 / 今日发生数。
    /// 「今日」按服务器本地时区 0 点起算（与网关口径一致；OccurredAt 存 UTC O 串，按字典序比较）。
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<AlarmSummaryDto>>> Summary(CancellationToken ct)
    {
        var active = await _db.AlarmRecords.AsNoTracking()
            .CountAsync(a => a.State == nameof(AlarmState.Active), ct);

        var now = DateTime.Now;
        var localTodayStart = new DateTime(now.Year, now.Month, now.Day);
        var todayStartUtc = TimeZoneInfo.ConvertTimeToUtc(localTodayStart).ToString("O");
        var today = await _db.AlarmRecords.AsNoTracking()
            .CountAsync(a => string.CompareOrdinal(a.OccurredAt, todayStartUtc) >= 0, ct);

        return Ok(ApiResponse<AlarmSummaryDto>.Ok(new AlarmSummaryDto(active, today)));
    }

    /// <summary>确认告警（不存在的告警返回 404；body 可选，AckBy 默认 console）</summary>
    [HttpPost("{id}/ack")]
    public async Task<ActionResult<ApiResponse<object>>> Ack(
        string id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AckAlarmDto? body = null,
        CancellationToken ct = default)
    {
        if (!await _db.AlarmRecords.AsNoTracking().AnyAsync(a => a.Id == id, ct))
            return NotFound(ApiResponse<object>.Fail("AlarmNotFound", $"告警 {id} 不存在"));

        await _store.AckAsync(id, string.IsNullOrWhiteSpace(body?.AckBy) ? "console" : body!.AckBy!, ct);
        return Ok(ApiResponse<object>.Ok(new { }));
    }
}
