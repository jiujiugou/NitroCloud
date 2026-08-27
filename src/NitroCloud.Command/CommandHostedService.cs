using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NitroCloud.Command;

/// <summary>
/// Command 后台宿主服务（ADR-010 D1/D6）：唯一宿主 = Api，随宿主启停。
/// 两个并行循环（<see cref="Task.WhenAll"/> 联合调度）：
/// - MQTT 收发循环：<see cref="MqttCommandClient.RunAsync"/>（连接 + 订阅 commands/ack + 断线重连）；
/// - 超时扫描循环：按 <see cref="CommandOptions.PollIntervalMs"/> 轮询 <see cref="CommandManager.ScanInFlightAsync"/>。
/// 回执事件订阅：<see cref="MqttCommandClient.AckMessageReceived"/> → <see cref="CommandManager.HandleAckAsync"/>。
/// </summary>
public sealed class CommandHostedService : BackgroundService
{
    /// <summary>Command MQTT 客户端（连接/订阅/回执事件）</summary>
    private readonly MqttCommandClient _client;
    /// <summary>命令状态机核心（回执处理 + 超时重试）</summary>
    private readonly CommandManager _manager;
    /// <summary>配置快照</summary>
    private readonly CommandOptions _options;
    /// <summary>日志</summary>
    private readonly ILogger<CommandHostedService> _logger;

    /// <summary>创建 Command 宿主服务</summary>
    public CommandHostedService(
        MqttCommandClient client,
        CommandManager manager,
        IOptions<CommandOptions> options,
        ILogger<CommandHostedService> logger)
    {
        _client = client;
        _manager = manager;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _client.AckMessageReceived += (payload, _) => _manager.HandleAckAsync(payload);

        var mqttLoop = _client.RunAsync(stoppingToken);
        var scanLoop = RunScanLoopAsync(stoppingToken);
        await Task.WhenAll(mqttLoop, scanLoop);
    }

    /// <summary>
    /// 超时扫描循环：固定间隔轮询 <see cref="CommandManager.ScanInFlightAsync"/>。
    /// 单次扫描异常被捕获记错误日志，不影响下一轮（扫描不是主路径，不应因一次异常杀掉服务）。
    /// </summary>
    /// <param name="ct">停止令牌；置位时退出循环</param>
    private async Task RunScanLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(100, _options.PollIntervalMs)));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    await _manager.ScanInFlightAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "命令扫描循环异常");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // 正常停机：PeriodicTimer.WaitForNextTickAsync 在取消时抛 OCE
        }
    }
}
