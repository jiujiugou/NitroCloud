using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Core.Flux.Domain;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NitroCloud.Domain.Measurements;
using NitroCloud.Shared;
using NitroCloud.Storage;
using NitroCloud.Storage.Models;

namespace NitroCloud.Influx;

/// <summary>
/// 时序存储的 InfluxDB 实现（ADR-008 D2：批量写 + Flux 查询封装）。
/// 写入：MeasurementRecord → PointData（tag=siteId/deviceId/devicePointId/pointName/quality，field=value，timestamp=记录时间戳）。
/// 查询：FluxQueryBuilder 生成语句 → FluxRecord → MeasurementPoint。
/// </summary>
public sealed class InfluxTimeseriesStore : ITimeseriesStore
{
    private readonly IQueryApi _queryApi;
    private readonly InfluxOptions _options;
    private readonly BatchWriter _writer;
    private readonly ILogger<InfluxTimeseriesStore> _logger;

    /// <summary>创建时序存储实现</summary>
    public InfluxTimeseriesStore(IQueryApi queryApi, BatchWriter writer, IOptions<InfluxOptions> options, ILogger<InfluxTimeseriesStore> logger)
    {
        _queryApi = queryApi;
        _writer = writer;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<OperationResult> WriteAsync(IReadOnlyList<MeasurementRecord> records, CancellationToken ct = default)
    {
        var points = new List<PointData>(records.Count);
        foreach (var record in records)
        {
            // 不可转数值的（纯字符串等）跳过并记指标，不阻塞整批（契约忠实 + Influx field 强类型）
            if (!ValueCoercion.TryGetDouble(record.Value, out var numeric))
            {
                Telemetry.CloudMetrics.IngestRecordsTotal.WithLabels("skipped_non_numeric").Inc();
                _logger.LogDebug("跳过非数值记录 {RecordId}（point={PointName}, value={Value}）",
                    record.Id, record.PointName, record.Value);
                continue;
            }

            points.Add(ToPointData(record, numeric));
            Telemetry.CloudMetrics.IngestRecordsTotal.WithLabels("stored").Inc();
        }

        return await _writer.WriteAsync(points, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MeasurementPoint>> QueryAsync(TimeseriesQuery query, CancellationToken ct = default)
    {
        var flux = FluxQueryBuilder.Build(query, _options.Bucket, _options.Measurement);
        var tables = await _queryApi.QueryAsync(flux, _options.Org, ct);

        var result = new List<MeasurementPoint>();
        foreach (var table in tables)
        {
            foreach (var record in table.Records)
            {
                var value = record.GetValue();
                if (value is not IConvertible || !ValueCoercion.TryGetDouble(value, out var numeric))
                    continue;

                result.Add(new MeasurementPoint
                {
                    SiteId = GetTag(record, "siteId") ?? query.SiteId,
                    DeviceId = GetTag(record, "deviceId") ?? query.DeviceId ?? "",
                    DevicePointId = GetTag(record, "devicePointId") ?? query.DevicePointId ?? "",
                    PointName = GetTag(record, "pointName") ?? "",
                    Value = numeric,
                    Quality = GetTag(record, "quality") ?? "Good",
                    Time = record.GetTimeInDateTime()?.ToUniversalTime() ?? DateTime.UtcNow
                });
            }
        }

        return result;
    }

    private PointData ToPointData(MeasurementRecord r, double value)
        => PointData.Measurement(_options.Measurement)
            .Tag("siteId", r.SiteId ?? "")
            .Tag("deviceId", r.DeviceId.ToString())
            .Tag("devicePointId", r.DevicePointId.ToString())
            .Tag("pointName", r.PointName)
            .Tag("quality", r.Quality.ToString())
            .Field("value", value)
            .Timestamp(r.Timestamp.ToUniversalTime(), WritePrecision.Ns);

    private static string? GetTag(FluxRecord record, string key)
        => record.GetValueByKey(key)?.ToString();
}
