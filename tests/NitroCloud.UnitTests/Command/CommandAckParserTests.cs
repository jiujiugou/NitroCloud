using System.Text;
using NitroCloud.Command;
using NitroCloud.Domain.Commands;
using NitroCloud.Shared;

namespace NitroCloud.UnitTests.Command;

/// <summary>
/// 回执载荷解析单测（ADR-010 D5/D8 / DESIGN.md §4.3 commands/ack）：
/// 合法回执、缺 commandId、非法 result、时间归一 UTC。
/// </summary>
public class CommandAckParserTests
{
    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    private static readonly Guid AckId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Parse_ValidSuccessAck_ReturnsSuccess_UtcNormalized()
    {
        var payload = """{"commandId":"11111111-1111-1111-1111-111111111111","result":"Success","error":"","at":"2026-08-27T10:05:00.200+08:00"}""";

        var result = new CommandAckParser().Parse(Utf8(payload));

        Assert.True(result.IsSuccess);
        var ack = result.Value!;
        Assert.Equal(AckId, ack.CommandId);
        Assert.Equal(CommandResult.Success, ack.Result);
        Assert.Equal("", ack.Error);
        // +08:00 → UTC 归一：10:05 本地 = 02:05 UTC
        Assert.Equal(new DateTime(2026, 8, 27, 2, 5, 0, 200, DateTimeKind.Utc), ack.At);
        Assert.Equal(DateTimeKind.Utc, ack.At.Kind);
    }

    [Fact]
    public void Parse_MissingCommandId_ReturnsProtocolError()
    {
        var result = new CommandAckParser().Parse(Utf8("""{"result":"Success"}"""));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Protocol, result.Error!.Category);
    }

    [Fact]
    public void Parse_InvalidResult_ReturnsProtocolError()
    {
        var result = new CommandAckParser().Parse(Utf8("""{"commandId":"11111111-1111-1111-1111-111111111111","result":"Maybe"}"""));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Protocol, result.Error!.Category);
    }

    [Fact]
    public void Parse_FailureResult_CarriesError()
    {
        var payload = """{"commandId":"11111111-1111-1111-1111-111111111111","result":"Failure","error":"PLC no response","at":"2026-08-27T02:00:00Z"}""";

        var result = new CommandAckParser().Parse(Utf8(payload));

        Assert.True(result.IsSuccess);
        var ack = result.Value!;
        Assert.Equal(CommandResult.Failure, ack.Result);
        Assert.Equal("PLC no response", ack.Error);
    }

    [Fact]
    public void Parse_NonJson_ReturnsProtocolError()
    {
        var result = new CommandAckParser().Parse(Utf8("not json"));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCategory.Protocol, result.Error!.Category);
    }
}
