using Microsoft.Extensions.Logging;
using NitroCloud.Shared;
using Polly;
using Polly.Retry;

namespace NitroCloud.Ingest;

/// <summary>
/// MQTT 接入连接重试策略（Polly 8 实现，ADR-008 D5 断线重连补充）：
/// 连接/订阅失败按配置重试（默认 3 次，重试间隔复用 <see cref="IngestOptions.ReconnectDelayMs"/>，固定退避）；
/// 重试耗尽仍失败时，把 <see cref="OperationalError"/>（Communication）装进 <see cref="OperationResult"/> 返回，
/// 由调用方记录错误日志；每次重试也会输出告警日志。
/// 独立成类便于单元测试：被测对象只依赖委托与日志，不依赖真实 broker。
/// </summary>
public sealed class MqttConnectRetryPolicy
{
    // Polly 弹性管线（本类唯一状态）；ExecuteAsync 每次调用经其执行，重试逻辑与间隔由构造时选项固化
    private readonly ResiliencePipeline<OperationResult> _pipeline;

    /// <summary>最大重试次数（首次尝试不计入）</summary>
    public int MaxRetries { get; }

    /// <summary>创建 MQTT 连接重试策略</summary>
    /// <param name="maxRetries">最大重试次数（默认 3，即首次 + 3 次重试 = 最多 4 次尝试）</param>
    /// <param name="retryDelay">单次重试间隔（复用 ReconnectDelayMs；非正值时回退 100ms）</param>
    /// <param name="logger">每次重试时输出告警日志</param>
    public MqttConnectRetryPolicy(int maxRetries, TimeSpan retryDelay, ILogger logger)
    {
        MaxRetries = Math.Max(0, maxRetries);

        // MaxRetries = 0 时退化为直通执行（不重试）；Polly 校验 MaxRetryAttempts 至少为 1，故为 0 时不挂重试策略
        var builder = new ResiliencePipelineBuilder<OperationResult>();
        if (MaxRetries > 0)
        {
            var options = new RetryStrategyOptions<OperationResult>
            {
                MaxRetryAttempts = MaxRetries,
                Delay = retryDelay <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(100) : retryDelay,
                BackoffType = DelayBackoffType.Constant,
                // 连接/订阅返回失败结果即视为可重试（异常已在 ConnectAndSubscribeAsync 内转成 OperationResult）
                ShouldHandle = new PredicateBuilder<OperationResult>().HandleResult(r => r.IsFailure),
                OnRetry = args =>
                {
                    logger.LogWarning(
                        "Ingest MQTT 连接失败，第 {Attempt}/{MaxRetries} 次重试（{Delay}ms 后）: {Error}",
                        args.AttemptNumber, MaxRetries, args.RetryDelay.TotalMilliseconds, args.Outcome.Result?.Error);
                    return default;
                }
            };

            builder.AddRetry(options);
        }

        _pipeline = builder.Build();
    }

    /// <summary>
    /// 带重试执行连接操作：成功返回成功结果；重试耗尽仍失败返回携带 <see cref="OperationalError"/> 的失败结果；
    /// 取消令牌触发时向上抛 <see cref="OperationCanceledException"/>（不吞取消）。
    /// </summary>
    /// <param name="action">执行「连接 + 订阅」的异步委托，须把异常/非成功状态收敛为 <see cref="OperationResult"/></param>
    /// <param name="ct">取消令牌；重试间隔等待与委托执行共用该令牌，取消立即中断重试</param>
    public async Task<OperationResult> ExecuteAsync(
        Func<CancellationToken, Task<OperationResult>> action,
        CancellationToken ct = default)
    {
        return await _pipeline.ExecuteAsync(
            token => new ValueTask<OperationResult>(action(token)),
            ct);
    }
}
