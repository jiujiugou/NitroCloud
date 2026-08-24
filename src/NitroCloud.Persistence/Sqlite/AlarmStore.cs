using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NitroCloud.Domain.Alarms;
using NitroCloud.Persistence.Entities;
using NitroCloud.Storage;
using NitroCloud.Storage.Models;

namespace NitroCloud.Persistence.Sqlite;

/// <summary>
/// 告警存储（SQLite / EF Core）。网关可能重复推送同一告警，按 alarmId 幂等 upsert。
/// </summary>
public sealed class AlarmStore : IAlarmStore
{
    private readonly AppDbContext _db;
    private readonly ILogger<AlarmStore> _logger;

    /// <summary>创建告警存储</summary>
    public AlarmStore(AppDbContext db, ILogger<AlarmStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AddAsync(AlarmRecord alarm, CancellationToken ct = default)
    {
        var entity = await _db.AlarmRecords.FindAsync([alarm.Id], ct);
        if (entity is null)
        {
            _db.AlarmRecords.Add(ToEntity(alarm));
        }
        else
        {
            // 幂等 upsert：保留云端已确认状态，其余字段以网关最新为准
            if (entity.State == nameof(AlarmState.Acknowledged))
            {
                entity.State = alarm.State == AlarmState.Resolved ? nameof(AlarmState.Resolved) : nameof(AlarmState.Acknowledged);
            }
            else
            {
                ApplyToEntity(entity, alarm);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AlarmRecord>> QueryAsync(AlarmQuery query, CancellationToken ct = default)
    {
        IQueryable<AlarmRecordEntity> q = _db.AlarmRecords.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.SiteId))
            q = q.Where(a => a.SiteId == query.SiteId);
        if (query.Severity.HasValue)
            q = q.Where(a => a.Severity == query.Severity.Value.ToString());
        if (query.State.HasValue)
            q = q.Where(a => a.State == query.State.Value.ToString());
        if (query.From.HasValue)
            q = q.Where(a => string.CompareOrdinal(a.OccurredAt, query.From.Value.ToUniversalTime().ToString("O")) >= 0);
        if (query.To.HasValue)
            q = q.Where(a => string.CompareOrdinal(a.OccurredAt, query.To.Value.ToUniversalTime().ToString("O")) <= 0);

        var entities = await q
            .OrderByDescending(a => a.OccurredAt)
            .Skip(Math.Max(0, query.Offset))
            .Take(Math.Clamp(query.Limit, 1, 1000))
            .ToListAsync(ct);

        return entities.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task AckAsync(string alarmId, string ackBy, CancellationToken ct = default)
    {
        var entity = await _db.AlarmRecords.FindAsync([alarmId], ct);
        if (entity is null)
        {
            _logger.LogWarning("确认不存在的告警 {AlarmId}（ackBy={AckBy}）", alarmId, ackBy);
            return;
        }

        if (entity.State != nameof(AlarmState.Resolved))
        {
            entity.State = nameof(AlarmState.Acknowledged);
            entity.AckedAt = DateTime.UtcNow.ToString("O");
        }

        await _db.SaveChangesAsync(ct);
    }

    private static AlarmRecordEntity ToEntity(AlarmRecord a) => new()
    {
        Id = a.Id,
        RuleId = a.RuleId,
        SiteId = a.SiteId,
        DeviceId = a.DeviceId,
        PointId = a.PointId,
        TriggerValue = a.TriggerValue,
        Threshold = a.Threshold,
        Severity = a.Severity.ToString(),
        Message = a.Message,
        State = a.State.ToString(),
        OccurredAt = a.OccurredAt.ToUniversalTime().ToString("O"),
        AckedAt = a.AckedAt?.ToUniversalTime().ToString("O")
    };

    private static void ApplyToEntity(AlarmRecordEntity e, AlarmRecord a)
    {
        e.RuleId = a.RuleId;
        e.SiteId = a.SiteId;
        e.DeviceId = a.DeviceId;
        e.PointId = a.PointId;
        e.TriggerValue = a.TriggerValue;
        e.Threshold = a.Threshold;
        e.Severity = a.Severity.ToString();
        e.Message = a.Message;
        e.State = a.State.ToString();
        e.OccurredAt = a.OccurredAt.ToUniversalTime().ToString("O");
        e.AckedAt = a.AckedAt?.ToUniversalTime().ToString("O");
    }

    private static AlarmRecord ToDomain(AlarmRecordEntity e) => new()
    {
        Id = e.Id,
        RuleId = e.RuleId,
        SiteId = e.SiteId,
        DeviceId = e.DeviceId,
        PointId = e.PointId,
        TriggerValue = e.TriggerValue,
        Threshold = e.Threshold,
        Severity = Enum.Parse<AlarmSeverity>(e.Severity),
        Message = e.Message,
        State = Enum.Parse<AlarmState>(e.State),
        OccurredAt = DateTime.Parse(e.OccurredAt, null, System.Globalization.DateTimeStyles.AssumeUniversal),
        AckedAt = e.AckedAt is null
            ? null
            : DateTime.Parse(e.AckedAt, null, System.Globalization.DateTimeStyles.AssumeUniversal)
    };
}
