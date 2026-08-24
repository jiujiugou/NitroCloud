namespace NitroCloud.Domain.Measurements;

/// <summary>
/// 批量测量载荷（v1 契约，与 NitroGateway.BatchMeasurements 对齐，以网关侧为准）。
/// JSON 顶层字段 <c>v</c>（当前 1）；旧版无 v 字段按 0 → v1 兼容解析（DESIGN.md §4.1）。
/// </summary>
public sealed record BatchMeasurements
{
    /// <summary>站点标识：随载荷上行，与 topic 第三段作冗余校验（ADR-004）</summary>
    public string SiteId { get; init; } = "";

    /// <summary>载荷版本号（当前 1；旧版无此字段按 v1 兼容）</summary>
    public int V { get; init; } = 1;

    /// <summary>批次唯一标识（用于去重，ADR-008 D5）</summary>
    public Guid Id { get; init; }

    /// <summary>所属设备 ID（Guid）</summary>
    public Guid DeviceId { get; init; }

    /// <summary>本次扫描开始时间（UTC）</summary>
    public DateTime ScanStartedAt { get; init; }

    /// <summary>本次扫描结束时间（UTC）</summary>
    public DateTime ScanCompletedAt { get; init; }

    /// <summary>本轮采集产生的全部测点记录</summary>
    public IReadOnlyList<MeasurementRecord> Records { get; init; } = Array.Empty<MeasurementRecord>();

    /// <summary>成功采集的点位数</summary>
    public int SuccessCount => Records.Count(r => r.Quality == Quality.Good);

    /// <summary>采集失败/不确定的点位数</summary>
    public int FailCount => Records.Count - SuccessCount;
}
