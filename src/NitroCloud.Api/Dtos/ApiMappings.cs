using NitroCloud.Domain.Commands;
using NitroCloud.Persistence.Entities;
using NitroCloud.Storage.Models;

namespace NitroCloud.Api.Dtos;

/// <summary>
/// 实体 → DTO 映射助手（集中映射，避免控制器重复）。
/// 在线状态（status/lastSeen）由 <c>OnlineStatusService</c> 计算后传入，不在此处推导。
/// </summary>
internal static class ApiMappings
{
    /// <summary>站点实体 → 站点 DTO（status/lastReportAt 由调用方传入在线判定结果）</summary>
    public static SiteDto ToDto(SiteEntity e, string status, string? lastReportAt)
        => new()
        {
            Id = e.Id,
            Name = e.Name,
            Location = e.Location,
            Status = status,
            LastReportAt = lastReportAt,
            CreatedAt = e.CreatedAt
        };

    /// <summary>设备实体 → 设备 DTO（status/lastSeenAt 由调用方传入在线判定结果）</summary>
    public static DeviceDto ToDto(DeviceEntity e, string status, string? lastSeenAt)
        => new()
        {
            Id = e.Id,
            SiteId = e.SiteId,
            Name = e.Name,
            Model = e.Model,
            Status = status,
            LastSeenAt = lastSeenAt
        };

    /// <summary>点位实体 → 点位 DTO（access 由 Writable 推导：可写 = ReadWrite，只读 = ReadOnly）</summary>
    public static PointDto ToDto(PointEntity e)
        => new()
        {
            Id = e.Id,
            DeviceId = e.DeviceId,
            Name = e.Name,
            DataType = e.DataType,
            Unit = e.Unit,
            AlarmEnabled = e.AlarmEnabled,
            Access = e.Writable ? "ReadWrite" : "ReadOnly",
            Enabled = true
        };

    /// <summary>时序查询结果 → 历史快照 DTO（Time → Timestamp，O 格式）</summary>
    public static PointSnapshotDto ToDto(MeasurementPoint p)
        => new()
        {
            SiteId = p.SiteId,
            DeviceId = p.DeviceId,
            DevicePointId = p.DevicePointId,
            Value = p.Value,
            Quality = p.Quality,
            Timestamp = p.Time.ToUniversalTime().ToString("O")
        };

    /// <summary>命令记录 → 命令 DTO（前端用 id 字段而非 commandId）</summary>
    public static CommandRecordDto ToDto(CommandRecord c)
        => new()
        {
            Id = c.CommandId.ToString(),
            SiteId = c.SiteId,
            DeviceId = c.DeviceId,
            PointId = c.PointId,
            Type = c.Type,
            Value = c.Value,
            RequestedBy = c.RequestedBy,
            Status = c.Status.ToString(),
            RequestedAt = c.RequestedAt.ToUniversalTime().ToString("O"),
            AckedAt = c.AckedAt?.ToUniversalTime().ToString("O"),
            Error = c.Error
        };
}
