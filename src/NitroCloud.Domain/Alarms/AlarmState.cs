namespace NitroCloud.Domain.Alarms;

/// <summary>
/// 云侧告警生命周期状态。
/// 网关上行 state 为 Active/Resolved 等原始状态；云侧在此基础上增加 Acknowledged（云端确认）。
/// 状态机：Active → Acknowledged → Resolved。
/// </summary>
public enum AlarmState
{
    /// <summary>已触发，当前活跃（来自网关）</summary>
    Active,

    /// <summary>操作员已在云端确认</summary>
    Acknowledged,

    /// <summary>已恢复（来自网关 state=Resolved 或复位）</summary>
    Resolved
}
