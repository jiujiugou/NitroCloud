namespace NitroCloud.Shared;

/// <summary>
/// 上行/下行 topic 分类（ADR-008 D4 数据流 + DESIGN.md §4 契约）。
/// 上行：measurements / alarms；下行：commands / commands/ack。
/// </summary>
public enum TopicKind
{
    /// <summary>无法识别的 topic 形态</summary>
    Unknown,

    /// <summary>上行测量批量：nitrogateway/{siteId}/{deviceId}/measurements</summary>
    Measurements,

    /// <summary>上行告警：nitrogateway/{siteId}/{deviceId}/alarms</summary>
    Alarms,

    /// <summary>下行命令发布：nitrogateway/{siteId}/{deviceId}/commands</summary>
    Commands,

    /// <summary>下行命令回执：nitrogateway/{siteId}/{deviceId}/commands/ack</summary>
    CommandAck
}
