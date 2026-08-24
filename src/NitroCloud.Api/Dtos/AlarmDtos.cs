using NitroCloud.Domain.Alarms;
using NitroCloud.Storage.Models;

namespace NitroCloud.Api.Dtos;

/// <summary>
/// 告警汇总（KPI：活跃数 / 今日发生数）。前端 web/src/api/types.ts 的 AlarmSummary 对齐。
/// 属 ADR-008 D7 清单之外的补充端点，前端已按此消费。
/// </summary>
public sealed record AlarmSummaryDto(int Active, int Today);

/// <summary>告警确认请求体（body 可选；未传 AckBy 时默认 console）</summary>
public sealed record AckAlarmDto
{
    /// <summary>确认人（可选，默认 console）</summary>
    public string? AckBy { get; init; }
}

/// <summary>告警查询请求参数（GET /api/alarms）</summary>
public sealed record AlarmQueryDto
{
    /// <summary>站点（可选过滤）</summary>
    public string? SiteId { get; init; }

    /// <summary>严重级别（可选过滤）</summary>
    public string? Severity { get; init; }

    /// <summary>状态（可选过滤）</summary>
    public string? State { get; init; }

    /// <summary>分页大小（默认 50，夹紧到 [1,1000]）</summary>
    public int? Limit { get; init; }

    /// <summary>分页偏移（默认 0）</summary>
    public int? Offset { get; init; }

    /// <summary>把请求参数转为 Storage.AlarmQuery（非法枚举返回 null，由调用方回 400）</summary>
    public AlarmQuery? ToAlarmQuery()
    {
        AlarmSeverity? severity = null;
        if (!string.IsNullOrWhiteSpace(Severity))
        {
            if (!Enum.TryParse<AlarmSeverity>(Severity, true, out var sev)) return null;
            severity = sev;
        }

        AlarmState? state = null;
        if (!string.IsNullOrWhiteSpace(State))
        {
            if (!Enum.TryParse<AlarmState>(State, true, out var st)) return null;
            state = st;
        }

        return new Storage.Models.AlarmQuery
        {
            SiteId = string.IsNullOrWhiteSpace(SiteId) ? null : SiteId,
            Severity = severity,
            State = state,
            Limit = Limit ?? 50,
            Offset = Offset ?? 0
        };
    }
}
