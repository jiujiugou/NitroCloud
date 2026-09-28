using Microsoft.Extensions.Logging;
using NitroCloud.Shared;
using MqttNet = MQTTnet;

namespace NitroCloud.Ingest;

/// <summary>
/// Ingest MQTT 连接 + 订阅逻辑（从 <see cref="MqttIngestHostedService"/> 抽出为纯静态方法，便于单测注入假 <see cref="MqttNet.IMqttClient"/>）。
///
/// 关键约束：MQTTnet 的 <c>MqttClient.ConnectAsync</c> 在客户端已连接时会抛
/// <see cref="InvalidOperationException"/>（"It is not allowed to connect with a server after the connection is established."）。
/// 因此本方法只在 <see cref="MqttNet.IMqttClient.IsConnected"/> 为 false 时才发起连接，已连接则直接补订阅——
/// 否则「连接成功但订阅失败」被整段重试时，ConnectAsync 会因连接仍在而永远失败（连接泄漏 + 死循环）。
/// </summary>
internal static class IngestMqttConnection
{
    /// <summary>
    /// 连接 broker + 订阅（带连接状态判定）：
    /// 已连接 → 跳过 ConnectAsync 直接订阅；未连接 → 连接成功后再订阅。
    /// 成功返回 <see cref="OperationResult.Success"/>；失败返回携带 <see cref="OperationalError"/>（Communication）的失败结果，
    /// 不抛异常（供 Polly 重试）；取消令牌触发时原样上抛 <see cref="OperationCanceledException"/>。
    /// </summary>
    /// <param name="client">MQTT 客户端（生产由宿主服务创建并复用）</param>
    /// <param name="options">Ingest 配置快照（主机/端口/ClientId）</param>
    /// <param name="logger">日志（订阅成功时记录）</param>
    /// <param name="ct">取消令牌；连接/订阅任一环节取消时原样上抛</param>
    internal static async Task<OperationResult> ConnectAndSubscribeAsync(
        MqttNet.IMqttClient client,
        IngestOptions options,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            if (!client.IsConnected)
            {
                var clientOptions = new MqttNet.MqttClientOptionsBuilder()
                    .WithTcpServer(options.MqttHost, options.MqttPort)
                    .WithClientId(options.ClientId)
                    .WithCleanStart()
                    .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                    .Build();

                // 状态判定：broker 返回非 Success（如凭据错误/标识拒绝）也视为连接失败
                var result = await client.ConnectAsync(clientOptions, ct);
                if (result.ResultCode != MqttNet.MqttClientConnectResultCode.Success)
                    return OperationalError.Communication($"MQTT 接入连接失败: {result.ResultCode} - {result.ReasonString}");
            }

            await SubscribeAsync(client, logger, ct);
            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationalError.Communication($"MQTT 接入连接异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 订阅上行 topic（measurements / alarms，均 QoS1，契约见 ADR-008 D4）。
    /// QoS1 保证至少一次投递（配合 <see cref="BatchDeduplicator"/> 去重）；订阅失败抛异常，
    /// 由 <see cref="ConnectAndSubscribeAsync"/> 收敛为 <see cref="OperationResult"/> 供重试。
    /// </summary>
    /// <param name="client">MQTT 客户端</param>
    /// <param name="logger">日志</param>
    /// <param name="ct">取消令牌</param>
    private static async Task SubscribeAsync(MqttNet.IMqttClient client, ILogger logger, CancellationToken ct)
    {
        var subscribe = new MqttNet.MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(TopicUtil.MeasurementsSubscription, MqttNet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .WithTopicFilter(TopicUtil.AlarmsSubscription, MqttNet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await client.SubscribeAsync(subscribe, ct);
        logger.LogInformation("Ingest 订阅 {Measurements} / {Alarms}（QoS1）",
            TopicUtil.MeasurementsSubscription, TopicUtil.AlarmsSubscription);
    }
}
