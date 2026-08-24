using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NitroCloud.Api.Dtos;
using NitroCloud.Api.Models;
using NitroCloud.Api.Realtime;
using NitroCloud.Persistence;
using NitroCloud.Persistence.Entities;
using NitroCloud.Storage;
using NitroCloud.Storage.Models;

namespace NitroCloud.Api.Controllers;

/// <summary>
/// 站点元数据 + 站点最近值快照 API（前端 web/src/api/sites.ts）。
/// status 为在线状态（ADR-007：最后上报时间 + 阈值），由 <see cref="OnlineStatusService"/> 计算，
/// 非元数据运营状态（Active/Disabled）；Maintenance = 元数据 Disabled。
/// </summary>
[ApiController, Route("api/sites")]
public sealed class SitesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILatestValueCache _cache;
    private readonly OnlineStatusService _status;

    /// <summary>创建控制器</summary>
    public SitesController(AppDbContext db, ILatestValueCache cache, OnlineStatusService status)
    {
        _db = db;
        _cache = cache;
        _status = status;
    }

    /// <summary>站点列表（含在线状态 / 最后上报）</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SiteDto>>>> GetAll()
    {
        var sites = await _db.Sites.AsNoTracking().OrderBy(s => s.CreatedAt).ToListAsync();
        return Ok(ApiResponse<IReadOnlyList<SiteDto>>.Ok(sites
            .Select(s => ApiMappings.ToDto(s, _status.GetSiteStatus(s), _status.GetSiteLastReportAt(s.Id)))
            .ToList()));
    }

    /// <summary>单站点</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<SiteDto>>> Get(string id)
    {
        var site = await _db.Sites.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (site is null)
            return NotFound(ApiResponse<SiteDto>.Fail("SiteNotFound", $"站点 {id} 不存在"));

        return Ok(ApiResponse<SiteDto>.Ok(
            ApiMappings.ToDto(site, _status.GetSiteStatus(site), _status.GetSiteLastReportAt(site.Id))));
    }

    /// <summary>创建站点（Id 可选，缺省生成 Guid；重复 409）</summary>
    [HttpPost]
    public async Task<ActionResult<ApiResponse<SiteDto>>> Create(SiteRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(ApiResponse<SiteDto>.Fail("InvalidName", "站点名称必填"));

        var id = string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("N") : request.Id.Trim();
        if (id.Length > 64)
            return BadRequest(ApiResponse<SiteDto>.Fail("InvalidId", "站点 Id 过长（≤64）"));

        if (await _db.Sites.AnyAsync(s => s.Id == id))
            return Conflict(ApiResponse<SiteDto>.Fail("DuplicateSite", $"站点 {id} 已存在"));

        if (request.Status is not null && request.Status is not ("Active" or "Disabled"))
            return BadRequest(ApiResponse<SiteDto>.Fail("InvalidStatus", "站点状态仅支持 Active/Disabled"));

        var entity = new SiteEntity
        {
            Id = id,
            Name = request.Name.Trim(),
            Location = request.Location?.Trim() ?? "",
            Status = string.IsNullOrWhiteSpace(request.Status) ? "Active" : request.Status.Trim(),
            CreatedAt = DateTime.UtcNow.ToString("O")
        };
        _db.Sites.Add(entity);
        await _db.SaveChangesAsync();

        // 新建站点缓存为空，在线状态为 Unknown（正确）
        return Ok(ApiResponse<SiteDto>.Ok(
            ApiMappings.ToDto(entity, _status.GetSiteStatus(entity), _status.GetSiteLastReportAt(entity.Id))));
    }

    /// <summary>更新站点（部分更新：仅覆盖提供的字段）</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<ApiResponse<SiteDto>>> Update(string id, SiteRequestDto request)
    {
        var entity = await _db.Sites.FirstOrDefaultAsync(s => s.Id == id);
        if (entity is null)
            return NotFound(ApiResponse<SiteDto>.Fail("SiteNotFound", $"站点 {id} 不存在"));

        if (!string.IsNullOrWhiteSpace(request.Name))
            entity.Name = request.Name.Trim();
        if (request.Location is not null)
            entity.Location = request.Location.Trim();
        if (request.Status is not null)
        {
            if (request.Status is not ("Active" or "Disabled"))
                return BadRequest(ApiResponse<SiteDto>.Fail("InvalidStatus", "站点状态仅支持 Active/Disabled"));
            entity.Status = request.Status.Trim();
        }

        await _db.SaveChangesAsync();
        return Ok(ApiResponse<SiteDto>.Ok(
            ApiMappings.ToDto(entity, _status.GetSiteStatus(entity), _status.GetSiteLastReportAt(entity.Id))));
    }

    /// <summary>删除站点（站点下存在设备时 400 拒绝）</summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(string id)
    {
        var entity = await _db.Sites.FirstOrDefaultAsync(s => s.Id == id);
        if (entity is null)
            return NotFound(ApiResponse<object>.Fail("SiteNotFound", $"站点 {id} 不存在"));

        if (await _db.Devices.AnyAsync(d => d.SiteId == id))
            return BadRequest(ApiResponse<object>.Fail("SiteHasDevices", $"站点 {id} 下存在设备，无法删除"));

        _db.Sites.Remove(entity);
        await _db.SaveChangesAsync();
        return Ok(ApiResponse<object>.Ok(new { }));
    }

    /// <summary>
    /// 站点最近值快照（ILatestValueCache.GetSite，实时面板秒级读缓存不查库；重启丢缓存可接受，面板自恢复）。
    /// dataType/quality 由 JsonStringEnumConverter 序列化为枚举名，timestamp 为 ISO 8601。
    /// </summary>
    [HttpGet("{siteId}/latest")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<LatestValue>>>> Latest(string siteId)
    {
        if (!await _db.Sites.AsNoTracking().AnyAsync(s => s.Id == siteId))
            return NotFound(ApiResponse<IReadOnlyList<LatestValue>>.Fail("SiteNotFound", $"站点 {siteId} 不存在"));

        return Ok(ApiResponse<IReadOnlyList<LatestValue>>.Ok(_cache.GetSite(siteId)));
    }
}
