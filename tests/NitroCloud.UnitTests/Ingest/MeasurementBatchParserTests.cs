using System.Text;
using System.Text.Json;
using NitroCloud.Domain.Devices;
using NitroCloud.Domain.Measurements;
using NitroCloud.Ingest.Parsing;

namespace NitroCloud.UnitTests.Ingest;

/// <summary>
/// MeasurementBatchParser 上行载荷解析单测（ADR-008 D4）：冗余校验、无 v 字段兼容、枚举双兼容、告警解析。
/// </summary>
public class MeasurementBatchParserTests
{
    private static readonly Guid DeviceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DevicePointId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    /// <summary>
    /// 构造一批测量 JSON；v=null 表示缺省 v 字段；dataType/quality/timestamp 可注入以覆盖枚举双兼容与偏移时区。
    /// </summary>
    private static string BatchJson(string? v = "1", string siteId = "site-1",
        string dataType = "Float", string quality = "Good", string recordSiteId = "site-1",
        string timestamp = "2026-08-23T01:00:00Z")
    {
        var vField = v is null ? "" : $"\"v\": {v},";
        return $$"""
            {
              {{vField}}
              "siteId": "{{siteId}}",
              "id": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
              "deviceId": "{{DeviceId}}",
              "scanStartedAt": "2026-08-23T01:00:00Z",
              "scanCompletedAt": "2026-08-23T01:00:00.500Z",
              "records": [
                {
                  "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                  "deviceId": "{{DeviceId}}",
                  "devicePointId": "{{DevicePointId}}",
                  "siteId": "{{recordSiteId}}",
                  "pointName": "Temp",
                  "value": 23.5,
                  "dataType": "{{dataType}}",
                  "timestamp": "{{timestamp}}",
                  "receivedAt": "2026-08-23T01:00:00.100Z",
                  "quality": "{{quality}}"
                }
              ]
            }
            """;
    }

    [Fact]
    public void Parse_ValidBatch_ReturnsBatchWithTopicSiteInjected()
    {
        var parser = new MeasurementBatchParser();
        var result = parser.Parse(Utf8(BatchJson()), "site-1");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.NotNull(result.Batch);
        Assert.Equal(1, result.Batch.V);
        Assert.Empty(result.Warnings);

        var record = Assert.Single(result.Batch.Records);
        Assert.Equal("site-1", record.SiteId);
        Assert.Equal(DeviceId, record.DeviceId);
        Assert.Equal(DevicePointId, record.DevicePointId);
        Assert.Equal("Temp", record.PointName);
        Assert.Equal(23.5, ((JsonElement)record.Value!).GetDouble());
        Assert.Equal(DataType.Float, record.DataType);
        Assert.Equal(Quality.Good, record.Quality);
        Assert.Equal(new DateTime(2026, 8, 23, 1, 0, 0, DateTimeKind.Utc), record.Timestamp);
    }

    [Fact]
    public void Parse_Version_ZeroOrMissing_UpgradedToV1()
    {
        var parser = new MeasurementBatchParser();

        // 显式 v=0（网关旧版本可能发 0）→ 自动升级 v1
        var zero = parser.Parse(Utf8(BatchJson(v: "0")), "site-1");
        Assert.True(zero.IsSuccess);
        Assert.Equal(1, zero.Batch!.V);

        // 缺省 v 字段 → 默认即 v1
        var missing = parser.Parse(Utf8(BatchJson(v: null)), "site-1");
        Assert.True(missing.IsSuccess);
        Assert.Equal(1, missing.Batch!.V);
    }

    [Fact]
    public void Parse_PayloadSiteMismatch_WarnsButSucceeds_AndTopicWins()
    {
        var parser = new MeasurementBatchParser();
        var result = parser.Parse(Utf8(BatchJson(siteId: "other-site")), "site-1");

        // 冗余校验：载荷 siteId 与 topic 不一致 → 记警告，不失败
        Assert.True(result.IsSuccess);
        Assert.Single(result.Warnings);
        Assert.Contains("不一致", result.Warnings[0]);

        // 以 topic 第三段 siteId 为准注入每条 record（ADR-004）
        var record = Assert.Single(result.Batch!.Records);
        Assert.Equal("site-1", record.SiteId);
    }

    [Fact]
    public void Parse_DataType_AcceptsStringAndNumber()
    {
        var parser = new MeasurementBatchParser();

        // 字符串 "Float"（DESIGN.md 示例）
        var byName = parser.Parse(Utf8(BatchJson(dataType: "Float")), "site-1");
        Assert.Equal(DataType.Float, Assert.Single(byName.Batch!.Records).DataType);

        // JSON 数字 8（网关默认把枚举序列化为数字）→ Float
        var numeric = BatchJson().Replace("\"dataType\": \"Float\"", "\"dataType\": 8");
        var byNumber = parser.Parse(Utf8(numeric), "site-1");
        Assert.Equal(DataType.Float, Assert.Single(byNumber.Batch!.Records).DataType);
    }

    [Fact]
    public void Parse_Quality_AcceptsStringAndNumber()
    {
        var parser = new MeasurementBatchParser();

        var byName = parser.Parse(Utf8(BatchJson(quality: "Bad")), "site-1");
        Assert.Equal(Quality.Bad, Assert.Single(byName.Batch!.Records).Quality);

        var numeric = BatchJson().Replace("\"quality\": \"Good\"", "\"quality\": 2");
        var byNumber = parser.Parse(Utf8(numeric), "site-1");
        Assert.Equal(Quality.Bad, Assert.Single(byNumber.Batch!.Records).Quality);
    }

    [Fact]
    public void Parse_Timestamp_WithOffset_NormalizedToUtc()
    {
        var parser = new MeasurementBatchParser();
        var result = parser.Parse(Utf8(BatchJson(timestamp: "2026-08-23T09:00:00+08:00")), "site-1");

        Assert.True(result.IsSuccess);
        var record = Assert.Single(result.Batch!.Records);
        // 09:00 +08:00 = 01:00 UTC
        Assert.Equal(new DateTime(2026, 8, 23, 1, 0, 0, DateTimeKind.Utc), record.Timestamp);
    }

    [Fact]
    public void Parse_InvalidJson_Fails()
    {
        var parser = new MeasurementBatchParser();
        var result = parser.Parse(Utf8("{ not json"), "site-1");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Batch);
        Assert.NotNull(result.Error);
        Assert.Contains("JSON", result.Error);
    }

    [Fact]
    public void Parse_NullJson_Fails()
    {
        var parser = new MeasurementBatchParser();
        var result = parser.Parse(Utf8("null"), "site-1");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Batch);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void ParseAlarm_ValidPayload_Succeeds()
    {
        var parser = new MeasurementBatchParser();
        var json = """
            {
              "alarmId": "alarm-1",
              "ruleId": "rule-1",
              "deviceId": "dev-1",
              "pointId": "pt-1",
              "triggerValue": 90.5,
              "threshold": 80,
              "severity": "Warning",
              "message": "温度过高",
              "state": "Active",
              "occurredAt": "2026-08-23T01:00:00Z"
            }
            """;

        var result = parser.ParseAlarm(Utf8(json), "site-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("alarm-1", result.Value!.AlarmId);
        Assert.Equal(90.5, result.Value.TriggerValue);
        Assert.Equal("Warning", result.Value.Severity);
    }

    [Fact]
    public void ParseAlarm_MissingAlarmId_Fails()
    {
        var parser = new MeasurementBatchParser();
        var result = parser.ParseAlarm(Utf8("""{ "ruleId": "rule-1" }"""), "site-1");

        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
        Assert.Contains("alarmId", result.Error.Message);
    }

    [Fact]
    public void ParseAlarm_InvalidJson_Fails()
    {
        var parser = new MeasurementBatchParser();
        var result = parser.ParseAlarm(Utf8("{ oops"), "site-1");

        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
    }
}
