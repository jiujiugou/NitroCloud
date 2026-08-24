using Microsoft.AspNetCore.SignalR;
using NitroCloud.Api.Hubs;
using NitroCloud.Domain.Alarms;
using NitroCloud.Domain.Commands;
using NitroCloud.Domain.Devices;
using NitroCloud.Domain.Measurements;
using NitroCloud.Storage;

namespace NitroCloud.Api.Realtime;

/// <summary>
/// 实时推送的 SignalR 实现（ADR-008 D1 推送解耦：Ingest/Command 只依赖 Storage.IRealtimeNotifier，
/// 本类用 IHubContext 落 SignalR 推送，保证单向依赖不破环）。
/// 测量/告警/设备状态按站点分组推（site:{siteId}），命令回执全局推（ack 无 siteId 字段）。
/// </summary>
public sealed class SignalRRealtimeNotifier : IRealtimeNotifier
{
    private readonly IHubContext<NitroCloudHub> _hub;

    /// <summary>创建推送器</summary>
    public SignalRRealtimeNotifier(IHubContext<NitroCloudHub> hub) => _hub = hub;

    /// <inheritdoc />
    public Task NotifyMeasurementsAsync(string siteId, IReadOnlyList<MeasurementRecord> records, CancellationToken ct = default)
        => _hub.Clients.Group(NitroCloudHub.GroupName(siteId)).SendAsync("OnMeasurements", records, ct);

    /// <inheritdoc />
    public Task NotifyAlarmAsync(AlarmRecord alarm, CancellationToken ct = default)
        => _hub.Clients.Group(NitroCloudHub.GroupName(alarm.SiteId)).SendAsync("OnAlarm", alarm, ct);

    /// <inheritdoc />
    public Task NotifyDeviceStatusAsync(string siteId, DeviceStatus status, CancellationToken ct = default)
        => _hub.Clients.Group(NitroCloudHub.GroupName(siteId)).SendAsync("OnDeviceStatus", status.ToString(), ct);

    /// <inheritdoc />
    public Task NotifyCommandAckAsync(CommandAck ack, CancellationToken ct = default)
        => _hub.Clients.All.SendAsync("OnCommandAck", ack, ct);
}
