namespace NitroCloud.Shared;

/// <summary>
/// topic 解析/构造工具（ADR-008 D2：Shared 提供 TopicUtil）。
/// 契约（以 NitroGateway 侧为准）：
///   - 上行测量：nitrogateway/{siteId}/{deviceId}/measurements
///   - 上行告警：nitrogateway/{siteId}/{deviceId}/alarms
///   - 下行命令：nitrogateway/{siteId}/{deviceId}/commands
///   - 下行回执：nitrogateway/{siteId}/{deviceId}/commands/ack
/// 订阅端可用通配：nitrogateway/+/+/measurements（ADR-008 D5：通配天然多站点）。
/// </summary>
public static class TopicUtil
{
    /// <summary>topic 根段（固定前缀），与网关发布一致</summary>
    public const string Root = "nitrogateway";

    /// <summary>上行测量订阅通配（ADR-008 D5）</summary>
    public const string MeasurementsSubscription = "nitrogateway/+/+/measurements";

    /// <summary>上行告警订阅通配（ADR-008 D5）</summary>
    public const string AlarmsSubscription = "nitrogateway/+/+/alarms";

    /// <summary>下行命令回执订阅通配（CommandManager 订阅）</summary>
    public const string CommandAckSubscription = "nitrogateway/+/+/commands/ack";

    /// <summary>回执 topic 后缀（相对 deviceId）</summary>
    public const string AckSuffix = "commands/ack";

    /// <summary>
    /// topic 解析结果：分类 + siteId（第三段）+ deviceId（第四段）。
    /// </summary>
    public readonly record struct ParsedTopic(TopicKind Kind, string SiteId, string DeviceId);

    /// <summary>
    /// 解析单个（非通配）topic。格式不符或段数不足返回 null。
    /// 注意：siteId/deviceId 若带 MQTT 通配符（+/#）视为非法，返回 null。
    /// </summary>
    public static ParsedTopic? Parse(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
            return null;

        // 按 '/' 切分；commands/ack 是五段，其余是四段
        var parts = topic.Split('/');
        if (parts.Length < 4 || parts[0] != Root)
            return null;

        var siteId = parts[1];
        var deviceId = parts[2];
        if (string.IsNullOrWhiteSpace(siteId) || string.IsNullOrWhiteSpace(deviceId) ||
            siteId.Contains('+') || siteId.Contains('#') ||
            deviceId.Contains('+') || deviceId.Contains('#'))
            return null;

        var kind = parts[3] switch
        {
            "measurements" => parts.Length == 4 ? TopicKind.Measurements : TopicKind.Unknown,
            "alarms" => parts.Length == 4 ? TopicKind.Alarms : TopicKind.Unknown,
            "commands" => parts.Length == 4
                ? TopicKind.Commands
                : (parts.Length == 5 && parts[4] == "ack" ? TopicKind.CommandAck : TopicKind.Unknown),
            _ => TopicKind.Unknown
        };

        return kind == TopicKind.Unknown ? null : new ParsedTopic(kind, siteId, deviceId);
    }

    /// <summary>构造上行测量 topic（供模拟器/测试复用）</summary>
    public static string Measurements(string siteId, string deviceId) => $"{Root}/{siteId}/{deviceId}/measurements";

    /// <summary>构造上行告警 topic</summary>
    public static string Alarms(string siteId, string deviceId) => $"{Root}/{siteId}/{deviceId}/alarms";

    /// <summary>构造下行命令 topic（云 → 网关）</summary>
    public static string Commands(string siteId, string deviceId) => $"{Root}/{siteId}/{deviceId}/commands";

    /// <summary>构造下行命令回执 topic（网关 → 云）</summary>
    public static string CommandAck(string siteId, string deviceId) => $"{Root}/{siteId}/{deviceId}/commands/ack";
}
