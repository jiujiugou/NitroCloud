using Prometheus;

namespace NitroCloud.Telemetry;

/// <summary>
/// 云平台 Prometheus 指标（ADR-008 D8：ingest 吞吐/延迟/队列深度/丢弃、命令 sent/ack/timeout、SignalR 连接数）。
/// 全局静态注册表，/metrics 端点由 ASP.NET Core 暴露（prometheus-net.AspNetCore）。
/// </summary>
public static class CloudMetrics
{
    /// <summary>Ingest 已处理测量批次总数（label: result = success/parse_failed/deduped）</summary>
    public static readonly Counter IngestBatchesTotal = Metrics.CreateCounter(
        "cloud_ingest_batches_total",
        "已处理的测量批次总数",
        new CounterConfiguration { LabelNames = ["result"] });

    /// <summary>Ingest 已处理测量记录总数（label: result = stored/skipped_non_numeric）</summary>
    public static readonly Counter IngestRecordsTotal = Metrics.CreateCounter(
        "cloud_ingest_records_total",
        "已处理的测量记录总数",
        new CounterConfiguration { LabelNames = ["result"] });

    /// <summary>Ingest 重试队列当前深度</summary>
    public static readonly Gauge IngestRetryQueueDepth = Metrics.CreateGauge(
        "cloud_ingest_retry_queue_depth",
        "Ingest 写失败重试队列当前深度");

    /// <summary>Ingest 因队列满被丢弃的批次总数</summary>
    public static readonly Counter IngestDroppedTotal = Metrics.CreateCounter(
        "cloud_ingest_dropped_total",
        "重试队列满时被丢弃的批次总数");

    /// <summary>Ingest 实时路径（最近值缓存/推送）失败次数（best-effort，不阻塞持久化）</summary>
    public static readonly Counter IngestRealtimeFailureTotal = Metrics.CreateCounter(
        "cloud_ingest_realtime_failure_total",
        "Ingest 实时路径（最近值缓存/推送）失败次数");

    /// <summary>告警入库总数</summary>
    public static readonly Counter AlarmReceivedTotal = Metrics.CreateCounter(
        "cloud_alarm_received_total",
        "收到的上行告警总数");

    /// <summary>Ingest MQTT 连接失败（Polly 重试耗尽仍未连接成功）总次数</summary>
    public static readonly Counter MqttConnectFailureTotal = Metrics.CreateCounter(
        "cloud_mqtt_connect_failure_total",
        "Ingest MQTT 连接失败（重试耗尽仍未成功）总次数");

    /// <summary>命令发送总数（label: result = sent/publish_failed）</summary>
    public static readonly Counter CommandSentTotal = Metrics.CreateCounter(
        "cloud_command_sent_total",
        "命令发布总数",
        new CounterConfiguration { LabelNames = ["result"] });

    /// <summary>命令回执总数（label: result = success/failure）</summary>
    public static readonly Counter CommandAckTotal = Metrics.CreateCounter(
        "cloud_command_ack_total",
        "命令回执总数",
        new CounterConfiguration { LabelNames = ["result"] });

    /// <summary>命令超时总数</summary>
    public static readonly Counter CommandTimeoutTotal = Metrics.CreateCounter(
        "cloud_command_timeout_total",
        "命令超时重试超上限总数");

    /// <summary>SignalR 当前连接数</summary>
    public static readonly Gauge SignalRConnections = Metrics.CreateGauge(
        "cloud_signalr_connections",
        "SignalR 当前连接数");
}
