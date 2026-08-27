using System.Text.Json;
using System.Text.Json.Serialization;
using NitroCloud.Domain.Commands;
using NitroCloud.Shared;

namespace NitroCloud.Command;

/// <summary>
/// 命令回执载荷解析器（ADR-010 D5/D8：commands/ack JSON，DESIGN.md §4.3）。
/// 职责边界：只做「JSON → 领域模型 <see cref="CommandAck"/>」转换与契约校验，业务处理（幂等/状态机）在
/// <see cref="CommandManager"/>。解析失败返回 <see cref="OperationalError"/>（Protocol），不抛异常。
/// </summary>
public sealed class CommandAckParser
{
    /// <summary>
    /// 共享 JSON 反序列化选项：camelCase；at 用 DateTimeOffset 承载（带时区偏移，统一转 UTC，防本机时区漂移）。
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// 解析回执载荷（DESIGN.md §4.3：commandId/result/error/at）。
    /// 校验：commandId 非空、result 为合法 <see cref="CommandResult"/>（Success/Failure）。
    /// </summary>
    /// <param name="payload">MQTT 载荷字节（UTF-8 JSON）</param>
    /// <returns>成功返回 <see cref="CommandAck"/>（At 归一为 UTC）；失败返回 Protocol 类错误</returns>
    public OperationResult<CommandAck> Parse(ReadOnlySpan<byte> payload)
    {
        try
        {
            var p = JsonSerializer.Deserialize<CommandAckPayload>(payload, Options);
            if (p is null)
                return OperationalError.Protocol("命令回执载荷为空或不是合法 JSON");
            if (p.CommandId == Guid.Empty)
                return OperationalError.Protocol("命令回执缺少 commandId");
            if (!Enum.TryParse<CommandResult>(p.Result, true, out var result))
                return OperationalError.Protocol($"命令回执 result 非法: {p.Result}");

            return new CommandAck
            {
                CommandId = p.CommandId,
                Result = result,
                Error = p.Error ?? "",
                At = p.At == default ? DateTime.UtcNow : p.At.UtcDateTime
            };
        }
        catch (JsonException ex)
        {
            return OperationalError.Protocol($"命令回执 JSON 解析失败: {ex.Message}");
        }
        catch (Exception ex)
        {
            return OperationalError.Protocol($"命令回执解析异常: {ex.Message}");
        }
    }
}

/// <summary>
/// 命令回执 JSON 载荷（DESIGN.md §4.3，camelCase）。
/// at 用 DateTimeOffset 承载，解析后由 <see cref="CommandAckParser"/> 归一为 UTC。
/// </summary>
internal sealed record CommandAckPayload
{
    /// <summary>对应命令 ID（网关按此去重）</summary>
    [JsonPropertyName("commandId")]
    public Guid CommandId { get; init; }

    /// <summary>执行结果（Success/Failure）</summary>
    [JsonPropertyName("result")]
    public string Result { get; init; } = "";

    /// <summary>失败原因（Success 时为空）</summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>回执时间（ISO 8601，带时区偏移）</summary>
    [JsonPropertyName("at")]
    public DateTimeOffset At { get; init; }
}
