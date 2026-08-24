namespace NitroCloud.Ingest;

/// <summary>
/// Ingest 接入配置（绑定 appsettings 的 <c>Ingest</c> 段，环境变量 <c>Ingest__*</c> 可覆盖）。
/// 订阅 topic 由 ADR-008 D5 契约固定为 measurements/alarms 通配，不在本类承载；本类只放连接 / 吞吐 / 重试参数。
///
/// 数值边界：本类保持配置原样不校验，由消费方在构造时钳制——
/// <see cref="ReconnectDelayMs"/>、<see cref="DedupeTtlSeconds"/>、<see cref="FlushIntervalSeconds"/> 下限钳制为 1；
/// <see cref="MqttConnectMaxRetries"/> 下限钳制为 0（0 = 关闭重试直通）；<see cref="RetryMaxAttempts"/> 下限钳制为 1。
/// </summary>
public sealed class IngestOptions
{
    /// <summary>MQTT broker 主机名 / IP（默认 <c>localhost</c>，EMQX / mosquitto 均适用）</summary>
    public string MqttHost { get; set; } = "localhost";

    /// <summary>MQTT broker 端口（默认 1883，标准明文端口）</summary>
    public int MqttPort { get; set; } = 1883;

    /// <summary>
    /// MQTT ClientId（默认 <c>nitrocloud-ingest-{进程号}</c>）。
    /// 借进程号保证多实例 / 重启时 clientId 唯一，避免 broker 因同 id 互踢（session takeover）导致连接反复断开。
    /// </summary>
    public string ClientId { get; set; } = $"nitrocloud-ingest-{Environment.ProcessId}";

    /// <summary>
    /// 断线重连延迟毫秒（默认 3000）。兼作两处用途：
    /// ① 连接/订阅失败的单次重试间隔（固定退避，见 <see cref="MqttConnectRetryPolicy"/>）；
    /// ② 连接断开后、等待下次重连的延迟（<see cref="MqttIngestHostedService.RunMqttLoopAsync"/>）。
    /// 消费方钳制为至少 1ms。
    /// </summary>
    public int ReconnectDelayMs { get; set; } = 3000;

    /// <summary>
    /// MQTT 连接/订阅失败最大重试次数（默认 3，Polly 重试；首次尝试不计入，即最多 4 次尝试）。
    /// 重试耗尽仍失败时，把 Communication 类 <see cref="OperationalError"/> 装入 <see cref="OperationResult"/> 返回，
    /// 宿主服务据此记错误日志并累加 <c>cloud_mqtt_connect_failure_total</c> 指标。
    /// 设为 0 可关闭重试（Polly 直通执行，仅一次尝试）。
    /// </summary>
    public int MqttConnectMaxRetries { get; set; } = 3;

    /// <summary>
    /// 批次去重窗口秒（ADR-008 D5，默认 60）。
    /// QoS1 重复投递的同一 batchId 在此窗口内被丢弃；窗口过小易漏去重（重复数据进库），
    /// 过大易误杀合法批次（同 id 长间隔重新出现）。消费方钳制为至少 1 秒。
    /// </summary>
    public int DedupeTtlSeconds { get; set; } = 60;

    /// <summary>
    /// 攒批条数（默认 200）：flush 循环每次最多取走该数量记录写入时序库，
    /// 减小单次 InfluxDB 批量写压力。与 <see cref="FlushIntervalSeconds"/> 共同决定落库节奏（先到先触发）。
    /// </summary>
    public int FlushBatchSize { get; set; } = 200;

    /// <summary>
    /// 攒批 flush 间隔秒（默认 1）：flush 循环按固定间隔扫描一次缓冲，
    /// 缓冲非空即取 <see cref="FlushBatchSize"/> 条写时序库。消费方钳制为至少 1 秒。
    /// </summary>
    public int FlushIntervalSeconds { get; set; } = 1;

    /// <summary>
    /// 写失败重试队列上限（批次，默认 10_000）。
    /// 底层有界 Channel（DropOldest）：满则丢弃最旧批次并累加丢弃指标，防内存无界膨胀（ADR-008 D5 降级策略）。
    /// </summary>
    public int RetryQueueCapacity { get; set; } = 10_000;

    /// <summary>
    /// 单批最大重试次数（默认 3）：超过则丢弃该批次并记错误日志 + 丢弃指标。
    /// 消费方钳制为至少 1。
    /// </summary>
    public int RetryMaxAttempts { get; set; } = 3;

    /// <summary>
    /// 重试基础退避秒（默认 1）：第 n 次重试间隔 = base * 2^(n-1)（指数退避）。
    /// 基数在 <see cref="IngestRetryQueue.ComputeNextAttemptAt"/> 内被钳制到最多 1 天，指数位被钳制到 2^20，
    /// 防 <see cref="TimeSpan"/> 溢出。消费方钳制为至少 1 秒。
    /// </summary>
    public int RetryBaseBackoffSeconds { get; set; } = 1;
}
