using Serilog;
using Serilog.Events;

namespace NitroCloud.Telemetry;

/// <summary>
/// Serilog 日志配置助手（ADR-008 D8：控制台 + 文件，复用网关模式）。
/// 由宿主（Api）在启动时调用 <see cref="Configure"/> 生成配置，避免每个模块重复拼装。
/// </summary>
public static class SerilogSetup
{
    /// <summary>
    /// 构造 Serilog 配置：控制台（标准格式）+ 文件（CompactJson，按天滚动，保留 30 天）。
    /// 日志目录默认 logs/，可用环境变量 NITROCLOUD_LOG_DIR 覆盖。
    /// </summary>
    public static LoggerConfiguration Configure(string logDir = "logs")
    {
        logDir = Environment.GetEnvironmentVariable("NITROCLOUD_LOG_DIR") ?? logDir;
        Directory.CreateDirectory(logDir);

        return new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.AspNetCore.SignalR", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .WriteTo.Console()
            .WriteTo.File(
                Path.Combine(logDir, "nitrocloud-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:O} [{Level:u3}] {Message:lj}{NewLine}{Exception}");
    }
}
