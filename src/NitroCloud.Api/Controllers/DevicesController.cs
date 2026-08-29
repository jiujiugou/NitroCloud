using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NitroCloud.Api.Dtos;
using NitroCloud.Api.Models;
using NitroCloud.Api.Realtime;
using NitroCloud.Persistence;
using NitroCloud.Persistence.Entities;

namespace NitroCloud.Api.Controllers;

/// <summary>
/// 设备元数据 API（前端 web/src/api/devices.ts）。
/// status 为在线状态（ADR-007：最后上报时间 + 阈值），由 <see cref="OnlineStatusService"/> 计算。
/// </summary>
[ApiController, Route("api/devices")]
public sealed class DevicesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly OnlineStatusService _status;

    /// <summary>创建控制器</summary>
    public DevicesController(AppDbContext db, OnlineStatusService status)
    {
        _db = db;
        _status = status;
    }

    /// <summary>全部设备（管理面板点位视图按设备过滤用；初版不分页）</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DeviceDto>>>> GetAll()
    {
        var devices = await _db.Devices.AsNoTracking().OrderBy(d => d.CreatedAt).ToListAsync();
        return Ok(ApiResponse<IReadOnlyList<DeviceDto>>.Ok(devices.Select(ToDto).ToList()));
    }

    /// <summary>站点下设备列表（绝对路由：api/sites/{siteId}/devices）</summary>
    [HttpGet("~/api/sites/{siteId}/devices")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DeviceDto>>>> GetBySite(string siteId)
    {
        if (!await _db.Sites.AsNoTracking().AnyAsync(s => s.Id == siteId))
            return NotFound(ApiResponse<IReadOnlyList<DeviceDto>>.Fail("SiteNotFound", $"站点 {siteId} 不存在"));

        var devices = await _db.Devices.AsNoTracking()
            .Where(d => d.SiteId == siteId).OrderBy(d => d.CreatedAt).ToListAsync();
        return Ok(ApiResponse<IReadOnlyList<DeviceDto>>.Ok(devices.Select(ToDto).ToList()));
    }

    /// <summary>单设备</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<DeviceDto>>> Get(string id)
    {
        var device = await _db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
        if (device is null)
            return NotFound(ApiResponse<DeviceDto>.Fail("DeviceNotFound", $"设备 {id} 不存在"));

        return Ok(ApiResponse<DeviceDto>.Ok(ToDto(device)));
    }

    /// <summary>创建设备（SiteId 必填且站点须存在；Id 可选，缺省生成 Guid；重复 409）</summary>
    [Authorize]
    [HttpPost]
    public async Task<ActionResult<ApiResponse<DeviceDto>>> Create(DeviceRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.SiteId))
            return BadRequest(ApiResponse<DeviceDto>.Fail("InvalidSiteId", "设备所属站点必填"));
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<DeviceDto>.Fail("InvalidName", "设备名称必填"));

        if (!await _db.Sites.AsNoTracking().AnyAsync(s => s.Id == request.SiteId))
            return NotFound(ApiResponse<DeviceDto>.Fail("SiteNotFound", $"站点 {request.SiteId} 不存在"));

        var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString() : request.Id.Trim();
        if (await _db.Devices.AnyAsync(d => d.Id == id))
            return Conflict(ApiResponse<DeviceDto>.Fail("DuplicateDevice", $"设备 {id} 已存在"));

        var entity = new DeviceEntity
        {
            Id = id,
            SiteId = request.SiteId.Trim(),
            Name = request.Name.Trim(),
            Model = request.Model?.Trim() ?? "",
            CreatedAt = DateTime.UtcNow.ToString("O")
        };
        _db.Devices.Add(entity);
        await _db.SaveChangesAsync();

        return Ok(ApiResponse<DeviceDto>.Ok(ToDto(entity)));
    }

    /// <summary>更新设备（部分更新：仅覆盖提供的字段；SiteId 变更须校验目标站点存在）</summary>
    [Authorize]
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<DeviceDto>>> Update(string id, DeviceRequestDto request)
    {
        var entity = await _db.Devices.FirstOrDefaultAsync(d => d.Id == id);
        if (entity is null)
            return NotFound(ApiResponse<DeviceDto>.Fail("DeviceNotFound", $"设备 {id} 不存在"));

        if (!string.IsNullOrWhiteSpace(request.SiteId) && request.SiteId != entity.SiteId)
        {
            if (!await _db.Sites.AsNoTracking().AnyAsync(s => s.Id == request.SiteId))
                return NotFound(ApiResponse<DeviceDto>.Fail("SiteNotFound", $"站点 {request.SiteId} 不存在"));
            entity.SiteId = request.SiteId.Trim();
        }
        if (!string.IsNullOrWhiteSpace(request.Name))
            entity.Name = request.Name.Trim();
        if (request.Model is not null)
            entity.Model = request.Model.Trim();

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<DeviceDto>.Ok(ToDto(entity)));
    }

    /// <summary>删除设备（设备下存在点位时 400 拒绝）</summary>
    [Authorize]
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(string id)
    {
        var entity = await _db.Devices.FirstOrDefaultAsync(d => d.Id == id);
        if (entity is null)
            return NotFound(ApiResponse<object>.Fail("DeviceNotFound", $"设备 {id} 不存在"));

        if (await _db.Points.AnyAsync(p => p.DeviceId == id))
            return BadRequest(ApiResponse<object>.Fail("DeviceHasPoints", $"设备 {id} 下存在点位，无法删除"));

        _db.Devices.Remove(entity);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { }));
    }

    private DeviceDto ToDto(DeviceEntity e)
        => ApiMappings.ToDto(e, _status.GetDeviceStatus(e.SiteId, e.Id), _status.GetDeviceLastSeenAt(e.SiteId, e.Id));
}
