using NitroCloud.Domain.Commands;
using NitroCloud.Shared;

namespace NitroCloud.Command;

/// <summary>
/// 命令发布抽象（ADR-010 D1）：Api 触发发布命令到 <c>commands</c> topic（QoS1）。
/// 发布失败返回失败结果、**不改 Pending 语义**——由后台扫描重发兜底，保证云端发起不被单次发布失败丢失。
/// </summary>
public interface ICommandDispatcher
{
    /// <summary>
    /// 发布一条命令（序列化为 DESIGN.md §4.3 的 CommandRequest JSON，camelCase）。
    /// </summary>
    /// <param name="command">命令记录（取 CommandId/Type/PointId/Value/RequestedAt/SiteId/DeviceId 组装载荷与 topic）</param>
    /// <param name="ct">取消令牌；取消时抛 <see cref="OperationCanceledException"/></param>
    /// <returns>成功 = 已发布；失败 = 携带 Communication 类 <see cref="OperationalError"/>（未连接 / 发布异常）</returns>
    Task<OperationResult> DispatchAsync(CommandRecord command, CancellationToken ct = default);
}
