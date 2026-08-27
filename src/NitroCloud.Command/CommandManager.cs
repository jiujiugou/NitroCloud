using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NitroCloud.Domain.Commands;
using NitroCloud.Shared;
using NitroCloud.Storage;
using NitroCloud.Telemetry;
using NitroCloud.Telemetry.Tracing;

namespace NitroCloud.Command;

/// <summary>
/// 命令状态机核心（ADR-010 D3/D5）：回执处理 + 超时重试扫描。
/// 纯类、无基础设施依赖，可直接单测。
/// 职责边界：
/// - <see cref="HandleAckAsync"/>：解析回执 → 幂等校验（未知 commandId 忽略 / 终态不覆盖）→ 更新状态 → 实时推送 → 计数；
/// - <see cref="ScanInFlightAsync"/>：扫描 Pending/Sent 中「超时未进展」的命令 → 重发（attempts+1，不换 commandId）→ 达上限标 Timeout。
/// 依赖的 <see cref="ICommandStore"/> 为 scoped 生命周期（EF Core），故按操作经 <see cref="IServiceScopeFactory"/> 开 scope 解析。
/// </summary>
public sealed class CommandManager
{
    /// <summary>服务作用域工厂：按操作解析 scoped 的 <see cref="ICommandStore"/>（避免从根容器解析 scoped）</summary>
    private readonly IServiceScopeFactory _scopeFactory;
    /// <summary>命令发布接口（发布失败不改 Pending 语义，由扫描重发兜底）</summary>
    private readonly ICommandDispatcher _dispatcher;
    /// <summary>实时推送接口（回执推送 OnCommandAck）</summary>
    private readonly IRealtimeNotifier _notifier;
    /// <summary>配置快照（构造时读入，运行期不刷新）</summary>
    private readonly CommandOptions _options;
    /// <summary>回执载荷解析器（无状态，可共享）</summary>
    private readonly CommandAckParser _ackParser;
    /// <summary>日志</summary>
    private readonly ILogger<CommandManager> _logger;

    /// <summary>创建命令状态机核心</summary>
    public CommandManager(
        IServiceScopeFactory scopeFactory,
        ICommandDispatcher dispatcher,
        IRealtimeNotifier notifier,
        IOptions<CommandOptions> options,
        CommandAckParser ackParser,
        ILogger<CommandManager> logger)
    {
        _scopeFactory = scopeFactory;
        _dispatcher = dispatcher;
        _notifier = notifier;
        _options = options.Value;
        _ackParser = ackParser;
        _logger = logger;
    }

    /// <summary>
    /// 处理一条命令回执（ADR-010 D4）：
    /// 解析载荷（失败记日志丢弃）→ 查记录 → 未知 commandId 忽略 / 终态不覆盖 →
    /// <see cref="CommandStatus.Acked"/> 或 <see cref="CommandStatus.Failed"/> → 实时推送 OnCommandAck → 计数。
    /// 推送为 best-effort：异常被捕获记日志，不影响状态落库。
    /// </summary>
    /// <param name="payload">commands/ack 消息载荷（UTF-8 JSON）</param>
    /// <param name="ct">取消令牌</param>
    public async Task HandleAckAsync(byte[] payload, CancellationToken ct = default)
    {
        using var activity = CloudActivitySource.Source.StartActivity(CloudActivities.CommandAck);

        var parseResult = _ackParser.Parse(payload);
        if (parseResult.IsFailure)
        {
            _logger.LogWarning("命令回执解析失败: {Error}", parseResult.Error?.Message);
            return;
        }

        var ack = parseResult.Value!;
        activity?.SetTag(CloudActivityTags.CommandId, ack.CommandId);

        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICommandStore>();

        var record = await store.GetAsync(ack.CommandId, ct);
        if (record is null)
        {
            _logger.LogDebug("忽略未知命令回执 commandId={CommandId}", ack.CommandId);
            return;
        }
        if (record.IsFinal)
        {
            // 终态不覆盖：重复回执 / 迟到的回执（已 Timeout）一律忽略
            _logger.LogDebug("命令 {CommandId} 已是终态 {Status}，忽略回执", ack.CommandId, record.Status);
            return;
        }

        var status = ack.Result == CommandResult.Success ? CommandStatus.Acked : CommandStatus.Failed;
        // 成功回执清空错误（Error 仅记录最近失败/超时原因）；失败回执携带网关 error
        var error = ack.Result == CommandResult.Success ? null : ack.Error;
        await store.UpdateStatusAsync(ack.CommandId, status, error, ackedAt: DateTime.UtcNow, ct: ct);

        // 实时推送 best-effort：失败不阻塞状态已落库的事实
        try
        {
            await _notifier.NotifyCommandAckAsync(ack, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "推送命令回执失败（commandId={CommandId}）", ack.CommandId);
        }

        CloudMetrics.CommandAckTotal.WithLabels(ack.Result == CommandResult.Success ? "success" : "failure").Inc();
    }

    /// <summary>
    /// 超时重试扫描（ADR-010 D5）：查询 Pending/Sent 中「超时未进展」的命令，
    /// 超时锚点 = <see cref="CommandRecord.SentAt"/> ?? <see cref="CommandRecord.RequestedAt"/>。
    /// - 未达 <see cref="CommandOptions.MaxAttempts"/>：重发（attempts+1，不换 commandId）；发布成功置 Sent + SentAt=now，
    ///   发布失败保持 Pending（下轮再试，防 Api 触发发布丢失）；
    /// - 已达上限：标 <see cref="CommandStatus.Timeout"/>（可人工重发）+ 超时指标。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task ScanInFlightAsync(CancellationToken ct = default)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));
        var maxAttempts = Math.Max(1, _options.MaxAttempts);
        var now = DateTime.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICommandStore>();

        var inFlight = await store.QueryInFlightAsync(ct);
        foreach (var command in inFlight)
        {
            var anchor = command.SentAt ?? command.RequestedAt;
            if (now - anchor <= timeout)
                continue;

            var attempts = command.Attempts + 1;
            if (attempts > maxAttempts)
            {
                await store.UpdateStatusAsync(command.CommandId, CommandStatus.Timeout,
                    $"重试 {attempts}/{maxAttempts} 次仍无回执", ct: ct);
                CloudMetrics.CommandTimeoutTotal.Inc();
                _logger.LogWarning("命令 {CommandId} 重试超上限（{Attempts}/{Max}），标记 Timeout",
                    command.CommandId, attempts, maxAttempts);
                continue;
            }

            var result = await _dispatcher.DispatchAsync(command, ct);
            if (result.IsSuccess)
            {
                await store.UpdateStatusAsync(command.CommandId, CommandStatus.Sent,
                    error: null, attempts: attempts, sentAt: now, ct: ct);
                _logger.LogInformation("命令 {CommandId} 超时重发成功（第 {Attempts} 次）", command.CommandId, attempts);
            }
            else
            {
                // 发布失败不改 Pending 语义：保持 Pending，下一轮再试（attempts 已累计，防长期卡死）
                await store.UpdateStatusAsync(command.CommandId, CommandStatus.Pending,
                    error: result.Error?.Message, attempts: attempts, ct: ct);
                _logger.LogWarning("命令 {CommandId} 重发失败: {Error}，保持 Pending 下一轮再试",
                    command.CommandId, result.Error?.Message);
            }
        }
    }
}
