using NitroCloud.Domain.Alarms;
using NitroCloud.Storage.Models;

namespace NitroCloud.Storage;

/// <summary>
/// 告警存储接口（ADR-008 D3，接口只增不删）。SQLite 实现（告警汇总落库）。
/// </summary>
public interface IAlarmStore
{
    /// <summary>新增/更新一条告警（按 alarmId 幂等 upsert）</summary>
    Task AddAsync(AlarmRecord alarm, CancellationToken ct = default);

    /// <summary>按站点/级别/状态/时间查询告警，时间降序</summary>
    Task<IReadOnlyList<AlarmRecord>> QueryAsync(AlarmQuery query, CancellationToken ct = default);

    /// <summary>云端确认告警（置 State=Acknowledged、AckedAt=now）</summary>
    Task AckAsync(string alarmId, string ackBy, CancellationToken ct = default);
}
