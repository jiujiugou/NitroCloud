namespace NitroCloud.Domain.Alarms;

/// <summary>
/// 告警严重性等级（与 NitroGateway.Alarm.AlarmSeverity 对齐，以网关侧为准）。
/// </summary>
public enum AlarmSeverity
{
    /// <summary>信息通知，无需处理</summary>
    Info,

    /// <summary>警告，需要关注</summary>
    Warning,

    /// <summary>严重，需要立即处理</summary>
    Critical,

    /// <summary>紧急，存在安全或设备损毁风险</summary>
    Emergency
}
