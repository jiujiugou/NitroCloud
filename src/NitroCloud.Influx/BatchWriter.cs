using InfluxDB.Client;
using InfluxDB.Client.Api.Domain;
using InfluxDB.Client.Writes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NitroCloud.Shared;

namespace NitroCloud.Influx;

/// <summary>
/// InfluxDB 批量写入封装（ADR-008 D2）。把一组 <see cref="PointData"/> 一次性写入 bucket/org，
/// 异常收敛为 <see cref="OperationResult"/>，供上层（Ingest 重试队列）按策略重试。
/// </summary>
public sealed class BatchWriter
{
    private readonly IWriteApiAsync _writeApi;
    private readonly InfluxOptions _options;
    private readonly ILogger<BatchWriter> _logger;

    /// <summary>创建批量写入器</summary>
    public BatchWriter(IWriteApiAsync writeApi, IOptions<InfluxOptions> options, ILogger<BatchWriter> logger)
    {
        _writeApi = writeApi;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 批量写入。全成功返回 Success；任一失败返回 Failure（携带错误信息，调用方重试整个批次）。
    /// </summary>
    public async Task<OperationResult> WriteAsync(IReadOnlyList<PointData> points, CancellationToken ct = default)
    {
        if (points.Count == 0)
            return OperationResult.Success();

        try
        {
            await _writeApi.WritePointsAsync(points, _options.Bucket, _options.Org, ct);
            _logger.LogDebug("InfluxDB 写入 {Count} 个点 → bucket={Bucket}", points.Count, _options.Bucket);
            return OperationResult.Success();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "InfluxDB 批量写入失败（{Count} 点）", points.Count);
            return OperationResult.Failure(OperationalError.Storage($"InfluxDB 批量写入失败: {ex.Message}"));
        }
    }
}
