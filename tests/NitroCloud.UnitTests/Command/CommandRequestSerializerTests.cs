using System.Text.Json;
using NitroCloud.Command;
using NitroCloud.Domain.Commands;

namespace NitroCloud.UnitTests.Command;

/// <summary>
/// 下行命令发布载荷序列化单测（ADR-010 D5 / DESIGN.md §4.3）：
/// 契约字段（commandId/type/pointId/value/requestedAt）齐全且 camelCase。
/// </summary>
public class CommandRequestSerializerTests
{
    [Fact]
    public void Serialize_UsesCamelCase_AllContractFields()
    {
        var request = new CommandRequest
        {
            CommandId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Type = "WritePoint",
            PointId = "22222222-2222-2222-2222-222222222222",
            Value = 42.5,
            RequestedAt = new DateTime(2026, 8, 27, 1, 0, 0, DateTimeKind.Utc)
        };

        using var doc = JsonDocument.Parse(CommandRequestSerializer.SerializeToString(request));
        var root = doc.RootElement;

        Assert.Equal("11111111-1111-1111-1111-111111111111", root.GetProperty("commandId").GetString());
        Assert.Equal("WritePoint", root.GetProperty("type").GetString());
        Assert.Equal("22222222-2222-2222-2222-222222222222", root.GetProperty("pointId").GetString());
        Assert.Equal(42.5, root.GetProperty("value").GetDouble());
        Assert.Equal("2026-08-27T01:00:00Z", root.GetProperty("requestedAt").GetString());
    }

    [Fact]
    public void SerializeToBytes_ProducesUtf8Json()
    {
        var request = new CommandRequest
        {
            CommandId = Guid.NewGuid(),
            Type = "WritePoint",
            PointId = "p-1",
            Value = 0,
            RequestedAt = DateTime.UtcNow
        };

        var bytes = CommandRequestSerializer.Serialize(request);

        Assert.True(bytes.Length > 0);
        using var doc = JsonDocument.Parse(bytes);
        Assert.True(doc.RootElement.TryGetProperty("commandId", out _));
    }
}
