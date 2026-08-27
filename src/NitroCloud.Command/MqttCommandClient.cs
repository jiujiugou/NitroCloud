using System.Buffers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NitroCloud.Domain.Commands;
using NitroCloud.Shared;
using NitroCloud.Telemetry;
using MqttNet = MQTTnet;

namespace NitroCloud.Command;

/// <summary>
/// Command MQTT 客户端（ADR-010 D1/D6）：独立 clientId 的单连接——
/// 发布 <c>commands</c>（QoS1）+ 订阅 <c>commands/ack</c>（QoS1），断线自持重连循环（简单固定退避，不引 Polly）。
/// 实现 <see cref="ICommandDispatcher"/> 供 Api 触发发布；回执经 <see cref="AckMessageReceived"/> 事件上抛，
/// 由 <see cref="CommandHostedService"/> 解析并交给 <see cref="CommandManager"/>。
/// </summary>
public sealed class MqttCommandClient : ICommandDispatcher, IAsyncDisposable
{
    /// <summary>配置快照（构造时读入，运行期不刷新）</summary>
    private readonly CommandOptions _options;
    /// <summary>日志</summary>
    private readonly ILogger<MqttCommandClient> _logger;

    /// <summary>MQTT 客户端（在 <see cref="RunAsync"/> 内创建并持有；可空以允许构造阶段无客户端）</summary>
    private MqttNet.IMqttClient? _client;
    /// <summary>断线信号：DisconnectedAsync 触发 TrySetResult 使「等待连接存活」的循环退出进入重连</summary>
    private TaskCompletionSource _disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 回执消息事件（topic 为 commands/ack 的原始载荷字节）。
    /// 订阅方（CommandHostedService）负责解析与业务处理；处理器异常在事件内被捕获记日志，不影响接收循环。
    /// </summary>
    public event Func<byte[], CancellationToken, Task>? AckMessageReceived;

    /// <summary>创建 Command MQTT 客户端</summary>
    public MqttCommandClient(IOptions<CommandOptions> options, ILogger<MqttCommandClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 连接主循环（由 <see cref="CommandHostedService"/> 启动）：每轮「连接 + 订阅 → 阻塞等待存活 → 断开后延迟重连」。
    /// 非取消异常被捕获记警告并继续循环，避免单次异常杀死整个命令接入（ADR-010 D6：单连接简单退避，不引 Polly）。
    /// </summary>
    /// <param name="ct">停止令牌；置位时停止重连与等待，退出循环</param>
    public async Task RunAsync(CancellationToken ct)
    {
        _client = new MqttNet.MqttClientFactory().CreateMqttClient();
        _client.DisconnectedAsync += _ =>
        {
            _disconnected.TrySetResult();
            return Task.CompletedTask;
        };
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                await ConnectAndSubscribeAsync(ct);
                _logger.LogInformation("Command MQTT 已连接 {Host}:{Port}（clientId={ClientId}）",
                    _options.MqttHost, _options.MqttPort, _options.ClientId);

                // 阻塞直到断线或取消（DisconnectedAsync 触发 TCS）
                await _disconnected.Task.WaitAsync(ct);
                _logger.LogWarning("Command MQTT 连接断开，准备重连");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Command MQTT 连接/订阅意外异常");
            }

            try
            {
                await Task.Delay(_options.ReconnectDelayMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// 发布一条命令到 <c>commands</c> topic（QoS1，DESIGN.md §4.3）。
    /// 未连接时返回失败结果（不改 Pending 语义，由后台扫描重发兜底）；取消令牌触发时原样抛
    /// <see cref="OperationCanceledException"/>。
    /// </summary>
    public async Task<OperationResult> DispatchAsync(CommandRecord command, CancellationToken ct = default)
    {
        if (_client is null || !_client.IsConnected)
        {
            CloudMetrics.CommandSentTotal.WithLabels("publish_failed").Inc();
            return OperationalError.Communication("Command MQTT 未连接，发布失败（后台扫描将重发）");
        }

        var request = new CommandRequest
        {
            CommandId = command.CommandId,
            Type = command.Type,
            PointId = command.PointId,
            Value = command.Value,
            RequestedAt = command.RequestedAt.ToUniversalTime()
        };

        try
        {
            var message = new MqttNet.MqttApplicationMessageBuilder()
                .WithTopic(TopicUtil.Commands(command.SiteId, command.DeviceId))
                .WithPayload(CommandRequestSerializer.Serialize(request))
                .WithQualityOfServiceLevel(MqttNet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
                .Build();

            await _client.PublishAsync(message, ct);
            CloudMetrics.CommandSentTotal.WithLabels("sent").Inc();
            _logger.LogInformation("命令 {CommandId} 已发布到 {Topic}",
                command.CommandId, TopicUtil.Commands(command.SiteId, command.DeviceId));
            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            CloudMetrics.CommandSentTotal.WithLabels("publish_failed").Inc();
            _logger.LogWarning(ex, "命令 {CommandId} 发布失败", command.CommandId);
            return OperationalError.Communication($"命令发布异常: {ex.Message}");
        }
    }

    /// <summary>正常断开并释放客户端（StopAsync / DisposeAsync 共用，幂等）</summary>
    public async Task StopAsync(CancellationToken ct = default)
    {
        var client = _client;
        _client = null;
        if (client is null)
            return;

        try
        {
            await client.DisconnectAsync(new MqttNet.MqttClientDisconnectOptions
            {
                Reason = MqttNet.MqttClientDisconnectOptionsReason.NormalDisconnection
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Command MQTT 断开时异常（忽略）");
        }
        client.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }

    /// <summary>MQTT 消息回调：仅转发 <c>commands/ack</c> 的原始载荷给事件订阅方</summary>
    private async Task OnMessageReceivedAsync(MqttNet.MqttApplicationMessageReceivedEventArgs e)
    {
        var parsed = TopicUtil.Parse(e.ApplicationMessage.Topic);
        if (parsed is null || parsed.Value.Kind != TopicKind.CommandAck)
            return;

        var payload = ReadPayload(e.ApplicationMessage.Payload);
        var handler = AckMessageReceived;
        if (handler is null)
            return;

        try
        {
            await handler(payload, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理命令回执异常 topic={Topic}", e.ApplicationMessage.Topic);
        }
    }

    /// <summary>
    /// 把 MQTT 消息载荷（ReadOnlySequence&lt;byte&gt;）逐段拷贝为字节数组
    /// （MQTTnet 5.x Payload 为分段序列；逐段拷贝兼容任意分段布局，与 Ingest 约定一致）。
    /// </summary>
    private static byte[] ReadPayload(ReadOnlySequence<byte> sequence)
    {
        var bytes = new byte[sequence.Length];
        var offset = 0;
        foreach (var segment in sequence)
        {
            segment.Span.CopyTo(bytes.AsSpan(offset));
            offset += segment.Length;
        }

        return bytes;
    }

    /// <summary>连接 broker + 订阅回执 topic（QoS1）；失败抛异常由 <see cref="RunAsync"/> 捕获重连</summary>
    private async Task ConnectAndSubscribeAsync(CancellationToken ct)
    {
        var options = new MqttNet.MqttClientOptionsBuilder()
            .WithTcpServer(_options.MqttHost, _options.MqttPort)
            .WithClientId(_options.ClientId)
            .WithCleanStart()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .Build();

        var result = await _client!.ConnectAsync(options, ct);
        if (result.ResultCode != MqttNet.MqttClientConnectResultCode.Success)
            throw new InvalidOperationException($"MQTT 命令连接失败: {result.ResultCode} - {result.ReasonString}");

        var subscribe = new MqttNet.MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(TopicUtil.CommandAckSubscription, MqttNet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();
        await _client.SubscribeAsync(subscribe, ct);
        _logger.LogInformation("Command 订阅回执 {Ack}（QoS1）", TopicUtil.CommandAckSubscription);
    }
}
