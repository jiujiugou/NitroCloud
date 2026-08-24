using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NitroCloud.Domain.Alarms;
using NitroCloud.Domain.Measurements;
using NitroCloud.Ingest.Parsing;
using NitroCloud.Shared;
using NitroCloud.Storage;
using NitroCloud.Telemetry;
using MqttNet = MQTTnet;

namespace NitroCloud.Ingest;

/// <summary>
/// MQTT 接入宿主服务（ADR-008 D4 上行数据流）：
/// 订阅 measurements/alarms → 解析/校验 → 去重 → ①更新最近值缓存 + 实时推送（实时优先）
/// → ②批量写 InfluxDB（失败进内存重试队列，指数退避，超限丢弃 + 指标）。
///
/// 三个并行循环（<see cref="ExecuteAsync"/> 内 <see cref="Task.WhenAll"/> 联合调度）：
/// - MQTT 收发循环：连接 + 订阅（带状态判定，失败按 <see cref="MqttConnectRetryPolicy"/> 重试）+ 断线重连；
/// - 批量 flush 循环：按固定间隔把攒批缓冲写入时序库，失败进重试队列；
/// - 重试 drain 循环：按指数退避重试写失败批次，超限丢弃。
///
/// 单例生命周期：构造时从 options 快照读取参数并完成各组件装配；消息处理里只解析不持有 MQTT 长连接外的
/// 可变化状态，告警存储因是 scoped 生命周期，按操作开 scope 解析（见 <see cref="HandleAlarmAsync"/>）。
/// </summary>
public sealed class MqttIngestHostedService : BackgroundService
{
    /// <summary>配置快照（options.Value，启动校验通过后的值；构造时读入，运行期不刷新）</summary>
    private readonly IngestOptions _options;
    /// <summary>时序存储（InfluxDB 批量写入目标，唯一持久化出口）</summary>
    private readonly ITimeseriesStore _timeseries;
    /// <summary>实时推送接口（Api 用 SignalR 实现；缓存与推送共用同一接口）</summary>
    private readonly IRealtimeNotifier _notifier;
    /// <summary>服务作用域工厂：用于按操作解析 scoped 生命周期依赖（如 IAlarmStore），避免从根容器解析 scoped</summary>
    private readonly IServiceScopeFactory _scopeFactory;
    /// <summary>日志</summary>
    private readonly ILogger<MqttIngestHostedService> _logger;
    /// <summary>共享解析器（线程安全，单实例复用）</summary>
    private readonly MeasurementBatchParser _parser = new();
    /// <summary>测量消息处理管线（解析 → 去重 → 实时 → 攒批入队）</summary>
    private readonly MeasurementPipeline _pipeline;
    /// <summary>写失败重试队列（有界内存队列，drop-oldest）</summary>
    private readonly IngestRetryQueue _retryQueue;
    /// <summary>MQTT 连接/订阅重试策略（Polly 8，默认 3 次重试）</summary>
    private readonly MqttConnectRetryPolicy _connectRetry;

    // 攒批缓冲：MQTT 消息处理器线程追加记录，flush 循环线程按批取走。
    // 属跨线程共享的可变状态，所有读写都须持 _pendingLock；用 lock 而非 Channel 是为了攒批入队回调
    // 简单同步（锁竞争窗口极小，且 flush 间隔内几乎无竞争）。
    private readonly List<MeasurementRecord> _pending = new();
    private readonly object _pendingLock = new();

    // MQTT 客户端：在 ExecuteAsync 内创建并持有；StopAsync 时正常断开并释放。可空以允许构造阶段无客户端。
    private MqttNet.IMqttClient? _client;
    // 断线信号：DisconnectedAsync 事件触发 TrySetResult 使「等待连接存活」的循环退出进入重连。
    // 每次进入连接循环前重建新实例（TaskCompletionSource 只能 Set 一次），保证每轮等待都是干净的未完成状态。
    private TaskCompletionSource _disconnected = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 创建 Ingest 宿主服务：从配置快照装配各组件。
    /// 数值钳制：去重窗口、重试基础退避、flush 间隔、重连延迟均至少为 1（防 0/负数导致异常或空转）；
    /// 连接重试次数至少为 0（0 = 关闭重试直通）。
    /// </summary>
    /// <param name="options">Ingest 配置（经 <see cref="IngestOptions"/> 绑定 + 启动校验）</param>
    /// <param name="timeseries">时序存储（写 InfluxDB）</param>
    /// <param name="cache">最近值缓存（注入管线，实时面板读内存）</param>
    /// <param name="notifier">实时推送接口（SignalR 实现）</param>
    /// <param name="scopeFactory">作用域工厂（告警存储按操作解析）</param>
    /// <param name="logger">日志</param>
    public MqttIngestHostedService(
        IOptions<IngestOptions> options,
        ITimeseriesStore timeseries,
        ILatestValueCache cache,
        IRealtimeNotifier notifier,
        IServiceScopeFactory scopeFactory,
        ILogger<MqttIngestHostedService> logger)
    {
        _options = options.Value;
        _timeseries = timeseries;
        _notifier = notifier;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pipeline = new MeasurementPipeline(
            _parser,
            new BatchDeduplicator(TimeSpan.FromSeconds(Math.Max(1, _options.DedupeTtlSeconds))),
            cache,
            notifier,
            logger,
            records => { lock (_pendingLock) _pending.AddRange(records); });
        _retryQueue = new IngestRetryQueue(
            _options.RetryQueueCapacity,
            _options.RetryMaxAttempts,
            TimeSpan.FromSeconds(Math.Max(1, _options.RetryBaseBackoffSeconds)));
        _connectRetry = new MqttConnectRetryPolicy(
            _options.MqttConnectMaxRetries,
            TimeSpan.FromMilliseconds(Math.Max(1, _options.ReconnectDelayMs)),
            logger);
    }

    /// <summary>
    /// 后台服务主入口：创建 MQTT 客户端并挂接事件 → 并行启动三个循环（MQTT 收发 / flush / retry drain）。
    /// 任一循环因取消正常退出后，<see cref="Task.WhenAll"/> 会等待其余循环随同取消退出，保证整体干净停机。
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _client = new MqttNet.MqttClientFactory().CreateMqttClient();
        _client.DisconnectedAsync += e =>
        {
            _disconnected.TrySetResult();
            return Task.CompletedTask;
        };
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;

        var mqttLoop = RunMqttLoopAsync(stoppingToken);
        var flushLoop = RunFlushLoopAsync(stoppingToken);
        var retryLoop = RunRetryLoopAsync(stoppingToken);

        await Task.WhenAll(mqttLoop, flushLoop, retryLoop);
    }

    /// <summary>
    /// 停止服务：先等基类（含 ExecuteAsync 三循环随 stoppingToken 退出），再对 MQTT 客户端做正常断开并释放。
    /// 断开原因显式指定 <see cref="MqttNet.MqttClientDisconnectOptionsReason.NormalDisconnection"/>，
    /// broker 侧可见为优雅下线，不触发会话残留。
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_client is not null)
        {
            await _client.DisconnectAsync(new MqttNet.MqttClientDisconnectOptions
            {
                Reason = MqttNet.MqttClientDisconnectOptionsReason.NormalDisconnection
            });
            _client.Dispose();
        }
    }

    // ═══════ MQTT 收发循环（断线重连） ═══════

    /// <summary>
    /// MQTT 收发主循环：每轮「连接 + 订阅（带重试）→ 阻塞等待存活 → 断开后延迟重连」。
    /// 连接/订阅失败按 <see cref="MqttConnectRetryPolicy"/> 重试（默认 3 次），
    /// 重试耗尽仍失败时记错误日志 + 累加连接失败指标，然后回到循环按重连延迟再尝试（broker 恢复后可自动接入）。
    /// 非取消异常被捕获记警告并继续循环，避免单次异常杀死整个接入服务。
    /// </summary>
    /// <param name="ct">停止令牌；置位时停止重连与等待，退出循环</param>
    private async Task RunMqttLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                // 连接 + 订阅（带状态判定），失败按 Polly 策略重试（默认 3 次）
                var result = await _connectRetry.ExecuteAsync(ConnectAndSubscribeAsync, ct);
                if (result.IsFailure)
                {
                    // 重试耗尽仍未连接成功：错误保存进 OperationalError + 错误日志 + 指标
                    CloudMetrics.MqttConnectFailureTotal.Inc();
                    _logger.LogError(
                        "Ingest MQTT 连接失败（已按策略重试 {RetryCount} 次，仍未连接成功）: {Error}",
                        _connectRetry.MaxRetries, result.Error);
                }
                else
                {
                    _logger.LogInformation("Ingest MQTT 已连接 {Host}:{Port}（clientId={ClientId}）",
                        _options.MqttHost, _options.MqttPort, _options.ClientId);

                    // 阻塞直到断线或取消（DisconnectedAsync 触发 TCS）
                    await _disconnected.Task.WaitAsync(ct);
                    _logger.LogWarning("Ingest MQTT 连接断开，准备重连");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ingest MQTT 连接/订阅意外异常");
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
    /// 连接 broker + 订阅（带连接状态判定）：
    /// 成功 = 已连接并订阅成功，返回 <see cref="OperationResult.Success"/>；
    /// 失败 = 返回携带 <see cref="OperationalError"/>（Communication）的失败结果，不抛异常（供 Polly 重试）。
    /// 取消令牌触发时原样抛出 <see cref="OperationCanceledException"/>。
    /// </summary>
    /// <param name="ct">取消令牌；连接/订阅任一环节取消时原样上抛</param>
    private async Task<OperationResult> ConnectAndSubscribeAsync(CancellationToken ct)
    {
        try
        {
            var options = new MqttNet.MqttClientOptionsBuilder()
                .WithTcpServer(_options.MqttHost, _options.MqttPort)
                .WithClientId(_options.ClientId)
                .WithCleanStart()
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
                .Build();

            // 状态判定：broker 返回非 Success（如凭据错误/标识拒绝）也视为连接失败
            var result = await _client!.ConnectAsync(options, ct);
            if (result.ResultCode != MqttNet.MqttClientConnectResultCode.Success)
                return OperationalError.Communication($"MQTT 接入连接失败: {result.ResultCode} - {result.ReasonString}");

            await SubscribeAsync(ct);
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
    /// <param name="ct">取消令牌</param>
    private async Task SubscribeAsync(CancellationToken ct)
    {
        var subscribe = new MqttNet.MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(TopicUtil.MeasurementsSubscription, MqttNet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .WithTopicFilter(TopicUtil.AlarmsSubscription, MqttNet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        var result = await _client!.SubscribeAsync(subscribe, ct);
        _logger.LogInformation("Ingest 订阅 {Measurements} / {Alarms}（QoS1）",
            TopicUtil.MeasurementsSubscription, TopicUtil.AlarmsSubscription);
    }

    // ═══════ 消息处理 ═══════

    /// <summary>
    /// MQTT 消息回调：解析 topic → 按类型分发（测量走管线、告警直接落库）。
    /// 单条消息处理异常被捕获记错误日志，不影响后续消息（一条坏消息不拖垮整个接收循环）。
    /// 载荷读取在 try 之外执行：<see cref="ReadPayload"/> 为纯拷贝、不抛业务异常。
    /// </summary>
    private async Task OnMessageReceivedAsync(MqttNet.MqttApplicationMessageReceivedEventArgs e)
    {
        var topic = e.ApplicationMessage.Topic;
        var payload = ReadPayload(e.ApplicationMessage.Payload);

        try
        {
            var parsed = TopicUtil.Parse(topic);
            if (parsed is null)
            {
                _logger.LogWarning("忽略无法解析的 topic: {Topic}", topic);
                return;
            }

            switch (parsed.Value.Kind)
            {
                case TopicKind.Measurements:
                    await HandleMeasurementsAsync(parsed.Value, payload);
                    break;
                case TopicKind.Alarms:
                    await HandleAlarmAsync(parsed.Value, payload);
                    break;
                default:
                    _logger.LogDebug("Ingest 不处理 {Kind} topic: {Topic}", parsed.Value.Kind, topic);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理消息异常 topic={Topic}", topic);
        }
    }

    /// <summary>
    /// 把 MQTT 消息载荷（ReadOnlySequence&lt;byte&gt;）逐段拷贝为字节数组
    /// （MQTTnet 5.x Payload 为分段序列，无 PayloadSegment 属性；逐段拷贝兼容任意分段布局）。
    /// 解析器与后续处理都按字节数组约定，故统一在此归一化。
    /// </summary>
    /// <param name="sequence">MQTT 消息载荷（可能由多个内存段组成）</param>
    private static byte[] ReadPayload(System.Buffers.ReadOnlySequence<byte> sequence)
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

    /// <summary>
    /// 测量消息分发：交给 <see cref="MeasurementPipeline.HandleAsync"/>（解析/去重/实时/攒批）。
    /// 保持 async 的薄封装：与告警路径对称、便于后续在分发点加过滤/审计而不改调用方。
    /// </summary>
    private async Task HandleMeasurementsAsync(TopicUtil.ParsedTopic topic, byte[] payload)
    {
        await _pipeline.HandleAsync(payload, topic.SiteId);
    }

    /// <summary>
    /// 告警消息处理：解析 → 构造 <see cref="AlarmRecord"/> → 落告警库 → 实时推送 → 计数。
    /// 映射回退：severity 解析失败回退 <see cref="AlarmSeverity.Warning"/>；state 非 "Resolved" 一律视为 Active
    /// （大小写不敏感，Resolved 为网关契约的固定值）。
    /// 站点归属以 topic 第三段为准（与测量一致），载荷内字段不再做冗余校验。
    /// </summary>
    private async Task HandleAlarmAsync(TopicUtil.ParsedTopic topic, byte[] payload)
    {
        var parseResult = _parser.ParseAlarm(payload, topic.SiteId);
        if (parseResult.IsFailure)
        {
            _logger.LogWarning("告警载荷解析失败（site={SiteId}）: {Error}", topic.SiteId, parseResult.Error?.Message);
            return;
        }

        var p = parseResult.Value!;
        var alarm = new AlarmRecord
        {
            Id = p.AlarmId,
            RuleId = p.RuleId,
            SiteId = topic.SiteId,
            DeviceId = p.DeviceId,
            PointId = p.PointId,
            TriggerValue = p.TriggerValue,
            Threshold = p.Threshold,
            Severity = Enum.TryParse<AlarmSeverity>(p.Severity, true, out var sev) ? sev : AlarmSeverity.Warning,
            Message = p.Message,
            State = p.State.Equals("Resolved", StringComparison.OrdinalIgnoreCase) ? AlarmState.Resolved : AlarmState.Active,
            OccurredAt = p.OccurredAt
        };

        // IAlarmStore 为 scoped 生命周期，单例宿主服务需按操作开 scope（避免从根容器解析 scoped）。
        using var scope = _scopeFactory.CreateScope();
        var alarmStore = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
        await alarmStore.AddAsync(alarm);
        await _notifier.NotifyAlarmAsync(alarm);
        CloudMetrics.AlarmReceivedTotal.Inc();
    }

    // ═══════ 批量 flush 循环 ═══════

    /// <summary>
    /// 批量写循环：按固定间隔从攒批缓冲取一批（至多 <see cref="IngestOptions.FlushBatchSize"/> 条）写入时序库。
    /// 写入失败 → 整批进重试队列（丢批次不丢记录，交给 drain 循环按退避重试）；成功则继续下一轮。
    /// 固定间隔 + 条数上限双约束，保证 InfluxDB 写入压力平稳、不因瞬时大流量打爆连接。
    /// </summary>
    /// <param name="ct">停止令牌；置位时退出循环</param>
    private async Task RunFlushLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _options.FlushIntervalSeconds)), ct);

            var batch = DrainPending(_options.FlushBatchSize);
            if (batch.Count == 0)
                continue;

            var result = await _timeseries.WriteAsync(batch, ct);
            if (result.IsFailure)
            {
                _logger.LogWarning("时序写入失败（{Count} 条），进重试队列: {Error}", batch.Count, result.Error?.Message);
                _retryQueue.TryEnqueue(batch);
            }
        }
    }

    /// <summary>
    /// 从攒批缓冲取走一批（至多 max 条）并清出缓冲，全程持锁。
    /// 返回新列表而非缓冲引用：写库/入队在锁外进行，不阻塞消息线程的入队追加。
    /// </summary>
    /// <param name="max">本批上限条数</param>
    private List<MeasurementRecord> DrainPending(int max)
    {
        lock (_pendingLock)
        {
            if (_pending.Count == 0)
                return new List<MeasurementRecord>();
            var take = _pending.Count <= max ? _pending.Count : max;
            var batch = _pending.GetRange(0, take);
            _pending.RemoveRange(0, take);
            return batch;
        }
    }

    // ═══════ 重试 drain 循环 ═══════

    /// <summary>
    /// 重试 drain 循环：每 500ms 轮询一次重试队列。
    /// 本轮先取空队列，区分「到期可重试」与「未到期需放回」：未到期条目保留原重试次数与时间、轮末统一放回，
    /// 避免「放回后下一轮立即又取出」造成空转忙轮询。
    /// 到期条目重试写库：成功即弃；失败且未超限则按指数退避算下次时间、Attempt+1 后放回；
    /// 失败且超限则丢弃并记错误日志 + 丢弃指标。
    /// </summary>
    /// <param name="ct">停止令牌；置位时退出循环</param>
    private async Task RunRetryLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(500, ct);

            // 本轮先取空队列，区分「到期可重试」与「未到期需放回」，避免放回后立即再取出造成空转。
            var toRequeue = new List<IngestRetryItem>();
            while (_retryQueue.TryDequeue(out var item))
            {
                if (item.NextAttemptAt > DateTime.UtcNow)
                {
                    toRequeue.Add(item); // 未到期，保留原重试次数与时间，轮末统一放回
                    continue;
                }

                var result = await _timeseries.WriteAsync(item.Batch, ct);
                if (result.IsSuccess)
                    continue;

                if (_retryQueue.IsExhausted(item.Attempt + 1))
                {
                    CloudMetrics.IngestDroppedTotal.Inc();
                    _logger.LogError("批次重试超限（{Attempt} 次），丢弃 {Count} 条: {Error}",
                        item.Attempt, item.Batch.Count, result.Error?.Message);
                    continue;
                }

                var next = _retryQueue.ComputeNextAttemptAt(item.Attempt);
                toRequeue.Add(new IngestRetryItem(item.Batch, item.Attempt + 1, next));
            }

            foreach (var item in toRequeue)
                _retryQueue.TryEnqueue(item);

            if (toRequeue.Count > 0)
                CloudMetrics.IngestRetryQueueDepth.Set(_retryQueue.Count);
        }
    }
}
