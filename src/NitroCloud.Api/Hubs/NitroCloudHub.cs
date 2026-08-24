using Microsoft.AspNetCore.SignalR;
using NitroCloud.Telemetry;

namespace NitroCloud.Api.Hubs;

/// <summary>
/// 实时数据 Hub（ADR-008 D7：大屏/管理面板实时推送）。
/// 入站：SubscribeSite(siteId)/UnsubscribeSite(siteId)/JoinGlobal（站点级分组 site:{siteId}）；
/// 出站：OnMeasurements/OnAlarm/OnDeviceStatus/OnCommandAck。
/// 前端以 /hubs/cloud 连接（web/src/api/signalr.ts）。
/// </summary>
public sealed class NitroCloudHub : Hub
{
    /// <summary>连接建立时记 SignalR 连接数（ADR-008 D8 指标）</summary>
    public override Task OnConnectedAsync()
    {
        CloudMetrics.SignalRConnections.Inc();
        return base.OnConnectedAsync();
    }

    /// <summary>连接断开时记 SignalR 连接数（递减）</summary>
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        CloudMetrics.SignalRConnections.Dec();
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>订阅某站点实时推送（加入 site:{siteId} 分组）</summary>
    public Task SubscribeSite(string siteId)
    {
        if (string.IsNullOrWhiteSpace(siteId))
            return Task.CompletedTask;
        return Groups.AddToGroupAsync(Context.ConnectionId, GroupName(siteId));
    }

    /// <summary>取消订阅某站点实时推送</summary>
    public Task UnsubscribeSite(string siteId)
    {
        if (string.IsNullOrWhiteSpace(siteId))
            return Task.CompletedTask;
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(siteId));
    }

    /// <summary>加入全局推送（默认已加入，预留：后续如需全局广播专用分组）</summary>
    public Task JoinGlobal() => Task.CompletedTask;

    /// <summary>站点分组名（统一 site:{siteId} 前缀）</summary>
    internal static string GroupName(string siteId) => $"site:{siteId}";
}
