using NitroCloud.Domain.Measurements;
using NitroCloud.Shared;
using NitroCloud.Storage.Models;

namespace NitroCloud.Storage;

/// <summary>
/// 时序存储接口（ADR-008 D3，接口只增不删）。批量写入 + 时间范围查询。
/// 由 Ingest 消费写入（Influx 实现）、Api 消费查询。
/// </summary>
public interface ITimeseriesStore
{
    /// <summary>
    /// 批量写入测量记录。实现侧应做批量优化而非逐条写；失败时返回失败结果，
    /// 调用方（Ingest 重试队列）必须检查结果并按策略重试，不得忽略。
    /// </summary>
    Task<OperationResult> WriteAsync(IReadOnlyList<MeasurementRecord> records, CancellationToken ct = default);

    /// <summary>
    /// 按站点（强制）+ 设备/点位 + 时间范围查询历史数据，时间升序。
    /// </summary>
    Task<IReadOnlyList<MeasurementPoint>> QueryAsync(TimeseriesQuery query, CancellationToken ct = default);
}
