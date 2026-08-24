using System.Text.Json;
using System.Text.Json.Serialization;
using NitroCloud.Domain.Measurements;
using NitroCloud.Shared;

namespace NitroCloud.Ingest.Parsing;

/// <summary>
/// 上行测量载荷解析器（ADR-008 D4：无 v 字段按 v1 兼容解析；载荷 siteId 与 topic 第三段冗余校验）。
/// 职责边界：只做「JSON → 领域模型」的转换与冗余校验，不涉及业务判定（去重/缓存/写库由
/// <c>MeasurementPipeline</c> 负责）。解析产物 = <see cref="ParsedBatch"/>（含批次 + 警告列表）。
///
/// 设计要点：
/// - 无 v 字段（反序列化得 0）按 v1 兼容解析，保证网关旧版载荷可接入；
/// - 载荷顶层 siteId 仅作冗余校验（ADR-004），以 topic 第三段为唯一事实来源，并在解析期注入每条 record，
///   供最近值缓存与 InfluxDB 按站点维度隔离；
/// - 解析失败返回失败结果（不抛异常），由调用方决定日志/丢弃策略，热路径不因坏包中断。
/// </summary>
public sealed class MeasurementBatchParser
{
    /// <summary>
    /// 共享 JSON 反序列化选项，静态只读可跨线程并发复用（System.Text.Json 保证只读选项并发安全）。
    /// - <see cref="JsonNamingPolicy.CamelCase"/>：网关载荷字段为 camelCase（如 <c>siteId</c>、<c>deviceId</c>）；
    /// - <see cref="UtcDateTimeConverter"/>：把带时区偏移的时间戳归一为 UTC，避免本机时区导致入库时间漂移；
    /// - <see cref="JsonStringEnumConverter"/>：网关序列化默认把枚举写成数字（如 <c>"dataType":8</c>），
    ///   DESIGN.md 样例又用字符串（<c>"Float"</c>），该转换器同时兼容两者。
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new UtcDateTimeConverter(), new JsonStringEnumConverter() }
    };

    /// <summary>
    /// 解析结果：成功时 <see cref="Batch"/> 非空并携带批次与警告列表；失败时 <see cref="Batch"/> 为 null 且
    /// <see cref="Error"/> 非空（记录人类可读原因，供日志输出）。
    /// </summary>
    public sealed record ParsedBatch(BatchMeasurements? Batch, IReadOnlyList<string> Warnings)
    {
        /// <summary>是否解析成功（等价于 <see cref="Batch"/> 非空）</summary>
        public bool IsSuccess => Batch is not null;

        /// <summary>解析失败原因（成功时为 null）</summary>
        public string? Error { get; init; }
    }

    /// <summary>
    /// 把 MQTT 上行载荷解析为 <see cref="BatchMeasurements"/>（v1 契约）。
    /// 处理步骤：反序列化 → 站点冗余校验（不一致记警告，不失败）→ 旧版无 v 字段按 v1 兼容 →
    /// 把 topic 第三段 siteId 注入每条 record。任一步失败均返回携带 <see cref="ParsedBatch.Error"/> 的失败结果。
    /// </summary>
    /// <param name="payload">MQTT 载荷字节（UTF-8 JSON）</param>
    /// <param name="topicSiteId">topic 第三段 siteId（ADR-004 第一隔离维度；作冗余校验基准 + 注入记录）</param>
    /// <returns>
    /// 成功：<see cref="ParsedBatch.IsSuccess"/>=true，<see cref="ParsedBatch.Batch"/> 的每个 record.SiteId
    /// 均已以 topic 第三段为准注入；<see cref="ParsedBatch.Warnings"/> 可能含载荷/topic 不一致警告。
    /// 失败：<see cref="ParsedBatch.IsSuccess"/>=false，<see cref="ParsedBatch.Error"/> 非空（坏 JSON / 缺必填字段等）。
    /// </returns>
    public ParsedBatch Parse(ReadOnlySpan<byte> payload, string topicSiteId)
    {
        try
        {
            var batch = JsonSerializer.Deserialize<BatchMeasurements>(payload, Options);
            if (batch is null)
                return Failed("载荷为空或不是合法 JSON");

            var warnings = new List<string>();
            // 冗余校验（ADR-004）：载荷 siteId 与 topic 第三段不一致 → 记警告，不静默丢弃
            if (!string.IsNullOrEmpty(batch.SiteId) && batch.SiteId != topicSiteId)
                warnings.Add($"载荷 siteId({batch.SiteId}) 与 topic siteId({topicSiteId}) 不一致");

            // 兼容旧版：无 v 字段（反序列化得 0）按 v1 处理
            if (batch.V == 0)
                batch = batch with { V = 1 };

            // 记录级冗余：把 topic 第三段 siteId 注入每条 record（ADR-004：以 topic 为准），
            // 供最近值缓存与 InfluxDB 写库按站点维度隔离。
            if (batch.Records.Count > 0)
            {
                batch = batch with
                {
                    Records = batch.Records
                        .Select(r => r with { SiteId = topicSiteId })
                        .ToList()
                };
            }

            return new ParsedBatch(batch, warnings);
        }
        catch (JsonException ex)
        {
            return Failed($"测量载荷 JSON 解析失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Failed($"测量载荷解析异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 解析上行告警载荷为 <see cref="AlarmPayload"/>（DESIGN.md §4.2 契约）。
    /// 告警与测量共用同一套 JSON 选项（camelCase + UTC + 枚举双兼容）。
    /// </summary>
    /// <param name="payload">MQTT 载荷字节（UTF-8 JSON）</param>
    /// <param name="topicSiteId">topic 第三段 siteId（当前仅透传/预留，告警站点归属由调用方按 topic 提取）</param>
    /// <returns>成功返回 <see cref="AlarmPayload"/>；失败返回 Protocol 类 <see cref="OperationalError"/>（坏 JSON / 缺 alarmId）</returns>
    public OperationResult<AlarmPayload> ParseAlarm(ReadOnlySpan<byte> payload, string topicSiteId)
    {
        try
        {
            var alarm = JsonSerializer.Deserialize<AlarmPayload>(payload, Options);
            if (alarm is null || string.IsNullOrWhiteSpace(alarm.AlarmId))
                return OperationalError.Protocol("告警载荷缺少 alarmId");
            return alarm;
        }
        catch (JsonException ex)
        {
            return OperationalError.Protocol($"告警载荷 JSON 解析失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            return OperationalError.Protocol($"告警载荷解析异常: {ex.Message}");
        }
    }

    /// <summary>构造失败结果（Batch 为 null，Error 记录原因）</summary>
    private static ParsedBatch Failed(string error) => new(null, Array.Empty<string>()) { Error = error };
}
