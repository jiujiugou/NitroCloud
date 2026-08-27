using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NitroCloud.Api.Dtos;
using NitroCloud.Api.Models;
using NitroCloud.Command;
using NitroCloud.Domain.Commands;
using NitroCloud.Persistence;
using NitroCloud.Storage;

namespace NitroCloud.Api.Controllers;

/// <summary>
/// 命令 API（ADR-010 D4）：云端反向写值（POST write）+ 命令状态查询（GET {commandId}）。
/// 前端 web/src/api/commands.ts 对齐。发布失败不改 Pending 语义，由后台扫描重发兜底。
/// </summary>
[ApiController, Route("api/commands")]
public sealed class CommandsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICommandStore _store;
    private readonly ICommandDispatcher _dispatcher;

    /// <summary>创建命令控制器</summary>
    public CommandsController(AppDbContext db, ICommandStore store, ICommandDispatcher dispatcher)
    {
        _db = db;
        _store = store;
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// 云端发起写值：校验 siteId/deviceId/pointId 非空 + 点位存在且可写 →
    /// 构造 <see cref="CommandStatus.Pending"/> 命令落库 → 发布到 commands topic（QoS1）→ 返回 commandId。
    /// 发布失败不阻塞响应：命令保持 Pending，后台扫描重发兜底（ADR-010 D1）。
    /// </summary>
    [HttpPost("write")]
    public async Task<ActionResult<ApiResponse<WriteValueResponseDto>>> Write(WriteValueRequestDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SiteId) || string.IsNullOrWhiteSpace(request.DeviceId) || string.IsNullOrWhiteSpace(request.PointId))
            return BadRequest(ApiResponse<WriteValueResponseDto>.Fail("InvalidCommand", "siteId/deviceId/pointId 必填"));

        // 点位存在性 + 归属校验：点位 → 设备 → 站点逐级确认
        var point = await _db.Points.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.PointId && p.DeviceId == request.DeviceId, ct);
        if (point is null)
            return NotFound(ApiResponse<WriteValueResponseDto>.Fail("PointNotFound",
                $"点位 {request.PointId} 不存在或不属于设备 {request.DeviceId}"));

        if (!await _db.Devices.AsNoTracking().AnyAsync(d => d.Id == request.DeviceId && d.SiteId == request.SiteId, ct))
            return NotFound(ApiResponse<WriteValueResponseDto>.Fail("DeviceNotFound",
                $"设备 {request.DeviceId} 不存在或不属于站点 {request.SiteId}"));

        if (!point.Writable)
            return BadRequest(ApiResponse<WriteValueResponseDto>.Fail("PointReadOnly",
                $"点位 {request.PointId} 只读，不可写值"));

        var command = new CommandRecord
        {
            Type = "WritePoint",
            SiteId = request.SiteId,
            DeviceId = request.DeviceId,
            PointId = request.PointId,
            Value = request.Value,
            RequestedAt = DateTime.UtcNow,
            Status = CommandStatus.Pending
        };

        await _store.AddAsync(command, ct);

        // 发布失败不改 Pending 语义：由后台扫描重发兜底（ADR-010 D1），此处不阻塞响应
        await _dispatcher.DispatchAsync(command, ct);

        return Ok(ApiResponse<WriteValueResponseDto>.Ok(new WriteValueResponseDto
        {
            CommandId = command.CommandId.ToString(),
            Status = command.Status.ToString(),
            RequestedAt = command.RequestedAt.ToUniversalTime().ToString("O")
        }));
    }

    /// <summary>命令状态查询（前端超时轮询兜底用，ADR-010 D4）</summary>
    [HttpGet("{commandId:guid}")]
    public async Task<ActionResult<ApiResponse<CommandRecordDto>>> Get(Guid commandId, CancellationToken ct)
    {
        var record = await _store.GetAsync(commandId, ct);
        if (record is null)
            return NotFound(ApiResponse<CommandRecordDto>.Fail("CommandNotFound", $"命令 {commandId} 不存在"));
        return Ok(ApiResponse<CommandRecordDto>.Ok(ApiMappings.ToDto(record)));
    }
}
