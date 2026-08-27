using System.Text.Json;
using NitroCloud.Domain.Commands;

namespace NitroCloud.Command;

/// <summary>
/// 下行命令发布载荷序列化（ADR-010 D5：DESIGN.md §4.3 CommandRequest JSON，camelCase）。
/// 与 Ingest 的解析器对称；Command 模块不跨模块引用 Ingest，故本地维护序列化选项。
/// </summary>
public static class CommandRequestSerializer
{
    /// <summary>
    /// 共享 JSON 序列化选项：camelCase（与前端 types.ts / 网关契约一致）。
    /// 静态只读可跨线程并发复用（System.Text.Json 保证只读选项并发安全）。
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>把命令请求序列化为 UTF-8 字节（MQTT 载荷）</summary>
    public static byte[] Serialize(CommandRequest request)
        => JsonSerializer.SerializeToUtf8Bytes(request, Options);

    /// <summary>把命令请求序列化为 JSON 字符串（测试/日志用）</summary>
    public static string SerializeToString(CommandRequest request)
        => JsonSerializer.Serialize(request, Options);
}
