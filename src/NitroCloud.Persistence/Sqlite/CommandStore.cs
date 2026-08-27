using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NitroCloud.Domain.Commands;
using NitroCloud.Persistence.Entities;
using NitroCloud.Storage;

namespace NitroCloud.Persistence.Sqlite;

/// <summary>
/// 命令存储（SQLite / EF Core，模式同 <see cref="AlarmStore"/>，ADR-010 D2）。
/// 状态机 Pending → Sent → Acked/Failed/Timeout；终态不覆盖（UpdateStatusAsync 读实体后判定）。
/// </summary>
public sealed class CommandStore : ICommandStore
{
    private readonly AppDbContext _db;
    private readonly ILogger<CommandStore> _logger;

    /// <summary>创建命令存储</summary>
    public CommandStore(AppDbContext db, ILogger<CommandStore> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task AddAsync(CommandRecord cmd, CancellationToken ct = default)
    {
        _db.CommandRecords.Add(ToEntity(cmd));
        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task UpdateStatusAsync(
        Guid commandId,
        CommandStatus status,
        string? error = null,
        int? attempts = null,
        DateTime? sentAt = null,
        DateTime? ackedAt = null,
        CancellationToken ct = default)
    {
        var entity = await _db.CommandRecords.FindAsync([commandId.ToString()], ct);
        if (entity is null)
        {
            _logger.LogWarning("更新不存在的命令 {CommandId} 状态", commandId);
            return;
        }

        // 终态不覆盖：已是 Acked/Failed/Timeout 的命令忽略后续状态更新（幂等）
        if (entity.Status is nameof(CommandStatus.Acked) or nameof(CommandStatus.Failed) or nameof(CommandStatus.Timeout))
            return;

        entity.Status = status.ToString();
        entity.Error = error;
        if (attempts.HasValue)
            entity.Attempts = attempts.Value;
        if (sentAt.HasValue)
            entity.SentAt = sentAt.Value.ToUniversalTime().ToString("O");
        if (ackedAt.HasValue)
            entity.AckedAt = ackedAt.Value.ToUniversalTime().ToString("O");

        await _db.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<CommandRecord?> GetAsync(Guid commandId, CancellationToken ct = default)
    {
        var entity = await _db.CommandRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CommandId == commandId.ToString(), ct);
        return entity is null ? null : ToDomain(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommandRecord>> QueryInFlightAsync(CancellationToken ct = default)
    {
        var entities = await _db.CommandRecords.AsNoTracking()
            .Where(r => r.Status == nameof(CommandStatus.Pending) || r.Status == nameof(CommandStatus.Sent))
            .OrderBy(r => r.RequestedAt)
            .ToListAsync(ct);
        return entities.Select(ToDomain).ToList();
    }

    private static CommandRecordEntity ToEntity(CommandRecord c) => new()
    {
        CommandId = c.CommandId.ToString(),
        Type = c.Type,
        SiteId = c.SiteId,
        DeviceId = c.DeviceId,
        PointId = c.PointId,
        Value = c.Value,
        RequestedAt = c.RequestedAt.ToUniversalTime().ToString("O"),
        Status = c.Status.ToString(),
        Error = c.Error,
        Attempts = c.Attempts,
        SentAt = c.SentAt?.ToUniversalTime().ToString("O"),
        AckedAt = c.AckedAt?.ToUniversalTime().ToString("O")
    };

    private static CommandRecord ToDomain(CommandRecordEntity e) => new()
    {
        CommandId = Guid.Parse(e.CommandId),
        Type = e.Type,
        SiteId = e.SiteId,
        DeviceId = e.DeviceId,
        PointId = e.PointId,
        Value = e.Value,
        RequestedAt = DateTime.Parse(e.RequestedAt, null, System.Globalization.DateTimeStyles.AssumeUniversal),
        Status = Enum.Parse<CommandStatus>(e.Status),
        Error = e.Error,
        Attempts = e.Attempts,
        SentAt = e.SentAt is null
            ? null
            : DateTime.Parse(e.SentAt, null, System.Globalization.DateTimeStyles.AssumeUniversal),
        AckedAt = e.AckedAt is null
            ? null
            : DateTime.Parse(e.AckedAt, null, System.Globalization.DateTimeStyles.AssumeUniversal)
    };
}
