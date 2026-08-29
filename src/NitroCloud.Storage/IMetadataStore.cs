using NitroCloud.Domain.Measurements;

namespace NitroCloud.Storage;

/// <summary>
/// 元数据存储接口（ADR-013：测量数据到达时自动注册站点/设备/点位；接口只增不删）。
/// 与告警/命令存储不同，本接口按「批」幂等注册：重复调用 / 重复记录不产生重复行。
/// best-effort 契约：调用方（Ingest）注册失败只记 Warning + 指标，不阻塞时序写入。
/// </summary>
public interface IMetadataStore
{
    /// <summary>
    /// 幂等注册一批测量记录中的元数据（站点 → 设备 → 点位）。
    /// 数据来源只用测量记录已有字段：SiteId / DeviceId / DevicePointId / PointName / DataType；
    /// 站点/设备显示名缺省用 Id 兜底（后续可在管理面板完善）。不新增、不修改网关契约。
    /// </summary>
    /// <param name="records">本批待写时序库的测量记录（含元数据来源字段）</param>
    /// <param name="ct">取消令牌</param>
    Task EnsureRegisteredAsync(IReadOnlyList<MeasurementRecord> records, CancellationToken ct = default);
}
