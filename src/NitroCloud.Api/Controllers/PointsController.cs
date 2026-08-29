using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NitroCloud.Api.Dtos;
using NitroCloud.Api.Models;
using NitroCloud.Domain.Devices;
using NitroCloud.Persistence;
using NitroCloud.Persistence.Entities;

namespace NitroCloud.Api.Controllers;

/// <summary>
/// 点位元数据 API（前端 web/src/api/points.ts）。
/// access 由 Writable 推导：可写 = ReadWrite，只读 = ReadOnly（初版无 WriteOnly）。
/// </summary>
[ApiController, Route("api/devices/{deviceId}/points")]
public sealed class PointsController : ControllerBase
{
    private readonly AppDbContext _db;

    /// <summary>创建控制器</summary>
    public PointsController(AppDbContext db) => _db = db;

    /// <summary>设备下点位列表</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PointDto>>>> GetAll(string deviceId)
    {
        if (!await _db.Devices.AsNoTracking().AnyAsync(d => d.Id == deviceId))
            return NotFound(ApiResponse<IReadOnlyList<PointDto>>.Fail("DeviceNotFound", $"设备 {deviceId} 不存在"));

        var points = await _db.Points.AsNoTracking()
            .Where(p => p.DeviceId == deviceId).OrderBy(p => p.Name).ToListAsync();
        return Ok(ApiResponse<IReadOnlyList<PointDto>>.Ok(points.Select(ApiMappings.ToDto).ToList()));
    }

    /// <summary>创建点位（DataType 枚举校验；Id 可选；未传 Access 默认只读）</summary>
    [Authorize]
    [HttpPost]
    public async Task<ActionResult<ApiResponse<PointDto>>> Create(string deviceId, PointRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<PointDto>.Fail("InvalidName", "点位名称必填"));

        if (!await _db.Devices.AsNoTracking().AnyAsync(d => d.Id == deviceId))
            return NotFound(ApiResponse<PointDto>.Fail("DeviceNotFound", $"设备 {deviceId} 不存在"));

        var dataType = ParseDataType(request.DataType);
        if (dataType is null)
            return BadRequest(ApiResponse<PointDto>.Fail("InvalidDataType", $"非法数据类型：{request.DataType}"));

        bool writable;
        if (request.Access is null)
            writable = false; // 默认只读，安全
        else
        {
            var w = ParseWritable(request.Access);
            if (w is null)
                return BadRequest(ApiResponse<PointDto>.Fail("InvalidAccess", $"非法访问权限：{request.Access}"));
            writable = w.Value;
        }

        var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString() : request.Id.Trim();
        if (await _db.Points.AnyAsync(p => p.Id == id))
            return Conflict(ApiResponse<PointDto>.Fail("DuplicatePoint", $"点位 {id} 已存在"));

        var entity = new PointEntity
        {
            Id = id,
            DeviceId = deviceId,
            Name = request.Name.Trim(),
            DataType = dataType.Value.ToString(),
            Unit = request.Unit?.Trim() ?? "",
            AlarmEnabled = request.AlarmEnabled ?? false,
            Writable = writable
        };
        _db.Points.Add(entity);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<PointDto>.Ok(ApiMappings.ToDto(entity)));
    }

    /// <summary>更新点位（部分更新：仅覆盖提供的字段）</summary>
    [Authorize]
    [HttpPut("{pointId}")]
    public async Task<ActionResult<ApiResponse<PointDto>>> Update(string deviceId, string pointId, PointRequestDto request)
    {
        var entity = await _db.Points.FirstOrDefaultAsync(p => p.Id == pointId && p.DeviceId == deviceId);
        if (entity is null)
            return NotFound(ApiResponse<PointDto>.Fail("PointNotFound", $"点位 {pointId} 不存在或不属于设备 {deviceId}"));

        if (!string.IsNullOrWhiteSpace(request.Name))
            entity.Name = request.Name.Trim();
        if (request.DataType is not null)
        {
            var dataType = ParseDataType(request.DataType);
            if (dataType is null)
                return BadRequest(ApiResponse<PointDto>.Fail("InvalidDataType", $"非法数据类型：{request.DataType}"));
            entity.DataType = dataType.Value.ToString();
        }
        if (request.Unit is not null)
            entity.Unit = request.Unit.Trim();
        if (request.AlarmEnabled.HasValue)
            entity.AlarmEnabled = request.AlarmEnabled.Value;
        if (request.Access is not null)
        {
            var w = ParseWritable(request.Access);
            if (w is null)
                return BadRequest(ApiResponse<PointDto>.Fail("InvalidAccess", $"非法访问权限：{request.Access}"));
            entity.Writable = w.Value;
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<PointDto>.Ok(ApiMappings.ToDto(entity)));
    }

    /// <summary>删除点位</summary>
    [Authorize]
    [HttpDelete("{pointId}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(string deviceId, string pointId)
    {
        var entity = await _db.Points.FirstOrDefaultAsync(p => p.Id == pointId && p.DeviceId == deviceId);
        if (entity is null)
            return NotFound(ApiResponse<object>.Fail("PointNotFound", $"点位 {pointId} 不存在或不属于设备 {deviceId}"));

        _db.Points.Remove(entity);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { }));
    }

    /// <summary>解析数据类型（空 → 默认 Float；非法 → null）</summary>
    private static DataType? ParseDataType(string? value)
        => string.IsNullOrWhiteSpace(value) ? DataType.Float
            : Enum.TryParse<DataType>(value, true, out var dt) ? dt : null;

    /// <summary>解析访问权限（ReadWrite/WriteOnly → 可写；ReadOnly → 只读；非法 → null）</summary>
    private static bool? ParseWritable(string? access) => access switch
    {
        "ReadWrite" or "WriteOnly" => true,
        "ReadOnly" => false,
        _ => null
    };
}
