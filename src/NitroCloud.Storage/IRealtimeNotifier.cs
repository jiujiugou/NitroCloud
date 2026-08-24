using NitroCloud.Domain.Alarms;
using NitroCloud.Domain.Commands;
using NitroCloud.Domain.Devices;
using NitroCloud.Domain.Measurements;

namespace NitroCloud.Storage;

/// <summary>
/// 实时推送接口（ADR-008 D1 推送解耦：Ingest/Command 不直接引用 SignalR，
/// 只依赖本接口，由 Api 用 IHubContext 实现，保证单向依赖不破环）。
/// </summary>
public interface IRealtimeNotifier
{
    /// <summary>推送一批测量记录（OnMeasurements，按站点分组）</summary>
    Task NotifyMeasurementsAsync(string siteId, IReadOnlyList<MeasurementRecord> records, CancellationToken ct = default);

    /// <summary>推送一条告警（OnAlarm）</summary>
    Task NotifyAlarmAsync(AlarmRecord alarm, CancellationToken ct = default);

    /// <summary>推送设备在线状态变化（OnDeviceStatus）</summary>
    Task NotifyDeviceStatusAsync(string siteId, DeviceStatus status, CancellationToken ct = default);

    /// <summary>推送命令回执（OnCommandAck）</summary>
    Task NotifyCommandAckAsync(CommandAck ack, CancellationToken ct = default);
}
