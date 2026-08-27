namespace NitroCloud.Command;

/// <summary>
/// Command 模块配置（绑定 appsettings 的 <c>Command</c> 段，环境变量 <c>Command__*</c> 可覆盖，ADR-010 D1/D5）。
/// 与 Ingest 同构：本类保持配置原样不校验，由消费方在构造时钳制数值边界。
/// </summary>
public sealed class CommandOptions
{
    /// <summary>MQTT broker 主机名 / IP（默认 <c>localhost</c>）</summary>
    public string MqttHost { get; set; } = "localhost";

    /// <summary>MQTT broker 端口（默认 1883，标准明文端口）</summary>
    public int MqttPort { get; set; } = 1883;

    /// <summary>
    /// MQTT ClientId（默认 <c>nitrocloud-command-{进程号}</c>）。
    /// 借进程号保证多实例 / 重启时 clientId 唯一，避免 broker 因同 id 互踢（session takeover）。
    /// </summary>
    public string ClientId { get; set; } = $"nitrocloud-command-{Environment.ProcessId}";

    /// <summary>断线重连延迟毫秒（默认 3000）；消费方钳制为至少 1ms</summary>
    public int ReconnectDelayMs { get; set; } = 3000;

    /// <summary>命令超时秒（默认 10，与前端 ACK_TIMEOUT_MS 对齐）；Sent/Pending 超此未进展即重发/判超时</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>最大重试次数（默认 3；Attempts 达此值仍未回执标 Timeout）；消费方钳制为至少 1</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>后台扫描轮询间隔毫秒（默认 1000）；消费方钳制为至少 100ms</summary>
    public int PollIntervalMs { get; set; } = 1000;
}
