using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NitroCloud.Ingest.Parsing;

/// <summary>
/// JSON DateTime 转换器：一律把带时区偏移的时间戳归一为 UTC（Kind=Utc）。
/// 避免 System.Text.Json 默认把带偏移时间转成本机本地时间导致入库时间漂移（云侧统一 UTC 约定）。
/// 接入 <see cref="MeasurementBatchParser.Options"/> 的 Converters 集合，供测量/告警载荷共用。
///
/// 边界约定：
/// - 输入支持 ISO 8601 带偏移字符串（如 <c>2026-08-23T01:00:00+08:00</c>），统一转 UTC 存储；
/// - 空/空白字符串按「当前 UTC 时间」兜底（容忍缺省时间戳，避免整包解析失败）；
/// - 写回（Write）统一输出 <c>yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'</c>（"O" 格式，无偏移、含小数秒），
///   保证序列化/反序列化往返稳定。
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    /// <inheritdoc />
    /// <remarks>严格按 <see cref="DateTimeOffset.Parse(string, IFormatProvider)"/> 解析，失败抛
    /// <see cref="JsonException"/> 由上层解析器捕获转失败结果。</remarks>
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        if (string.IsNullOrWhiteSpace(s))
            return DateTime.UtcNow;
        return DateTimeOffset.Parse(s, CultureInfo.InvariantCulture).UtcDateTime;
    }

    /// <inheritdoc />
    /// <remarks>输出前统一 <see cref="DateTime.ToUniversalTime"/>，避免本地时间写回带偏移。</remarks>
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToUniversalTime().ToString("O"));
}
