using NitroCloud.Domain.Measurements;
using NitroCloud.Storage.Models;

namespace NitroCloud.Storage;

/// <summary>
/// 最近值内存缓存（ADR-005，接口只增不删）。
/// 由 Ingest 在解析通过后更新；Api/大屏读取，避免秒级刷新打爆时序库。
/// 容量有上限（实现侧按站点收敛）；重启丢缓存可接受（面板自恢复）。
/// </summary>
public interface ILatestValueCache
{
    /// <summary>用一批测量记录更新缓存（同一批数据与 InfluxDB 写路径一致）</summary>
    void Update(IReadOnlyList<MeasurementRecord> records);

    /// <summary>取站点下全部点位最近值</summary>
    IReadOnlyList<LatestValue> GetSite(string siteId);

    /// <summary>取单个点位最近值（不存在返回 null）</summary>
    LatestValue? GetPoint(string siteId, string deviceId, string devicePointId);

    /// <summary>
    /// 取站点最近一次上报时间（该站点下所有点位时间戳的最大值；从未上报返回 null）。
    /// 供 Api 在线状态判定（ADR-007：最后上报时间 + 阈值）使用，O(1) 读取。
    /// </summary>
    DateTime? GetSiteLastSeen(string siteId);

    /// <summary>取设备最近一次上报时间（该设备下所有点位时间戳的最大值；从未上报返回 null）</summary>
    DateTime? GetDeviceLastSeen(string siteId, string deviceId);
}
