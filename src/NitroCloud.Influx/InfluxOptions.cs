namespace NitroCloud.Influx;

/// <summary>
/// InfluxDB 连接与写入配置（appsettings 段 <c>Influx</c>，可用环境变量 Influx__* 覆盖）。
/// </summary>
public sealed class InfluxOptions
{
    /// <summary>InfluxDB 服务地址（默认本机 8086）</summary>
    public string Url { get; set; } = "http://localhost:8086";

    /// <summary>访问令牌（InfluxDB 2.x token）</summary>
    public string Token { get; set; } = "";

    /// <summary>组织（默认 nitrocloud）</summary>
    public string Org { get; set; } = "nitrocloud";

    /// <summary>bucket（时序只进此 bucket，ADR-001 载荷墙）</summary>
    public string Bucket { get; set; } = "nitrocloud";

    /// <summary>measurement（默认 device_point）</summary>
    public string Measurement { get; set; } = "device_point";

    /// <summary>写请求超时秒数（默认 10）</summary>
    public int TimeoutSeconds { get; set; } = 10;
}
